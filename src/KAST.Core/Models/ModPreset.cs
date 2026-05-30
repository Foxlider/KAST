using KAST.Core.Enums;

namespace KAST.Core.Models;

public class ModPreset
{
    public int Id { get; set; }
    public int ServerInstanceId { get; set; }
    public ServerInstance ServerInstance { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public ModPresetType Type { get; set; } = ModPresetType.Kast;
    public string? RawHtmlContent { get; set; }
    public string? ImagePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAppliedAt { get; set; }

    public ICollection<ModPresetEntry> Entries { get; set; } = [];
    public ICollection<Mission> Missions { get; set; } = [];
}
