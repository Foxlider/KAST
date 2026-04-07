using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IModService
{
    Task<IReadOnlyList<SteamMod>> GetAllModsAsync(CancellationToken ct = default);
    Task<SteamMod?> GetModByIdAsync(int id, CancellationToken ct = default);
    Task<SteamMod?> GetModByWorkshopIdAsync(long workshopId, CancellationToken ct = default);
    Task<SteamMod> AddWorkshopModAsync(long workshopId, CancellationToken ct = default);
    Task<SteamMod> ImportLocalModAsync(string path, string name, CancellationToken ct = default);
    Task DeleteModAsync(int id, CancellationToken ct = default);
    Task<SteamMod> UpdateModAsync(SteamMod mod, CancellationToken ct = default);
    Task DownloadModAsync(int id, CancellationToken ct = default);
    Task UpdateModFilesAsync(int id, CancellationToken ct = default);
    Task CheckForUpdatesAsync(CancellationToken ct = default);
}
