namespace KAST.Core.Models;

public class ServerInstanceMod
{
    public int ServerInstanceId { get; set; }
    public ServerInstance ServerInstance { get; set; } = null!;

    public int SteamModId { get; set; }
    public SteamMod SteamMod { get; set; } = null!;

    public bool IsServerSide { get; set; } = true;
    public int LoadOrder { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
