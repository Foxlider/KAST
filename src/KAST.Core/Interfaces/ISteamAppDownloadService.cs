using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface ISteamAppDownloadService
{
    Task<SteamAppDownloadResult> DownloadAppAsync(
        SteamAppDownloadRequest request,
        IProgress<SteamDownloadProgress>? operationProgress = null,
        CancellationToken ct = default);
}