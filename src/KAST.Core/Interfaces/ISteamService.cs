using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface ISteamService
{
    // ── Auth ──
    Task<bool> LoginAnonymousAsync(CancellationToken ct = default);
    Task<bool> LoginWithTokenAsync(string username, string refreshToken, CancellationToken ct = default);
    Task<SteamQrAuthSession> BeginQrLoginAsync(CancellationToken ct = default);
    Task<bool> PollQrLoginAsync(SteamQrAuthSession session, CancellationToken ct = default);
    Task LogoutAsync();

    // ── State ──
    bool IsAuthenticated { get; }          // true = logged in with a real account
    string? CurrentUsername { get; }       // Steam account name (null when anonymous)
    SteamUserProfile? Profile { get; }     // persona info once SteamFriends responds
    event Action? AuthStateChanged;

    // ── Steam operations ──
    Task DownloadWorkshopItemAsync(long workshopId, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default);
    Task DownloadAppAsync(uint appId, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<WorkshopItemInfo?> GetWorkshopItemInfoAsync(long workshopId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkshopItemInfo>> SearchWorkshopAsync(string query, int count = 20, CancellationToken ct = default);
}

public class SteamUserProfile
{
    public ulong SteamId { get; set; }
    public string PersonaName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

public class SteamQrAuthSession
{
    public string ChallengeUrl { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public Action<string>? ChallengeUrlChanged { get; set; }
}

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
