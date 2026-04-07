using KAST.Infrastructure.Services;
using KAST.Tests.Helpers;

namespace KAST.Tests;

public class ApiKeyServiceTests : IDisposable
{
    private readonly Infrastructure.Data.KastDbContext _db;
    private readonly ApiKeyService _sut;

    public ApiKeyServiceTests()
    {
        _db = DbHelper.CreateInMemoryDb();
        _sut = new ApiKeyService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateApiKey_ReturnsKeyWithPrefix()
    {
        var (key, rawKey) = await _sut.CreateApiKeyAsync("Test Key");

        Assert.Equal("Test Key", key.Name);
        Assert.StartsWith("kast_", rawKey);
        Assert.Equal(rawKey[..8], key.KeyPrefix);
        Assert.True(key.IsActive);
        Assert.NotEmpty(key.KeyHash);
    }

    [Fact]
    public async Task CreateApiKey_PersistedToDb()
    {
        await _sut.CreateApiKeyAsync("Persisted Key");

        Assert.Single(_db.ApiKeys);
    }

    [Fact]
    public async Task ValidateKey_ValidKey_ReturnsTrue()
    {
        var (_, rawKey) = await _sut.CreateApiKeyAsync("Valid Key");

        var isValid = await _sut.ValidateKeyAsync(rawKey);

        Assert.True(isValid);
    }

    [Fact]
    public async Task ValidateKey_InvalidKey_ReturnsFalse()
    {
        await _sut.CreateApiKeyAsync("Some Key");

        var isValid = await _sut.ValidateKeyAsync("kast_xx_this_is_totally_bogus_key");

        Assert.False(isValid);
    }

    [Fact]
    public async Task ValidateKey_UpdatesLastUsedAt()
    {
        var (key, rawKey) = await _sut.CreateApiKeyAsync("Usage Key");
        Assert.Null(key.LastUsedAt);

        await _sut.ValidateKeyAsync(rawKey);

        var updated = await _db.ApiKeys.FindAsync(key.Id);
        Assert.NotNull(updated!.LastUsedAt);
    }

    [Fact]
    public async Task RevokeKey_DeactivatesKey()
    {
        var (key, _) = await _sut.CreateApiKeyAsync("Revocable Key");
        Assert.True(key.IsActive);

        await _sut.RevokeKeyAsync(key.Id);

        var revoked = await _db.ApiKeys.FindAsync(key.Id);
        Assert.False(revoked!.IsActive);
    }

    [Fact]
    public async Task ValidateKey_RevokedKey_ReturnsFalse()
    {
        var (key, rawKey) = await _sut.CreateApiKeyAsync("Soon Revoked");
        await _sut.RevokeKeyAsync(key.Id);

        var isValid = await _sut.ValidateKeyAsync(rawKey);

        Assert.False(isValid);
    }

    [Fact]
    public async Task GetAllKeys_ReturnsAllOrderedByCreatedAt()
    {
        await _sut.CreateApiKeyAsync("First");
        await _sut.CreateApiKeyAsync("Second");
        await _sut.CreateApiKeyAsync("Third");

        var keys = await _sut.GetAllKeysAsync();

        Assert.Equal(3, keys.Count);
        // Ordered descending by CreatedAt (newest first)
        Assert.Equal("Third", keys[0].Name);
    }

    [Fact]
    public async Task CreateApiKey_MultipleKeys_HaveUniqueHashes()
    {
        var (key1, _) = await _sut.CreateApiKeyAsync("Key 1");
        var (key2, _) = await _sut.CreateApiKeyAsync("Key 2");

        Assert.NotEqual(key1.KeyHash, key2.KeyHash);
    }

    [Fact]
    public async Task RevokeKey_NonExistentId_DoesNotThrow()
    {
        await _sut.RevokeKeyAsync(999);
        // No exception = pass
    }
}
