using KAST.Core.Enums;

namespace KAST.Core.Models;

public class SteamMod
{
    public int Id { get; set; }
    public long WorkshopId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Author { get; set; }
    public long SizeBytes { get; set; }
    public long ExpectedSizeBytes { get; set; }
    public ModSource Source { get; set; } = ModSource.SteamWorkshop;
    public ModStatus Status { get; set; } = ModStatus.NotInstalled;
    public string LocalPath { get; set; } = string.Empty;
    public DateTime? LastUpdatedSteam { get; set; }
    public DateTime? LastUpdatedLocal { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsClientSide { get; set; }
    public bool IsServerSide { get; set; } = true;

    public ICollection<ServerInstanceMod> ServerInstances { get; set; } = [];
}
