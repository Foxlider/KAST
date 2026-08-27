namespace KAST.Core.Models;

public sealed class SteamAppDownloadResult
{
    public int FilesVerified { get; init; }
    public int FilesDownloaded { get; init; }
    public int DepotsSkipped { get; init; }
    public long TotalBytes { get; init; }
}