using KAST.Data.Models;

namespace KAST.Core.Interfaces
{
    public interface IConfigService
    {
        Task<KastSettings> GetConfigAsync();
        Task UpdateConfigAsync(KastSettings config);
    }
}
