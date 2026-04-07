using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface ISteamService
{
    Task<bool> LoginAnonymousAsync(CancellationToken ct = default);
    Task<bool> LoginAsync(string username, string password, string? twoFactorCode = null, CancellationToken ct = default);
    Task LogoutAsync();
    bool IsLoggedIn { get; }
    string? CurrentUsername { get; }

    Task DownloadWorkshopItemAsync(long workshopId, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default);
    Task DownloadAppAsync(uint appId, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<WorkshopItemInfo?> GetWorkshopItemInfoAsync(long workshopId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkshopItemInfo>> SearchWorkshopAsync(string query, int count = 20, CancellationToken ct = default);
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
}
