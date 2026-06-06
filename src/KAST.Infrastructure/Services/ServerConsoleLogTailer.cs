using System.Collections.Concurrent;
using System.Text;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public sealed class ServerConsoleLogTailer(
    IAppEventBroadcaster broadcaster,
    IArmaRptEventDetector eventDetector,
    ILogger<ServerConsoleLogTailer> logger,
    IHostEnvironment? hostEnvironment = null) : IServerConsoleLogTailer
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private const string RptPattern = "arma3server_x64_*.rpt";

    private readonly ConcurrentDictionary<int, TailSession> _sessions = new();

    public void StartFollowing(ServerInstance instance, DateTime sessionStartedUtc, bool replayExistingContent)
    {
        if (instance.Id <= 0 || string.IsNullOrWhiteSpace(instance.InstallPath))
            return;

        if (_sessions.ContainsKey(instance.Id))
            return;

        var configDirectory = Path.Combine(GetInstanceInstallDirectory(instance), "KAST", instance.Id.ToString());
        var session = new TailSession(instance.Id, configDirectory, sessionStartedUtc, replayExistingContent);

        if (!_sessions.TryAdd(instance.Id, session))
        {
            session.Dispose();
            return;
        }

        session.Task = Task.Run(() => FollowAsync(session));
    }

    public async Task StopFollowingAsync(int serverInstanceId)
    {
        if (!_sessions.TryRemove(serverInstanceId, out var session))
            return;

        await session.StopAsync();
    }

    private async Task FollowAsync(TailSession session)
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval);
            do
            {
                await DrainLatestRptAsync(session, session.Token);
            }
            while (await timer.WaitForNextTickAsync(session.Token));
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
            await DrainLatestRptAsync(session, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RPT tailer failed for instance {InstanceId}", session.InstanceId);
        }
        finally
        {
            _sessions.TryRemove(session.InstanceId, out _);
            session.Dispose();
        }
    }

    private async Task DrainLatestRptAsync(TailSession session, CancellationToken ct)
    {
        var latest = FindLatestRpt(session);
        if (latest is null)
            return;

        if (!string.Equals(session.CurrentPath, latest.FullName, StringComparison.OrdinalIgnoreCase))
        {
            session.CurrentPath = latest.FullName;
            session.Position = session.ReplayExistingContent ? 0 : latest.Length;
            session.ReplayExistingContent = true;
            session.PendingText = "";
        }
        else if (latest.Length < session.Position)
        {
            session.Position = 0;
            session.PendingText = "";
        }

        if (latest.Length <= session.Position)
            return;

        byte[] bytes;
        await using (var stream = new FileStream(
                         latest.FullName,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.ReadWrite | FileShare.Delete,
                         bufferSize: 8192,
                         FileOptions.SequentialScan | FileOptions.Asynchronous))
        {
            stream.Seek(session.Position, SeekOrigin.Begin);
            var length = checked((int)Math.Min(stream.Length - session.Position, int.MaxValue));
            bytes = new byte[length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(offset, bytes.Length - offset), ct);
                if (read == 0)
                    break;
                offset += read;
            }

            if (offset != bytes.Length)
                Array.Resize(ref bytes, offset);

            session.Position = stream.Position;
        }

        if (bytes.Length == 0)
            return;

        var text = session.PendingText + Encoding.UTF8.GetString(bytes);
        var lines = SplitCompleteLines(text, out var pending);
        session.PendingText = pending;

        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;

            var timestamp = DateTime.UtcNow;
            await broadcaster.BroadcastLogEntryAsync(new LogEntryEvent(session.InstanceId, line, timestamp));
            foreach (var runtimeEvent in eventDetector.Detect(session.InstanceId, line, timestamp))
                await broadcaster.BroadcastServerRuntimeEventAsync(runtimeEvent);
        }
    }

    private static List<string> SplitCompleteLines(string text, out string pending)
    {
        var lines = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;

            var line = text[start..i].TrimEnd('\r');
            lines.Add(line);
            start = i + 1;
        }

        pending = start < text.Length ? text[start..] : "";
        return lines;
    }

    private static FileInfo? FindLatestRpt(TailSession session)
    {
        if (!Directory.Exists(session.ConfigDirectory))
            return null;

        var files = Directory
            .EnumerateFiles(session.ConfigDirectory, RptPattern, SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => !session.ReplayExistingContent || IsSessionCandidate(file, session.SessionStartedUtc))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.CreationTimeUtc)
            .ToList();

        return files.FirstOrDefault();
    }

    private static bool IsSessionCandidate(FileInfo file, DateTime sessionStartedUtc)
        => file.LastWriteTimeUtc >= sessionStartedUtc.AddSeconds(-5) ||
           file.CreationTimeUtc >= sessionStartedUtc.AddSeconds(-5);

    private string GetInstanceInstallDirectory(ServerInstance instance)
    {
        var installPath = Environment.ExpandEnvironmentVariables(instance.InstallPath);
        return Path.IsPathFullyQualified(installPath)
            ? Path.GetFullPath(installPath)
            : Path.GetFullPath(installPath, hostEnvironment?.ContentRootPath ?? AppContext.BaseDirectory);
    }

    private sealed class TailSession(
        int instanceId,
        string configDirectory,
        DateTime sessionStartedUtc,
        bool replayExistingContent) : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        public int InstanceId { get; } = instanceId;
        public string ConfigDirectory { get; } = configDirectory;
        public DateTime SessionStartedUtc { get; } = sessionStartedUtc;
        public bool ReplayExistingContent { get; set; } = replayExistingContent;
        public string? CurrentPath { get; set; }
        public long Position { get; set; }
        public string PendingText { get; set; } = "";
        public CancellationToken Token => _cts.Token;
        public Task? Task { get; set; }

        public async Task StopAsync()
        {
            await _cts.CancelAsync();
            if (Task is not null)
            {
                try { await Task; }
                catch (OperationCanceledException) { }
            }
        }

        public void Dispose() => _cts.Dispose();
    }
}
