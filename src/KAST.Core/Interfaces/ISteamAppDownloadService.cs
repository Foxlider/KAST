using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface ISteamAppDownloadService
{
    Task<SteamAppDownloadResult> DownloadAppAsync(
        SteamAppDownloadRequest request,
        IProgress<SteamDownloadProgress>? progress = null,
        CancellationToken ct = default);
}