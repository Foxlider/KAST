using System.Security.Cryptography;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;

namespace KAST.Infrastructure.Services;

public class ApiKeyService(KastDbContext db) : IApiKeyService
{
    public async Task<(ApiKey Key, string RawKey)> CreateApiKeyAsync(string name, CancellationToken ct = default)
    {
        var rawKey = GenerateApiKey();
        var hash = HashKey(rawKey);

        var key = new ApiKey
        {
            Name = name,
            KeyHash = hash,
            KeyPrefix = rawKey[..8]
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        return (key, rawKey);
    }

    public async Task<IReadOnlyList<ApiKey>> GetAllKeysAsync(CancellationToken ct = default)
        => await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(ct);

    public async Task<bool> ValidateKeyAsync(string rawKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || rawKey.Length < 8)
            return false;

        var prefix = rawKey[..8];
        var candidate = await db.ApiKeys
            .Where(k => k.KeyPrefix == prefix && k.IsActive)
            .FirstOrDefaultAsync(k => VerifyKey(rawKey, k.KeyHash), ct);

        if (candidate != null)
        {
            candidate.LastUsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }

        return false;
    }

    public async Task RevokeKeyAsync(int id, CancellationToken ct = default)
    {
        var key = await db.ApiKeys.FindAsync([id], ct);
        if (key != null)
        {
            key.IsActive = false;
            await db.SaveChangesAsync(ct);
        }
    }

    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return $"kast_{Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "")}";
    }

    private static string HashKey(string key)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var derived = KeyDerivation.Pbkdf2(key, salt, KeyDerivationPrf.HMACSHA256, 100_000, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(derived)}";
    }

    private static bool VerifyKey(string key, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expectedHash = Convert.FromBase64String(parts[1]);
        var derived = KeyDerivation.Pbkdf2(key, salt, KeyDerivationPrf.HMACSHA256, 100_000, 32);

        return CryptographicOperations.FixedTimeEquals(derived, expectedHash);
    }
}
