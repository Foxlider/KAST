namespace KAST.Core.Models;

public sealed record DownloadFileProgress(
    string FileName,
    long BytesDownloaded,
    long TotalBytes,
    double ProgressPercent,
    double BytesPerSecond,
    bool IsComplete = false,
    bool IsSkipped = false,
    string Status = "Downloading");
