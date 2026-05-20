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
    public DateTime? LastChecked { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Manifest ID of the locally installed version (0 = not tracked).</summary>
    public ulong InstalledManifestId { get; set; }
    /// <summary>Latest manifest ID fetched from Steam (0 = not yet checked).</summary>
    public ulong SteamManifestId { get; set; }

    public bool IsClientSide { get; set; }
    public bool IsServerSide { get; set; } = true;

    public ICollection<ServerInstanceMod> ServerInstances { get; set; } = [];
}
