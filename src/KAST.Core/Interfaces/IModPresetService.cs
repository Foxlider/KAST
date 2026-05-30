using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IModPresetService
{
    Task<IReadOnlyList<ModPreset>> GetPresetsForInstanceAsync(int instanceId, CancellationToken ct = default);
    Task<ModPreset?> GetPresetByIdAsync(int id, CancellationToken ct = default);
    Task<ModPreset> CreatePresetAsync(ModPreset preset, CancellationToken ct = default);
    Task<ModPreset> UpdatePresetAsync(ModPreset preset, CancellationToken ct = default);
    Task DeletePresetAsync(int id, CancellationToken ct = default);

    Task<ModPreset> ImportFromArmaHtmlAsync(int instanceId, string name, string rawHtml, CancellationToken ct = default);
    Task ApplyPresetAsync(int presetId, int instanceId, CancellationToken ct = default);
    Task<ModPreset> SaveInstanceAsKastPresetAsync(int instanceId, string name, CancellationToken ct = default);
}
