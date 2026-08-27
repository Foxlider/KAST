using System.Diagnostics;
using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Telemetry;

namespace KAST.Infrastructure.Services.Content;

/// <summary>
/// Downloads a Steam Workshop mod via SteamKit2.
/// </summary>
public class SteamModInstaller(ISteamService steam, IFileSystemService fs, IOutputSanitizer sanitizer) : IContentInstaller
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
        activity?.SetTag("workshop.id",  request.WorkshopId);

        try
        {
        state.BeginStep(0);
        state.AddLog($"Downloading Workshop item {request.WorkshopId} to {sanitizer.ToDisplayPath(request.DestinationPath)}");

        if (!steam.IsConnected)
        {
            state.AddLog("Connecting to Steam (anonymous)...");
            await steam.LoginAnonymousAsync(ct);
        }
        if (!steam.IsConnected)
            throw new InvalidOperationException("Failed to connect to Steam.");

        var progress = new Progress<double>(pct => state.SetStepProgress(0, pct));
        var installedManifestId = await steam.DownloadWorkshopItemAsync(
            request.WorkshopId, request.DestinationPath, progress, ct,
            Math.Max(DownloadConcurrency.MinimumSteamWorkers, request.MaxParallelDownloads));
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
            activity?.SetStatus(ActivityStatusCode.Error, sanitizer.Sanitize(ex.Message));
            throw;
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
