using KAST.Core.Models;

namespace KAST.Core.Interfaces
{
    public interface IModsService
    {
        Task<SteamMod[]> GetSteamModsAsync();
        Task<LocalMod[]> GetLocalModsAsync();

    }
}
