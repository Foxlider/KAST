using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface ISteamWorkshopDownloadService
{
    Task<ulong> DownloadWorkshopItemAsync(
        long workshopId,
        string destinationPath,
        IProgress<double>? progress = null,
        int maxParallelDownloads = DownloadConcurrency.DefaultSteamWorkers,
        CancellationToken ct = default);
}