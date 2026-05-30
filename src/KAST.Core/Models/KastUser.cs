using System.ComponentModel.DataAnnotations;

namespace KAST.Core.Models;

public class KastUser
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(64)]
    public string NormalizedUsername { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? AvatarFileName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
}
