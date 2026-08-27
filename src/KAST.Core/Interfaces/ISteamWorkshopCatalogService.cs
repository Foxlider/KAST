using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface ISteamWorkshopCatalogService
{
    Task<WorkshopItemInfo?> GetWorkshopItemInfoAsync(long workshopId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkshopItemInfo>> SearchWorkshopAsync(string query, int count = 20, CancellationToken ct = default);
}