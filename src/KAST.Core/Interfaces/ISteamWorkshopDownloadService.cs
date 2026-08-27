using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface ISteamWorkshopDownloadService
{
    Task<ulong> DownloadWorkshopItemAsync(
        long workshopId,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default,
        int maxParallelDownloads = DownloadConcurrency.DefaultSteamWorkers);
}