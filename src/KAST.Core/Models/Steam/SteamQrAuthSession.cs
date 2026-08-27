namespace KAST.Core.Models;

public class SteamQrAuthSession
{
    public string ChallengeUrl { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public Action<string>? ChallengeUrlChanged { get; set; }
}