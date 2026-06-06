using System.Security.Cryptography;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Services.SystemAccounts;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KAST.Infrastructure.Services;

public class UserAccountService(
    KastDbContext db,
    IConfiguration configuration,
    ISystemAccountProvider? systemAccounts = null,
    ISettingsService? settingsService = null) : IUserAccountService
{
    private const int MaxAvatarBytes = 2 * 1024 * 1024;
    private const string DefaultOidcDisplayName = "OpenID Connect";
    private const string DefaultOidcAllowedGroup = "KAST Admins";
    private static readonly HashSet<string> AllowedAvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    };

    public Task<bool> HasAnyUsersAsync(CancellationToken ct = default)
        => db.Users.AnyAsync(ct);

    public Task<KastUser?> GetByIdAsync(int id, CancellationToken ct = default)
        => db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct);

    public Task<KastUser?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        var normalized = NormalizeUsername(username);
        return db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedUsername == normalized && u.IsActive, ct);
    }

    public Task<KastUser?> GetByExternalLoginAsync(string issuer, string subject, CancellationToken ct = default)
        => db.Users.AsNoTracking().FirstOrDefaultAsync(
            u => u.ExternalIssuer == issuer &&
                 u.ExternalSubject == subject &&
                 u.IsActive,
            ct);

    private ISystemAccountProvider SystemAccounts { get; } =
        systemAccounts ?? new UnsupportedSystemAccountProvider("System account provider is not configured.");

    public async Task<IReadOnlyList<KastUser>> GetAllUsersAsync(CancellationToken ct = default)
        => await db.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.Username).ToListAsync(ct);

    public async Task<KastUser> CreateInitialAdminAsync(
        string username,
        string password,
        Stream? avatarStream = null,
        string? avatarFileName = null,
        long avatarLength = 0,
        CancellationToken ct = default)
    {
        if (await HasAnyUsersAsync(ct))
            throw new InvalidOperationException("Initial administrator account already exists.");

        return await CreateAdminAsync(username, password, avatarStream, avatarFileName, avatarLength, ct);
    }

    public async Task<KastUser> CreateAdminAsync(
        string username,
        string password,
        Stream? avatarStream = null,
        string? avatarFileName = null,
        long avatarLength = 0,
        CancellationToken ct = default)
    {
        var normalized = NormalizeUsername(username);
        ValidateUsername(username, normalized);
        ValidatePassword(password);

        if (await db.Users.AnyAsync(u => u.NormalizedUsername == normalized, ct))
            throw new InvalidOperationException("Username is already in use.");

        var user = new KastUser
        {
            Username = username.Trim(),
            NormalizedUsername = normalized,
            PasswordHash = HashPassword(password),
            AuthSource = KastUser.LocalAuthSource,
            AvatarFileName = await SaveAvatarAsync(avatarStream, avatarFileName, avatarLength, ct),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return user;
    }

    public async Task<KastUser> ProvisionOidcAdminAsync(OidcProvisioningRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Issuer))
            throw new InvalidOperationException("OIDC issuer claim is required.");

        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new InvalidOperationException("OIDC subject claim is required.");

        var allowedGroups = GetConfiguredOidcAllowedGroups();
        if (!request.Groups.Any(group => allowedGroups.Contains(group)))
            throw new InvalidOperationException("OIDC user is not a member of an allowed KAST administrator group.");

        var issuer = request.Issuer.Trim();
        var subject = request.Subject.Trim();
        var displayName = FirstNonBlank(request.DisplayName, request.Email, subject);
        var providerName = FirstNonBlank(configuration["Auth:Oidc:DisplayName"], DefaultOidcDisplayName);

        var existing = await db.Users.FirstOrDefaultAsync(
            u => u.ExternalIssuer == issuer &&
                 u.ExternalSubject == subject &&
                 u.IsActive,
            ct);

        if (existing is not null)
        {
            existing.Username = await GetUniqueUsernameAsync(displayName, existing.Id, ct);
            existing.NormalizedUsername = NormalizeUsername(existing.Username);
            existing.AuthSource = KastUser.OidcAuthSource;
            existing.ExternalProvider = providerName;
            existing.LastLoginAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var username = await GetUniqueUsernameAsync(displayName, existingUserId: null, ct);
        var user = new KastUser
        {
            Username = username,
            NormalizedUsername = NormalizeUsername(username),
            PasswordHash = string.Empty,
            AuthSource = KastUser.OidcAuthSource,
            ExternalProvider = providerName,
            ExternalIssuer = issuer,
            ExternalSubject = subject,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public Task<SystemAccountProviderStatus> GetSystemAccountProviderStatusAsync(CancellationToken ct = default)
        => Task.FromResult(SystemAccounts.GetStatus());

    public async Task<IReadOnlyList<SystemAccount>> SearchSystemAccountsAsync(string? query, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        return await SystemAccounts.SearchAccountsAsync(query, settings.SystemAuthDomain, ct);
    }

    public async Task<KastUser> AllowSystemAccountAsync(SystemAccount account, CancellationToken ct = default)
    {
        ValidateSystemAccount(account);

        var existing = await db.Users.FirstOrDefaultAsync(
            u => u.ExternalIssuer == account.Issuer &&
                 u.ExternalSubject == account.Subject,
            ct);

        if (existing is not null)
        {
            existing.Username = await GetUniqueUsernameAsync(account.Username, existing.Id, ct);
            existing.NormalizedUsername = NormalizeUsername(existing.Username);
            existing.PasswordHash = string.Empty;
            existing.AuthSource = KastUser.SystemAuthSource;
            existing.ExternalProvider = account.Provider;
            existing.IsActive = true;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var username = await GetUniqueUsernameAsync(account.Username, existingUserId: null, ct);
        var user = new KastUser
        {
            Username = username,
            NormalizedUsername = NormalizeUsername(username),
            PasswordHash = string.Empty,
            AuthSource = KastUser.SystemAuthSource,
            ExternalProvider = account.Provider,
            ExternalIssuer = account.Issuer,
            ExternalSubject = account.Subject,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task RemoveSystemAccountAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == userId &&
                 u.AuthSource == KastUser.SystemAuthSource &&
                 u.IsActive,
            ct) ?? throw new InvalidOperationException("System account was not found.");

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<KastUser?> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default)
    {
        var normalized = NormalizeUsername(username);
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.NormalizedUsername == normalized &&
                 u.AuthSource == KastUser.LocalAuthSource &&
                 u.IsActive,
            ct);
        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash) || !VerifyPassword(password, user.PasswordHash))
            return null;

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<KastUser?> ValidateSystemCredentialsAsync(string username, string password, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (!settings.SystemAuthEnabled)
            return null;

        var account = await SystemAccounts.ValidateCredentialsAsync(username, password, settings.SystemAuthDomain, ct);
        if (account is null)
            return null;

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.AuthSource == KastUser.SystemAuthSource &&
                 u.ExternalIssuer == account.Issuer &&
                 u.ExternalSubject == account.Subject &&
                 u.IsActive,
            ct);
        if (user is null)
            return null;

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<KastUser> UpdateProfileAsync(
        int userId,
        string username,
        string? newPassword = null,
        Stream? avatarStream = null,
        string? avatarFileName = null,
        long avatarLength = 0,
        CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new InvalidOperationException("User account was not found.");

        if (user.IsExternallyManaged)
            throw new InvalidOperationException("Externally managed accounts cannot be edited in KAST.");

        var normalized = NormalizeUsername(username);
        ValidateUsername(username, normalized);
        if (await db.Users.AnyAsync(u => u.Id != userId && u.NormalizedUsername == normalized, ct))
            throw new InvalidOperationException("Username is already in use.");

        user.Username = username.Trim();
        user.NormalizedUsername = normalized;

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            ValidatePassword(newPassword);
            user.PasswordHash = HashPassword(newPassword);
        }

        var newAvatar = await SaveAvatarAsync(avatarStream, avatarFileName, avatarLength, ct);
        if (!string.IsNullOrEmpty(newAvatar))
            user.AvatarFileName = newAvatar;

        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return user;
    }

    public Task<string?> GetAvatarPathAsync(string fileName, CancellationToken ct = default)
    {
        if (!IsSafeStoredAvatarFileName(fileName))
            return Task.FromResult<string?>(null);

        var path = Path.Join(GetAvatarDirectory(), fileName);
        return Task.FromResult(File.Exists(path) ? path : null);
    }

    private async Task<string?> SaveAvatarAsync(Stream? stream, string? originalFileName, long length, CancellationToken ct)
    {
        if (stream is null || string.IsNullOrWhiteSpace(originalFileName) || length <= 0)
            return null;

        if (length > MaxAvatarBytes)
            throw new InvalidOperationException("Avatar image must be 2 MB or smaller.");

        var extension = Path.GetExtension(originalFileName);
        if (!AllowedAvatarExtensions.Contains(extension))
            throw new InvalidOperationException("Avatar image must be a PNG, JPG, JPEG, or WEBP file.");

        var directory = GetAvatarDirectory();
        Directory.CreateDirectory(directory);

        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = Path.Join(directory, storedName);

        await using var output = File.Create(path);
        await stream.CopyToAsync(output, ct);

        return storedName;
    }

    private string GetAvatarDirectory()
    {
        var configured = configuration["Kast:AvatarDirectory"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var connectionString = configuration.GetConnectionString("Default") ?? string.Empty;
        var dbPath = ExtractSqliteDataSource(connectionString);
        var dbDirectory = string.IsNullOrWhiteSpace(dbPath) ? null : Path.GetDirectoryName(dbPath);

        return string.IsNullOrWhiteSpace(dbDirectory)
            ? Path.GetFullPath("./avatars")
            : Path.Join(dbDirectory, "avatars");
    }

    private static string? ExtractSqliteDataSource(string connectionString)
    {
        const string dataSource = "Data Source=";
        var start = connectionString.IndexOf(dataSource, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        var valueStart = start + dataSource.Length;
        var valueEnd = connectionString.IndexOf(';', valueStart);
        var value = valueEnd >= 0
            ? connectionString[valueStart..valueEnd]
            : connectionString[valueStart..];

        return value.Trim().Trim('"');
    }

    private async Task<string> GetUniqueUsernameAsync(string preferredUsername, int? existingUserId, CancellationToken ct)
    {
        var baseUsername = NormalizeExternalUsername(preferredUsername);

        for (var suffixNumber = 0; suffixNumber < 1_000; suffixNumber++)
        {
            var suffix = suffixNumber == 0 ? string.Empty : $"-{suffixNumber + 1}";
            var maxBaseLength = 64 - suffix.Length;
            var candidate = baseUsername.Length > maxBaseLength
                ? baseUsername[..maxBaseLength]
                : baseUsername;
            candidate += suffix;

            var normalized = NormalizeUsername(candidate);
            var exists = await db.Users.AnyAsync(
                u => u.NormalizedUsername == normalized &&
                     (!existingUserId.HasValue || u.Id != existingUserId.Value),
                ct);
            if (!exists)
                return candidate;
        }

        throw new InvalidOperationException("Could not allocate a unique username for the OIDC account.");
    }

    private HashSet<string> GetConfiguredOidcAllowedGroups()
    {
        var groups = configuration.GetSection("Auth:Oidc:AllowedGroups")
            .GetChildren()
            .Select(child => child.Value)
            .Concat(SplitConfigList(configuration["Auth:Oidc:AllowedGroups"]))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim());

        var allowedGroups = new HashSet<string>(groups, StringComparer.OrdinalIgnoreCase);
        if (allowedGroups.Count == 0)
            allowedGroups.Add(DefaultOidcAllowedGroup);

        return allowedGroups;
    }

    private static IEnumerable<string> SplitConfigList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizeExternalUsername(string username)
    {
        var trimmed = string.IsNullOrWhiteSpace(username) ? "oidc-user" : username.Trim();
        return trimmed.Length > 64 ? trimmed[..64] : trimmed;
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private async Task<KastSettings> GetSettingsAsync(CancellationToken ct)
    {
        if (settingsService is not null)
            return await settingsService.GetSettingsAsync(ct);

        var settings = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (settings is not null)
            return settings;

        settings = new KastSettings
        {
            ModsDirectory = configuration["Kast:ModsDirectory"] ?? "./mods",
            ServersDirectory = configuration["Kast:ServersDirectory"] ?? "./servers",
            Arma3ServerAppId = int.TryParse(configuration["Kast:Arma3AppId"], out var appId) ? appId : 233780,
            UpdateChannelId = configuration["Kast:UpdateChannelId"] ?? "stable",
            AutoUpdateCheckEnabled = !bool.TryParse(configuration["Kast:AutoUpdateCheckEnabled"], out var autoCheck) || autoCheck
        };
        db.Settings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }

    private static void ValidateSystemAccount(SystemAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.Username))
            throw new InvalidOperationException("System account username is required.");

        if (string.IsNullOrWhiteSpace(account.Issuer) || string.IsNullOrWhiteSpace(account.Subject))
            throw new InvalidOperationException("System account identity is required.");

        if (string.IsNullOrWhiteSpace(account.Provider))
            throw new InvalidOperationException("System account provider is required.");
    }

    private static void ValidateUsername(string username, string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Username is required.");

        if (username.Trim().Length > 64)
            throw new InvalidOperationException("Username must be 64 characters or fewer.");
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Password is required.");
    }

    private static string NormalizeUsername(string username)
        => username.Trim().ToUpperInvariant();

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var derived = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, 100_000, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(derived)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expectedHash = Convert.FromBase64String(parts[1]);
        var derived = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, 100_000, 32);

        return CryptographicOperations.FixedTimeEquals(derived, expectedHash);
    }

    private static bool IsSafeStoredAvatarFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
            return false;

        var extension = Path.GetExtension(fileName);
        if (!AllowedAvatarExtensions.Contains(extension))
            return false;

        return fileName[..^extension.Length].All(Uri.IsHexDigit);
    }
}
