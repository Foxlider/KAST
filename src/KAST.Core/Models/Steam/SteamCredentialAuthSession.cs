namespace KAST.Core.Models;

public class SteamCredentialAuthSession
{
    public string? ErrorMessage { get; set; }
    public bool RequiresGuardCode { get; set; }
    public string? GuardCodePrompt { get; set; }
    public bool WaitingForDeviceConfirmation { get; set; }
    public Action? StateChanged { get; set; }
}