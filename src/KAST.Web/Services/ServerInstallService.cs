using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
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

    /// <summary>
    /// Creator DLC content is distributed as additional depots within app 233780
    /// on the "creatordlc" branch — NOT via separate AppIDs.
    /// Individual DLC flags control which -mod= directories are loaded at server launch.
    /// </summary>
    private const string CreatorDlcBranch = "creatordlc";

    /// <summary>
    /// DLC metadata. Most DLCs live on the <c>creatordlc</c> branch filtered by depot ID.
    /// Contact is special: it uses its own <c>contact</c> branch (base depots carry the content).
    /// DepotId 0 + no Branch override means the DLC has no server depot yet.
    /// </summary>
    internal static readonly (Func<ServerInstance, bool> Enabled, uint DepotId, string? Branch, string Name, string Folder)[] DlcTable =
    [
        (i => i.ContactDlc,  0u,      "contact",  "Contact",              "contact"),   // own branch, base depots
        (i => i.GmDlc,       233792u, null,        "Global Mobilization",  "gm"),
        (i => i.PfDlc,       233794u, null,        "S.O.G. Prairie Fire",  "vn"),
        (i => i.CslaDlc,     233793u, null,        "CSLA Iron Curtain",    "csla"),
        (i => i.WsDlc,       233795u, null,        "Western Sahara",       "ws"),
        (i => i.SpeDlc,      233788u, null,        "Spearhead 1944",       "spe"),       // "Prague Project 2"
        (i => i.RfDlc,       233799u, null,        "Reaction Forces",      "rf"),
        (i => i.EfDlc,       233798u, null,        "Expeditionary Forces", "ef"),
    ];

    // Active CancellationTokenSources keyed by instance ID
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _active = new();

    public bool IsDownloading(int instanceId) => _active.ContainsKey(instanceId);

    /// <summary>Starts a background download for the given instance. Returns immediately.</summary>
    public void StartDownload(int instanceId, string installPath, ServerInstance instance)
    {
        if (_active.ContainsKey(instanceId))
            return; // already in progress

        var cts = new CancellationTokenSource();
        _active[instanceId] = cts;

        var state = store.GetOrCreate(instanceId);
        state.Reset();
        state.IsDownloading = true;
        state.AddLog($"Queued download for instance {instanceId}.");

        _ = Task.Run(() => RunDownloadAsync(instanceId, installPath, instance, cts.Token));
    }

    /// <summary>Cancels an in-progress download.</summary>
    public void CancelDownload(int instanceId)
    {
        if (_active.TryRemove(instanceId, out var cts))
            cts.Cancel();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task RunDownloadAsync(int instanceId, string installPath, ServerInstance instance, CancellationToken ct)
    {
        var state = store.GetOrCreate(instanceId);

        // Determine which DLC steps are needed (depot on creatordlc OR own branch)
        var enabledDlcs = DlcTable.Where(d => d.Enabled(instance) && (d.DepotId != 0 || d.Branch is not null)).ToList();
        int totalSteps = 1 + enabledDlcs.Count;

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

            // Fetch parallel download setting
            int maxParallelDownloads;
            using (var scope = scopeFactory.CreateScope())
            {
                var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                var kastSettings = await settings.GetSettingsAsync(ct);
                maxParallelDownloads = Math.Max(1, kastSettings.ParallelDownloads);
            }
            state.AddLog($"Parallel downloads: {maxParallelDownloads}.");

            // Warn about DLCs with no known depot and no branch override
            foreach (var dlc in DlcTable.Where(d => d.Enabled(instance) && d.DepotId == 0 && d.Branch is null))
                state.AddLog($"⚠ {dlc.Name}: no server depot available — skipping download.");

            var percentProgress = new Progress<double>(pct => state.SetProgress(pct));
            var logProgress    = new Progress<string>(line => state.AddLog(line));

            // ── Step 1: Base dedicated server (public branch) ────────────────
            state.SetStep(1, totalSteps, "Arma 3 Dedicated Server");
            state.AddLog($"[Step 1/{totalSteps}] Downloading Arma 3 Dedicated Server (AppId {Arma3ServerAppId}, branch: public)...");
            state.SetProgress(0);

            await steam.DownloadAppAsync(Arma3ServerAppId, installPath, percentProgress, logProgress, ignorePlatformFilter: false, branch: "public", maxParallelDownloads: maxParallelDownloads, ct: ct);

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

            state.AddLog($"[Step 1/{totalSteps}] Arma 3 Dedicated Server complete.");

            // ── Steps 2..N: Creator DLC depots (creatordlc branch, filtered) ─
            int stepNum = 1;
            foreach (var dlc in enabledDlcs)
            {
                stepNum++;
                string branch = dlc.Branch ?? CreatorDlcBranch;
                state.SetStep(stepNum, totalSteps, dlc.Name);

                if (dlc.Branch is not null)
                {
                    // DLC has its own branch — download all base depots from that branch
                    state.AddLog($"[Step {stepNum}/{totalSteps}] Downloading {dlc.Name} (branch: {branch}, all depots)...");
                    state.SetProgress(0);
                    await steam.DownloadAppAsync(Arma3ServerAppId, installPath, percentProgress, logProgress,
                        ignorePlatformFilter: false, branch: branch, maxParallelDownloads: maxParallelDownloads, ct: ct);
                }
                else
                {
                    // Standard depot filter on creatordlc branch
                    state.AddLog($"[Step {stepNum}/{totalSteps}] Downloading {dlc.Name} (depot {dlc.DepotId}, branch: {branch})...");
                    state.SetProgress(0);
                    await steam.DownloadAppAsync(Arma3ServerAppId, installPath, percentProgress, logProgress,
                        ignorePlatformFilter: true, branch: branch,
                        depotFilter: [dlc.DepotId], maxParallelDownloads: maxParallelDownloads, ct: ct);
                }
                state.AddLog($"[Step {stepNum}/{totalSteps}] {dlc.Name} complete.");
            }

            var buildId = DateTime.UtcNow.ToString("yyyyMMddHHmm");
            state.AddLog($"All {totalSteps} step(s) complete. Build stamp: {buildId}");
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

    public IReadOnlyList<ValidationResult> ValidateServerFiles(string installPath, ServerInstance? instance = null)
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

        // DLC subfolder checks
        if (instance != null)
        {
            foreach (var dlc in DlcTable.Where(d => d.Enabled(instance)))
            {
                var dlcPath = Path.Combine(installPath, dlc.Folder);
                results.Add(new($"{dlc.Name} ({dlc.Folder}/)", Directory.Exists(dlcPath)));
            }
        }

        return results;
    }
}