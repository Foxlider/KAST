using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface ISteamAuthenticationService
{
    Task<bool> LoginAnonymousAsync(CancellationToken ct = default);
    Task<bool> LoginWithTokenAsync(string username, string refreshToken, CancellationToken ct = default);
    Task<SteamQrAuthSession> BeginQrLoginAsync(CancellationToken ct = default);
    Task<bool> PollQrLoginAsync(SteamQrAuthSession session, CancellationToken ct = default);
    Task<SteamCredentialAuthSession> BeginCredentialLoginAsync(string username, string password, CancellationToken ct = default);
    Task<bool> SubmitCredentialGuardCodeAsync(SteamCredentialAuthSession session, string code, CancellationToken ct = default);
    Task<bool> PollCredentialLoginAsync(SteamCredentialAuthSession session, CancellationToken ct = default);
    Task LogoutAsync();

    bool IsAuthenticated { get; }
    bool IsConnected { get; }
    string? CurrentUsername { get; }
    SteamUserProfile? Profile { get; }
    event Action? AuthStateChanged;
}