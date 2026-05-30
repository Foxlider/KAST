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
public class SteamModInstaller(ISteamService steam, IFileSystemService fs, IAppEventBroadcaster? broadcaster = null) : IContentInstaller
{
    public ContentType Type => ContentType.SteamMod;

    public IReadOnlyList<ContentStep> PlanSteps(ContentInstallRequest request) =>
    [
        new ContentStep { Name = "Download from Workshop", Detail = $"ID {request.WorkshopId}" },
        new ContentStep { Name = "Calculate size" }
    ];

    public async Task InstallAsync(ContentInstallRequest request, ContentInstallState state, CancellationToken ct)
    {
        using var activity = KastActivitySources.Content.StartActivity(
            "kast.steam.mod_download", ActivityKind.Internal);
        activity?.SetTag("workshop.id", request.WorkshopId);
        activity?.SetTag("destination", request.DestinationPath);

        try
        {
            state.BeginStep(0);
            state.AddLog($"Downloading Workshop item {request.WorkshopId} → {request.DestinationPath}");

            if (!steam.IsConnected)
            {
                state.AddLog("Connecting to Steam (anonymous)...");
                await steam.LoginAnonymousAsync(ct);
            }
            if (!steam.IsConnected)
                throw new InvalidOperationException("Failed to connect to Steam.");

            var progress = new Progress<double>(pct =>
            {
                state.SetStepProgress(0, pct);
                if (request.ModId <= 0 || broadcaster is null)
                    return;

                var bytesDownloaded = request.ExpectedSizeBytes > 0
                    ? (long)(pct / 100.0 * request.ExpectedSizeBytes)
                    : 0;
                _ = broadcaster.BroadcastDownloadProgressAsync(new ModDownloadProgressEvent(
                    request.ModId,
                    request.WorkshopId,
                    pct,
                    bytesDownloaded,
                    request.ExpectedSizeBytes));
            });
            state.AddLog($"Parallel chunk downloads: {Math.Max(1, request.MaxParallelDownloads)}.");
            var installedManifestId = await steam.DownloadWorkshopItemAsync(
                request.WorkshopId,
                request.DestinationPath,
                progress,
                request.MaxParallelDownloads,
                ct);
            state.InstalledManifestId = installedManifestId;

            state.CompleteStep(0);
            state.AddLog("Download complete.");

            state.BeginStep(1);
            state.AddLog("Calculating size on disk...");

            long size = fs.GetDirectorySize(request.DestinationPath);
            state.AddLog($"Size: {size / (1024.0 * 1024.0):F1} MiB");
            activity?.SetTag("mod.size_bytes", size);

            state.CompleteStep(1);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public IReadOnlyList<ContentValidationResult> Validate(ContentInstallRequest request)
    {
        var results = new List<ContentValidationResult>();
        bool exists = Directory.Exists(request.DestinationPath);
        results.Add(new("Mod directory", exists, request.DestinationPath));

        if (!exists) return results;

        long size = fs.GetDirectorySize(request.DestinationPath);
        results.Add(new("Files on disk", size > 0, $"{size / (1024.0 * 1024.0):F1} MiB"));

        return results;
    }
}
