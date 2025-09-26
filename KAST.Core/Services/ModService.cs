using KAST.Data;
using KAST.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace KAST.Core.Services
{
    public class ModService
    {
        private readonly ApplicationDbContext _context;

        public ModService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Get all mods from the database
        /// </summary>
        public async Task<List<Mod>> GetAllModsAsync()
        {
            return await _context.Mods
                .OrderBy(m => m.Name)
                .ToListAsync();
        }

        /// <summary>
        /// Get a specific mod by ID
        /// </summary>
        public async Task<Mod?> GetModByIdAsync(Guid id)
        {
            return await _context.Mods
                .Include(m => m.ModProfiles)
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        /// <summary>
        /// Get a mod by Steam ID
        /// </summary>
        public async Task<Mod?> GetModBySteamIdAsync(string steamId)
        {
            return await _context.Mods
                .FirstOrDefaultAsync(m => m.SteamId == steamId);
        }

        /// <summary>
        /// Add or update a mod
        /// </summary>
        public async Task<Mod> SaveModAsync(Mod mod)
        {
            var existingMod = await _context.Mods
                .FirstOrDefaultAsync(m => m.Id == mod.Id);

            if (existingMod == null)
            {
                _context.Mods.Add(mod);
            }
            else
            {
                _context.Entry(existingMod).CurrentValues.SetValues(mod);
                existingMod.LastUpdated = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return mod;
        }

        /// <summary>
        /// Delete a mod
        /// </summary>
        public async Task<bool> DeleteModAsync(Guid id)
        {
            var mod = await _context.Mods.FindAsync(id);
            if (mod == null) return false;

            _context.Mods.Remove(mod);
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Get mods for a specific profile
        /// </summary>
        public async Task<List<Mod>> GetModsForProfileAsync(Guid profileId)
        {
            return await _context.ModProfiles
                .Where(mp => mp.ProfileId == profileId && mp.IsEnabled)
                .OrderBy(mp => mp.Order)
                .Select(mp => mp.Mod)
                .ToListAsync();
        }

        /// <summary>
        /// Update mod versions based on file system scan
        /// </summary>
        public async Task UpdateModFromFileSystemAsync(string modPath)
        {
            // This would integrate with existing file system scanning logic
            // For now, this is a placeholder for future implementation
            await Task.CompletedTask;
        }
    }
}