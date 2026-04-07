using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface ISettingsService
{
    Task<KastSettings> GetSettingsAsync(CancellationToken ct = default);
    Task UpdateSettingsAsync(KastSettings settings, CancellationToken ct = default);
}
