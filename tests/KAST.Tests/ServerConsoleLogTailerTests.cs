using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KAST.Tests;

public class ServerConsoleLogTailerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kast-rpt-tail-{Guid.NewGuid():N}");
    private readonly CapturingBroadcaster _broadcaster = new();
    private readonly ServerConsoleLogTailer _sut;

    public ServerConsoleLogTailerTests()
    {
        _sut = new ServerConsoleLogTailer(
            _broadcaster,
            new ArmaRptEventDetector(),
            NullLogger<ServerConsoleLogTailer>.Instance);
    }

    [Fact]
    public async Task StartFollowing_NewSession_ReplaysExistingRptFromStart()
    {
        var instance = CreateInstance();
        var rpt = CreateRpt(instance, "19:00:12 Connected to Steam servers\r\n");

        _sut.StartFollowing(instance, File.GetLastWriteTimeUtc(rpt), replayExistingContent: true);

        await WaitForAsync(() => _broadcaster.Logs.Count == 1);
        Assert.Equal("19:00:12 Connected to Steam servers", _broadcaster.Logs[0].Line);
        Assert.Contains(_broadcaster.RuntimeEvents, e => e.Kind == ServerRuntimeEventKind.SteamConnected);
    }

    [Fact]
    public async Task StartFollowing_RecoveryMode_StartsAtEndOfFile()
    {
        var instance = CreateInstance();
        var rpt = CreateRpt(instance, "old line\r\n");

        _sut.StartFollowing(instance, DateTime.UtcNow, replayExistingContent: false);
        await Task.Delay(250);
        await File.AppendAllTextAsync(rpt, "new line\r\n");

        await WaitForAsync(() => _broadcaster.Logs.Count == 1);
        Assert.Equal("new line", _broadcaster.Logs[0].Line);
    }

    [Fact]
    public async Task StartFollowing_BuffersIncompleteLineUntilNewline()
    {
        var instance = CreateInstance();
        var rpt = CreateRpt(instance, "partial");

        _sut.StartFollowing(instance, File.GetLastWriteTimeUtc(rpt), replayExistingContent: true);
        await Task.Delay(250);
        Assert.Empty(_broadcaster.Logs);

        await File.AppendAllTextAsync(rpt, " line\r\n");

        await WaitForAsync(() => _broadcaster.Logs.Count == 1);
        Assert.Equal("partial line", _broadcaster.Logs[0].Line);
    }

    [Fact]
    public async Task StartFollowing_WaitsForDelayedRptCreation()
    {
        var instance = CreateInstance();

        _sut.StartFollowing(instance, DateTime.UtcNow, replayExistingContent: true);
        await Task.Delay(250);
        CreateRpt(instance, "delayed line\r\n");

        await WaitForAsync(() => _broadcaster.Logs.Count == 1);
        Assert.Equal("delayed line", _broadcaster.Logs[0].Line);
    }

    [Fact]
    public async Task StartFollowing_SwitchesToNewerRptFile()
    {
        var instance = CreateInstance();
        CreateRpt(instance, "first file\r\n", "arma3server_x64_2026-06-06_19-00-00.rpt");

        _sut.StartFollowing(instance, DateTime.UtcNow.AddSeconds(-1), replayExistingContent: true);
        await WaitForAsync(() => _broadcaster.Logs.Count == 1);

        await Task.Delay(50);
        CreateRpt(instance, "second file\r\n", "arma3server_x64_2026-06-06_19-01-00.rpt");

        await WaitForAsync(() => _broadcaster.Logs.Count == 2);
        Assert.Equal("second file", _broadcaster.Logs[1].Line);
    }

    [Fact]
    public async Task StartFollowing_ResetsPositionWhenRptIsTruncated()
    {
        var instance = CreateInstance();
        var rpt = CreateRpt(instance, "first line\r\n");

        _sut.StartFollowing(instance, File.GetLastWriteTimeUtc(rpt), replayExistingContent: true);
        await WaitForAsync(() => _broadcaster.Logs.Count == 1);

        await File.WriteAllTextAsync(rpt, "x\r\n");

        await WaitForAsync(() => _broadcaster.Logs.Count == 2);
        Assert.Equal("x", _broadcaster.Logs[1].Line);
    }

    public void Dispose()
    {
        _sut.StopFollowingAsync(1).GetAwaiter().GetResult();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ServerInstance CreateInstance()
    {
        Directory.CreateDirectory(_root);
        return new ServerInstance
        {
            Id = 1,
            Name = "Test",
            InstallPath = _root
        };
    }

    private static string CreateRpt(ServerInstance instance, string content, string? fileName = null)
    {
        var configDir = Path.Combine(instance.InstallPath, "KAST", instance.Id.ToString());
        Directory.CreateDirectory(configDir);
        var path = Path.Combine(configDir, fileName ?? $"arma3server_x64_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.rpt");
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            if (cts.IsCancellationRequested)
                throw new TimeoutException("Condition was not met.");

            await Task.Delay(25);
        }
    }

    private sealed class CapturingBroadcaster : IAppEventBroadcaster
    {
        public List<LogEntryEvent> Logs { get; } = [];
        public List<ServerRuntimeEvent> RuntimeEvents { get; } = [];

        public event Action<ModDownloadProgressEvent>? OnModDownloadProgress;
        public event Action<ModStatusChangedEvent>? OnModStatusChanged;
        public event Action<ServerStatusChangedEvent>? OnServerStatusChanged;

        public Task BroadcastDownloadProgressAsync(ModDownloadProgressEvent progress) => Task.CompletedTask;
        public Task BroadcastModStatusChangedAsync(ModStatusChangedEvent status) => Task.CompletedTask;
        public Task BroadcastServerStatusChangedAsync(ServerStatusChangedEvent status) => Task.CompletedTask;
        public Task BroadcastHostMetricsAsync(HostMetricsUpdatedEvent metrics) => Task.CompletedTask;
        public Task BroadcastInstanceMetricsAsync(InstanceMetricsUpdatedEvent metrics) => Task.CompletedTask;

        public Task BroadcastLogEntryAsync(LogEntryEvent logEntry)
        {
            Logs.Add(logEntry);
            return Task.CompletedTask;
        }

        public Task BroadcastServerRuntimeEventAsync(ServerRuntimeEvent runtimeEvent)
        {
            RuntimeEvents.Add(runtimeEvent);
            return Task.CompletedTask;
        }

        public Task BroadcastExternalProcessesAsync(ExternalProcessesUpdatedEvent processes)
            => Task.CompletedTask;
    }
}
