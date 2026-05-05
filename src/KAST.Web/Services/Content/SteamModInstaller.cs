using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;

namespace KAST.Web.Services.Content;

/// <summary>
/// Installs a Steam Workshop mod: downloads via SteamKit2 into the staging directory.
/// </summary>
public class SteamModInstaller(ISteamService steam, IFileSystemService fs) : IContentInstaller
{
    public ContentType Type => ContentType.SteamMod;

    public IReadOnlyList<ContentStep> PlanSteps(ContentInstallRequest request)
    {
        return
        [
            new ContentStep { Name = "Download from Workshop", Detail = $"ID {request.WorkshopId}" },
            new ContentStep { Name = "Calculate size" }
        ];
    }

    public async Task InstallAsync(ContentInstallRequest request, ContentInstallState state, CancellationToken ct)
    {
        // ── Step 0: Download ──
        state.BeginStep(0);
        state.AddLog($"Downloading Workshop item {request.WorkshopId} → {request.DestinationPath}");

        // Ensure Steam is connected
        if (!steam.IsConnected)
        {
            state.AddLog("Connecting to Steam (anonymous)...");
            await steam.LoginAnonymousAsync(ct);
        }
        if (!steam.IsConnected)
            throw new InvalidOperationException("Failed to connect to Steam.");

        var progress = new Progress<double>(pct => state.SetStepProgress(0, pct));
        await steam.DownloadWorkshopItemAsync(request.WorkshopId, request.DestinationPath, progress, ct);

        state.CompleteStep(0);
        state.AddLog("Download complete.");

        // ── Step 1: Size calc ──
        state.BeginStep(1);
        state.AddLog("Calculating size on disk...");

        long size = fs.GetDirectorySize(request.DestinationPath);
        state.AddLog($"Size: {size / (1024.0 * 1024.0):F1} MiB");

        state.CompleteStep(1);
    }

    public IReadOnlyList<ContentValidationResult> Validate(ContentInstallRequest request)
    {
        var results = new List<ContentValidationResult>();
        bool exists = Directory.Exists(request.DestinationPath);
        results.Add(new("Mod directory", exists, request.DestinationPath));

        if (exists)
        {
            long size = fs.GetDirectorySize(request.DestinationPath);
            results.Add(new("Files on disk", size > 0, $"{size / (1024.0 * 1024.0):F1} MiB"));
        }

        return results;
    }
}
