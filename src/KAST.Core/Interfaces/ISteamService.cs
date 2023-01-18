using KAST.Data.Models;

namespace KAST.Core.Interfaces
{
    public interface ISteamService
    {
        Task<SteamMod> GetModInfo(ulong id);
    }
}
