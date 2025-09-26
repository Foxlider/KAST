using System.ComponentModel.DataAnnotations;

namespace KAST.Data.Models
{
    public class Profile
    {
        [Key]
        public Guid Id { get; set; }

        /// <summary>
        /// Display name for the profile
        /// </summary>
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional description of the profile
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Server configuration (serialized JSON)
        /// </summary>
        public string? ServerConfig { get; set; }

        /// <summary>
        /// Server profile configuration (serialized JSON)
        /// </summary>
        public string? ServerProfile { get; set; }

        /// <summary>
        /// Performance configuration (serialized JSON)
        /// </summary>
        public string? PerformanceConfig { get; set; }

        /// <summary>
        /// Command line arguments for the server
        /// </summary>
        public string? CommandLineArgs { get; set; }

        /// <summary>
        /// Whether this profile is currently active
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Creation timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last modified timestamp
        /// </summary>
        public DateTime LastModified { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Associated server ID
        /// </summary>
        public Guid? ServerId { get; set; }

        // Navigation properties
        public Server? Server { get; set; }
        public ICollection<ModProfile> ModProfiles { get; set; } = new List<ModProfile>();
        public ICollection<ProfileHistory> ProfileHistories { get; set; } = new List<ProfileHistory>();
    }
}