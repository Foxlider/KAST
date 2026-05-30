using System.Security.Cryptography;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KAST.Infrastructure.Services;

public class UserAccountService(KastDbContext db, IConfiguration configuration) : IUserAccountService
{
    private const int MaxAvatarBytes = 2 * 1024 * 1024;
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

    public async Task<IReadOnlyList<KastUser>> GetAllUsersAsync(CancellationToken ct = default)
        => await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct);

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
            AvatarFileName = await SaveAvatarAsync(avatarStream, avatarFileName, avatarLength, ct),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return user;
    }

    public async Task<KastUser?> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default)
    {
        var normalized = NormalizeUsername(username);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedUsername == normalized && u.IsActive, ct);
        if (user is null || !VerifyPassword(password, user.PasswordHash))
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

        var path = Path.Combine(GetAvatarDirectory(), fileName);
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
        var path = Path.Combine(directory, storedName);

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
            : Path.Combine(dbDirectory, "avatars");
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
