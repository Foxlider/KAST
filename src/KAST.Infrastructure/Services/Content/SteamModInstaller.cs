using System.Diagnostics;
using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Telemetry;

namespace KAST.Infrastructure.Services.Content;

/// <summary>
/// Downloads a Steam Workshop mod via SteamKit2.
/// </summary>
public class SteamModInstaller(
    ISteamService steam,
    IFileSystemService fs,
    IOutputSanitizer sanitizer,
    IAppEventBroadcaster? broadcaster = null) : IContentInstaller
{
    public ContentType Type => ContentType.SteamMod;

    public IReadOnlyList<ContentStep> PlanSteps(ContentInstallRequest request) =>
    [
        new ContentStep { Name = "Download from Workshop", Detail = $"ID {request.WorkshopId} → {sanitizer.ToDisplayPath(request.DestinationPath)}" },
        new ContentStep { Name = "Calculate size", Detail = sanitizer.ToDisplayPath(request.DestinationPath) }
    ];

    public async Task InstallAsync(ContentInstallRequest request, ContentInstallState state, CancellationToken ct)
    {
        using var activity = KastActivitySources.Content.StartActivity(
            "kast.steam.mod_download", ActivityKind.Internal);
        activity?.SetTag("workshop.id", request.WorkshopId);

        try
        {
            state.BeginStep(0);
            state.SetStatusMessage("Queued for Steam");
            state.AddLog($"Downloading Workshop item {request.WorkshopId} to {sanitizer.ToDisplayPath(request.DestinationPath)}");

            if (!steam.IsConnected)
            {
                state.SetStatusMessage("Connecting to Steam");
                state.AddLog("Connecting to Steam (anonymous)...");
                await steam.LoginAnonymousAsync(ct);
            }
            if (!steam.IsConnected)
                throw new InvalidOperationException("Failed to connect to Steam.");

            var reporter = new ThrottledModProgressReporter(
                broadcaster,
                request.ModId,
                request.WorkshopId,
                request.ExpectedSizeBytes);
            var progress = new Progress<double>(pct =>
            {
                state.SetStepProgress(0, pct);
                reporter.Report(pct);
            });
            var fileProgress = new Progress<DownloadFileProgress>(file =>
            {
                state.SetFileProgress(file);

                var overallPct = state.Steps.Count > 0 ? state.Steps[0].Progress : 0;
                reporter.Report(overallPct, file);
            });
            var statusProgress = new Progress<string>(state.SetStatusMessage);
            state.AddLog($"Parallel workers: {Math.Max(1, request.MaxParallelDownloads)}.");
            var installedManifestId = await steam.DownloadWorkshopItemAsync(
                request.WorkshopId,
                request.DestinationPath,
                progress,
                fileProgress,
                statusProgress,
                request.MaxParallelDownloads,
                ct);
            state.InstalledManifestId = installedManifestId;

            state.CompleteStep(0);
            reporter.Report(100, force: true);
            state.AddLog("Download complete.");

            state.BeginStep(1);
            state.SetStatusMessage("Calculating size on disk");
            state.AddLog("Calculating size on disk...");

            long size = fs.GetDirectorySize(request.DestinationPath);
            state.AddLog($"Size: {size / (1024.0 * 1024.0):F1} MiB");
            activity?.SetTag("mod.size_bytes", size);

            state.CompleteStep(1);
            state.SetStatusMessage("Installed");
        }
        catch (Exception ex)
        {
            state.SetStatusMessage(ex is OperationCanceledException ? "Cancelled" : "Failed");
            activity?.SetStatus(ActivityStatusCode.Error, sanitizer.Sanitize(ex.Message));
            throw;
        }
    }

    private sealed class ThrottledModProgressReporter(
        IAppEventBroadcaster? broadcaster,
        int modId,
        long workshopId,
        long expectedSizeBytes)
    {
        private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);
        private readonly object _lock = new();
        private DateTime _lastSent = DateTime.MinValue;
        private double _lastPercent = -1;

        public void Report(double percent, DownloadFileProgress? file = null, bool force = false)
        {
            if (modId <= 0 || broadcaster is null)
                return;

            percent = Math.Clamp(percent, 0, 100);
            var now = DateTime.UtcNow;
            lock (_lock)
            {
                if (!force &&
                    percent < 100 &&
                    now - _lastSent < MinimumInterval &&
                    Math.Abs(percent - _lastPercent) < 1)
                {
                    return;
                }

                _lastSent = now;
                _lastPercent = percent;
            }

            var bytesDownloaded = expectedSizeBytes > 0
                ? (long)(percent / 100.0 * expectedSizeBytes)
                : file?.BytesDownloaded ?? 0;
            var files = file is null ? null : new[] { file };
            _ = broadcaster.BroadcastDownloadProgressAsync(new ModDownloadProgressEvent(
                modId,
                workshopId,
                percent,
                bytesDownloaded,
                expectedSizeBytes,
                files));
        }
    }

    public IReadOnlyList<ContentValidationResult> Validate(ContentInstallRequest request)
    {
        var results = new List<ContentValidationResult>();
        bool exists = Directory.Exists(request.DestinationPath);
        var displayPath = sanitizer.ToDisplayPath(request.DestinationPath);
        results.Add(new("Mod directory", exists, exists ? displayPath : $"Missing: {displayPath}"));

        if (!exists) return results;

        long size = fs.GetDirectorySize(request.DestinationPath);
        results.Add(new("Files on disk", size > 0, $"{size / (1024.0 * 1024.0):F1} MiB"));

        return results;
    }
}
