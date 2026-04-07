using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IApiKeyService
{
    Task<(ApiKey Key, string RawKey)> CreateApiKeyAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<ApiKey>> GetAllKeysAsync(CancellationToken ct = default);
    Task<bool> ValidateKeyAsync(string rawKey, CancellationToken ct = default);
    Task RevokeKeyAsync(int id, CancellationToken ct = default);
}
