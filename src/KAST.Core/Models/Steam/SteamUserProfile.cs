namespace KAST.Core.Models;

public class SteamUserProfile
{
    public ulong SteamId { get; set; }
    public string PersonaName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}