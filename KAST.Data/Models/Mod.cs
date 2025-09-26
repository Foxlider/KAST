using System.ComponentModel.DataAnnotations;

namespace KAST.Data.Models
{
    public class Mod
    {
        [Key]
        public Guid Id { get; set; }

        /// <summary>
        /// Steam Workshop ID for steam mods, null for local mods
        /// </summary>
        public string? SteamId { get; set; }

        /// <summary>
        /// Display name of the mod
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Author of the mod
        /// </summary>
        public string? Author { get; set; }

        /// <summary>
        /// Local file path to the mod
        /// </summary>
        [Required]
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Size of the mod in bytes
        /// </summary>
        public long SizeBytes { get; set; }

        /// <summary>
        /// Whether this is a local mod or from Steam Workshop
        /// </summary>
        public bool IsLocal { get; set; }

        /// <summary>
        /// Last time the mod was updated
        /// </summary>
        public DateTime? LastUpdated { get; set; }

        /// <summary>
        /// Current version/revision of the mod
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Whether the mod is currently enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Creation timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<ModProfile> ModProfiles { get; set; } = new List<ModProfile>();
    }
}