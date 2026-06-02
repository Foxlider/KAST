using System.ComponentModel.DataAnnotations;

namespace KAST.Core.Models;

public class KastUser
{
    public const string LocalAuthSource = "Local";
    public const string OidcAuthSource = "Oidc";

    public int Id { get; set; }

    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(64)]
    public string NormalizedUsername { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(32)]
    public string AuthSource { get; set; } = LocalAuthSource;

    [MaxLength(64)]
    public string? ExternalProvider { get; set; }

    [MaxLength(512)]
    public string? ExternalIssuer { get; set; }

    [MaxLength(255)]
    public string? ExternalSubject { get; set; }

    [MaxLength(255)]
    public string? AvatarFileName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    public bool IsExternallyManaged => AuthSource != LocalAuthSource;
}
