using KAST.Core.Enums;

namespace KAST.Core.Models;

public class DownloadTask
{
    public int Id { get; set; }
    public int ModId { get; set; }
    public long WorkshopId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public bool IsUpdate { get; set; }
    public string DestinationPath { get; set; } = string.Empty;
    public double ProgressPercent { get; set; }
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public ulong InstalledManifestId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public SteamMod? Mod { get; set; }
}
