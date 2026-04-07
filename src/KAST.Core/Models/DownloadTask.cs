using KAST.Core.Enums;

namespace KAST.Core.Models;

public class DownloadTask
{
    public int Id { get; set; }
    public long WorkshopId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public double ProgressPercent { get; set; }
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
