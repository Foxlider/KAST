namespace KAST.Core.Models;

public class ModPresetEntry
{
    public int ModPresetId { get; set; }
    public ModPreset ModPreset { get; set; } = null!;
    public int SteamModId { get; set; }
    public SteamMod SteamMod { get; set; } = null!;
    public bool IsClientSide { get; set; }
    public bool IsServerSide { get; set; }
    public int LoadOrder { get; set; }
}
