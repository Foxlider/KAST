using System.ComponentModel.DataAnnotations;

namespace KAST.Data.Models
{
    /// <summary>
    /// Stores historical versions of profile configurations for versioning support
    /// </summary>
    public class ProfileHistory
    {
        [Key]
        public Guid Id { get; set; }

        /// <summary>
        /// Foreign key to the profile this history entry belongs to
        /// </summary>
        [Required]
        public Guid ProfileId { get; set; }

        /// <summary>
        /// Version number for this configuration snapshot
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// Optional comment or description of changes made
        /// </summary>
        public string? ChangeDescription { get; set; }

        /// <summary>
        /// Snapshot of server configuration at this point in time
        /// </summary>
        public string? ServerConfigSnapshot { get; set; }

        /// <summary>
        /// Snapshot of server profile configuration at this point in time
        /// </summary>
        public string? ServerProfileSnapshot { get; set; }

        /// <summary>
        /// Snapshot of performance configuration at this point in time
        /// </summary>
        public string? PerformanceConfigSnapshot { get; set; }

        /// <summary>
        /// Snapshot of command line arguments at this point in time
        /// </summary>
        public string? CommandLineArgsSnapshot { get; set; }

        /// <summary>
        /// JSON array of mod IDs and their order at this point in time
        /// </summary>
        public string? ModsSnapshot { get; set; }

        /// <summary>
        /// When this snapshot was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Who made the changes (for future use)
        /// </summary>
        public string? ChangedBy { get; set; }

        // Navigation properties
        public Profile Profile { get; set; } = null!;
    }
}