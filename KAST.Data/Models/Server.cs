using System.ComponentModel.DataAnnotations;

namespace KAST.Data.Models
{
    public class Server
    {
        [Key]
        public Guid Id { get; set; }

        /// <summary>
        /// Display name for the server
        /// </summary>
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Installation path for the Arma 3 server
        /// </summary>
        [Required]
        public string InstallPath { get; set; } = string.Empty;

        /// <summary>
        /// Version of the server installation
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// When the server was created/added
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last time the server was updated
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<Profile> Profiles { get; set; } = new List<Profile>();
    }
}
