using KAST.Data.Models;

namespace KAST.Core.Interfaces
{
    public interface IModsService
    {
        Task<SteamMod[]> GetSteamModsAsync();
        Task<LocalMod[]> GetLocalModsAsync();
    }
}
