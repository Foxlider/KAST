using KAST.Data;
using KAST.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KAST.Core.Services
{
    public class ProfileService
    {
        private readonly ApplicationDbContext _context;

        public ProfileService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Get all profiles
        /// </summary>
        public async Task<List<Profile>> GetAllProfilesAsync()
        {
            return await _context.Profiles
                .Include(p => p.Server)
                .Include(p => p.ModProfiles)
                    .ThenInclude(mp => mp.Mod)
                .OrderBy(p => p.Name)
                .ToListAsync();
        }

        /// <summary>
        /// Get a specific profile by ID
        /// </summary>
        public async Task<Profile?> GetProfileByIdAsync(Guid id)
        {
            return await _context.Profiles
                .Include(p => p.Server)
                .Include(p => p.ModProfiles)
                    .ThenInclude(mp => mp.Mod)
                .Include(p => p.ProfileHistories)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        /// <summary>
        /// Get the currently active profile
        /// </summary>
        public async Task<Profile?> GetActiveProfileAsync()
        {
            return await _context.Profiles
                .Include(p => p.Server)
                .Include(p => p.ModProfiles)
                    .ThenInclude(mp => mp.Mod)
                .FirstOrDefaultAsync(p => p.IsActive);
        }

        /// <summary>
        /// Create a new profile
        /// </summary>
        public async Task<Profile> CreateProfileAsync(Profile profile)
        {
            profile.Id = Guid.NewGuid();
            profile.CreatedAt = DateTime.UtcNow;
            profile.LastModified = DateTime.UtcNow;

            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            // Create initial history entry
            await CreateHistoryEntryAsync(profile, "Profile created");

            return profile;
        }

        /// <summary>
        /// Update an existing profile
        /// </summary>
        public async Task<Profile> UpdateProfileAsync(Profile profile, string? changeDescription = null)
        {
            var existingProfile = await _context.Profiles
                .Include(p => p.ProfileHistories)
                .FirstOrDefaultAsync(p => p.Id == profile.Id);

            if (existingProfile == null)
                throw new ArgumentException("Profile not found", nameof(profile));

            // Store old values for history
            var oldProfile = new Profile
            {
                ServerConfig = existingProfile.ServerConfig,
                ServerProfile = existingProfile.ServerProfile,
                PerformanceConfig = existingProfile.PerformanceConfig,
                CommandLineArgs = existingProfile.CommandLineArgs
            };

            // Update profile
            _context.Entry(existingProfile).CurrentValues.SetValues(profile);
            existingProfile.LastModified = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Create history entry if there were significant changes
            if (HasSignificantChanges(oldProfile, existingProfile))
            {
                await CreateHistoryEntryAsync(existingProfile, changeDescription ?? "Profile updated");
            }

            return existingProfile;
        }

        /// <summary>
        /// Set a profile as active (and deactivate others)
        /// </summary>
        public async Task<Profile> SetActiveProfileAsync(Guid profileId)
        {
            // Deactivate all profiles
            await _context.Profiles
                .Where(p => p.IsActive)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.IsActive, false));

            // Activate the specified profile
            var profile = await _context.Profiles.FindAsync(profileId);
            if (profile == null)
                throw new ArgumentException("Profile not found", nameof(profileId));

            profile.IsActive = true;
            await _context.SaveChangesAsync();

            return profile;
        }

        /// <summary>
        /// Delete a profile
        /// </summary>
        public async Task<bool> DeleteProfileAsync(Guid id)
        {
            var profile = await _context.Profiles.FindAsync(id);
            if (profile == null) return false;

            _context.Profiles.Remove(profile);
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Add a mod to a profile
        /// </summary>
        public async Task AddModToProfileAsync(Guid profileId, Guid modId, int order = 0)
        {
            // Check if relationship already exists
            var existing = await _context.ModProfiles
                .FirstOrDefaultAsync(mp => mp.ProfileId == profileId && mp.ModId == modId);

            if (existing != null)
            {
                existing.IsEnabled = true;
                existing.Order = order;
            }
            else
            {
                var modProfile = new ModProfile
                {
                    Id = Guid.NewGuid(),
                    ProfileId = profileId,
                    ModId = modId,
                    Order = order,
                    IsEnabled = true,
                    AddedAt = DateTime.UtcNow
                };

                _context.ModProfiles.Add(modProfile);
            }

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Remove a mod from a profile
        /// </summary>
        public async Task RemoveModFromProfileAsync(Guid profileId, Guid modId)
        {
            var modProfile = await _context.ModProfiles
                .FirstOrDefaultAsync(mp => mp.ProfileId == profileId && mp.ModId == modId);

            if (modProfile != null)
            {
                _context.ModProfiles.Remove(modProfile);
                await _context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Update mod order in a profile
        /// </summary>
        public async Task UpdateModOrderInProfileAsync(Guid profileId, Dictionary<Guid, int> modOrders)
        {
            var modProfiles = await _context.ModProfiles
                .Where(mp => mp.ProfileId == profileId)
                .ToListAsync();

            foreach (var modProfile in modProfiles)
            {
                if (modOrders.TryGetValue(modProfile.ModId, out int newOrder))
                {
                    modProfile.Order = newOrder;
                }
            }

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Create a history entry for a profile
        /// </summary>
        private async Task CreateHistoryEntryAsync(Profile profile, string changeDescription)
        {
            var lastVersion = await _context.ProfileHistories
                .Where(ph => ph.ProfileId == profile.Id)
                .MaxAsync(ph => (int?)ph.Version) ?? 0;

            var modsSnapshot = await _context.ModProfiles
                .Where(mp => mp.ProfileId == profile.Id)
                .Select(mp => new { mp.ModId, mp.Order, mp.IsEnabled })
                .ToListAsync();

            var history = new ProfileHistory
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Version = lastVersion + 1,
                ChangeDescription = changeDescription,
                ServerConfigSnapshot = profile.ServerConfig,
                ServerProfileSnapshot = profile.ServerProfile,
                PerformanceConfigSnapshot = profile.PerformanceConfig,
                CommandLineArgsSnapshot = profile.CommandLineArgs,
                ModsSnapshot = JsonSerializer.Serialize(modsSnapshot),
                CreatedAt = DateTime.UtcNow
            };

            _context.ProfileHistories.Add(history);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Check if there are significant changes between two profiles
        /// </summary>
        private static bool HasSignificantChanges(Profile oldProfile, Profile newProfile)
        {
            return oldProfile.ServerConfig != newProfile.ServerConfig ||
                   oldProfile.ServerProfile != newProfile.ServerProfile ||
                   oldProfile.PerformanceConfig != newProfile.PerformanceConfig ||
                   oldProfile.CommandLineArgs != newProfile.CommandLineArgs;
        }

        /// <summary>
        /// Get profile history
        /// </summary>
        public async Task<List<ProfileHistory>> GetProfileHistoryAsync(Guid profileId)
        {
            return await _context.ProfileHistories
                .Where(ph => ph.ProfileId == profileId)
                .OrderByDescending(ph => ph.Version)
                .ToListAsync();
        }

        /// <summary>
        /// Restore profile from a specific version
        /// </summary>
        public async Task<Profile> RestoreProfileFromHistoryAsync(Guid profileId, int version)
        {
            var profile = await GetProfileByIdAsync(profileId);
            var historyEntry = await _context.ProfileHistories
                .FirstOrDefaultAsync(ph => ph.ProfileId == profileId && ph.Version == version);

            if (profile == null || historyEntry == null)
                throw new ArgumentException("Profile or history entry not found");

            // Restore configuration
            profile.ServerConfig = historyEntry.ServerConfigSnapshot;
            profile.ServerProfile = historyEntry.ServerProfileSnapshot;
            profile.PerformanceConfig = historyEntry.PerformanceConfigSnapshot;
            profile.CommandLineArgs = historyEntry.CommandLineArgsSnapshot;

            // Restore mods if snapshot exists
            if (!string.IsNullOrEmpty(historyEntry.ModsSnapshot))
            {
                var modsSnapshot = JsonSerializer.Deserialize<List<dynamic>>(historyEntry.ModsSnapshot);
                // Implementation would restore mod associations here
            }

            return await UpdateProfileAsync(profile, $"Restored from version {version}");
        }
    }
}