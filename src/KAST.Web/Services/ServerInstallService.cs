using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KAST.Web.Services;

/// <summary>
/// Singleton service that runs Arma 3 dedicated server file downloads as background tasks.
/// Uses IServiceScopeFactory for DB access so it can safely outlive any Blazor circuit.
/// </summary>
public class ServerInstallService(
    IServiceScopeFactory scopeFactory,
    ISteamService steam,
    ServerInstallProgressStore store,
    ILogger<ServerInstallService> logger)
{
    private const uint Arma3ServerAppId = 233780;

    // Active CancellationTokenSources keyed by instance ID
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _active = new();

    public bool IsDownloading(int instanceId) => _active.ContainsKey(instanceId);

    /// <summary>Starts a background download for the given instance. Returns immediately.</summary>
    public void StartDownload(int instanceId, string installPath)
    {
        if (_active.ContainsKey(instanceId))
            return; // already in progress

        var cts = new CancellationTokenSource();
        _active[instanceId] = cts;

        var state = store.GetOrCreate(instanceId);
        state.Reset();
        state.IsDownloading = true;
        state.AddLog($"Queued download for instance {instanceId}.");

        _ = Task.Run(() => RunDownloadAsync(instanceId, installPath, cts.Token));
    }

    /// <summary>Cancels an in-progress download.</summary>
    public void CancelDownload(int instanceId)
    {
        if (_active.TryRemove(instanceId, out var cts))
            cts.Cancel();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task RunDownloadAsync(int instanceId, string installPath, CancellationToken ct)
    {
        var state = store.GetOrCreate(instanceId);

        try
        {
            await UpdateStatusAsync(instanceId, ServerInstanceStatus.Downloading, CancellationToken.None);

            // Ensure we have at least an anonymous Steam connection
            if (!steam.IsConnected)
            {
                state.AddLog("Connecting to Steam (anonymous)...");
                await steam.LoginAnonymousAsync(ct);
            }

            if (!steam.IsConnected)
                throw new InvalidOperationException("Failed to connect to Steam.");

            state.AddLog(steam.IsAuthenticated
                ? $"Signed in as {steam.CurrentUsername}."
                : "Connected anonymously.");

            var percentProgress = new Progress<double>(pct => state.SetProgress(pct));
            var logProgress    = new Progress<string>(line => state.AddLog(line));

            await steam.DownloadAppAsync(Arma3ServerAppId, installPath, percentProgress, logProgress, ct);

            // Post-download: set executable bit on Linux/macOS
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var exe = Path.Combine(installPath, "arma3server_x64");
                if (File.Exists(exe))
                {
                    File.SetUnixFileMode(exe,
                        UnixFileMode.UserRead  | UnixFileMode.UserWrite  | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    state.AddLog("Set executable permissions on arma3server_x64.");
                }
            }

            var buildId = DateTime.UtcNow.ToString("yyyyMMddHHmm");
            state.AddLog($"Installation complete. Build stamp: {buildId}");
            state.SetProgress(100);

            await UpdateInstallAsync(instanceId, DateTime.UtcNow, buildId, CancellationToken.None);
            state.IsDownloading = false;
            state.IsComplete = true;
            state.NotifyChanged();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Server install cancelled for instance {Id}", instanceId);
            state.AddLog("Download cancelled.");
            state.IsDownloading = false;
            state.ErrorMessage = "Cancelled";
            state.NotifyChanged();
            await UpdateStatusAsync(instanceId, ServerInstanceStatus.Stopped, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Server install failed for instance {Id}", instanceId);
            state.AddLog($"ERROR: {ex.Message}");
            state.IsDownloading = false;
            state.ErrorMessage = ex.Message;
            state.NotifyChanged();
            await UpdateStatusAsync(instanceId, ServerInstanceStatus.Stopped, CancellationToken.None);
        }
        finally
        {
            _active.TryRemove(instanceId, out _);
        }
    }

    private async Task UpdateStatusAsync(int instanceId, ServerInstanceStatus status, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var instance = await db.ServerInstances.FindAsync([instanceId], ct);
        if (instance is null) return;
        instance.Status = status;
        await db.SaveChangesAsync(ct);
    }

    private async Task UpdateInstallAsync(int instanceId, DateTime installedAt, string buildId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var instance = await db.ServerInstances.FindAsync([instanceId], ct);
        if (instance is null) return;
        instance.InstalledAt    = installedAt;
        instance.InstalledBuildId = buildId;
        instance.Status         = ServerInstanceStatus.Stopped;
        await db.SaveChangesAsync(ct);
    }

    // ── File validation ───────────────────────────────────────────────────────

    public record ValidationResult(string Label, bool OK, string? Detail = null);

    public IReadOnlyList<ValidationResult> ValidateServerFiles(string installPath)
    {
        var results = new List<ValidationResult>();

        bool dirExists = Directory.Exists(installPath);
        results.Add(new("Install directory", dirExists, dirExists ? installPath : "Not found"));

        if (!dirExists)
            return results;

        // Main executable
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "arma3server_x64.exe"
            : "arma3server_x64";
        var exePath = Path.Combine(installPath, exeName);
        results.Add(new("Server executable", File.Exists(exePath), exeName));

        // Core data directories / key files
        var checks = new[]
        {
            ("addons/",          (string?)null),
            ("dta/",             null),
            ("keys/",            null),
        };

        foreach (var (rel, _) in checks)
        {
            var full = Path.Combine(installPath, rel.TrimEnd('/'));
            results.Add(new(rel.TrimEnd('/'), Directory.Exists(full)));
        }

        // Executable bit on Linux
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(exePath))
        {
            var mode = File.GetUnixFileMode(exePath);
            var hasExec = mode.HasFlag(UnixFileMode.UserExecute);
            results.Add(new("Executable permission (+x)", hasExec));
        }

        return results;
    }
}