using KAST.Core.Models;
using KAST.Core.Interfaces;
using KAST.Data;
using Microsoft.EntityFrameworkCore;

namespace KAST.Core.Services
{
    public class ModsService : IModsService
    {
        private readonly KastDbContext _context;
        public ModsService(KastDbContext context)
        {
            _context = context;
        }
        public Task<SteamMod[]> GetSteamModsAsync()
        {
            return _context.SteamMods.Include(m => m.Author).ToArrayAsync();
        }

        public Task<LocalMod[]> GetLocalModsAsync()
        {
            return _context.LocalMods.Include(m => m.Author).ToArrayAsync();
        }
    }
}
