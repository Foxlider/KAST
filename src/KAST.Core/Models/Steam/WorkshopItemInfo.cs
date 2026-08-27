namespace KAST.Core.Models;

public class WorkshopItemInfo
{
    public long WorkshopId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Author { get; set; }
    public long SizeBytes { get; set; }
    public DateTime LastUpdated { get; set; }
    public int Subscriptions { get; set; }
    public uint ConsumerAppId { get; set; }
    public ulong ManifestId { get; set; }
    public List<string> Tags { get; set; } = [];
}