using System.ComponentModel.DataAnnotations;

namespace KAST.Data.Models
{
    /// <summary>
    /// Junction table for many-to-many relationship between Mods and Profiles
    /// </summary>
    public class ModProfile
    {
        [Key]
        public Guid Id { get; set; }

        /// <summary>
        /// Foreign key to Mod
        /// </summary>
        [Required]
        public Guid ModId { get; set; }

        /// <summary>
        /// Foreign key to Profile
        /// </summary>
        [Required]
        public Guid ProfileId { get; set; }

        /// <summary>
        /// Order of the mod in the profile (for launch order)
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Whether this mod is enabled for this specific profile
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// When this mod was added to the profile
        /// </summary>
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Mod Mod { get; set; } = null!;
        public Profile Profile { get; set; } = null!;
    }
}