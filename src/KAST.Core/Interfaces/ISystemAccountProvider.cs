using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface ISystemAccountProvider
{
    SystemAccountProviderStatus GetStatus();
    Task<IReadOnlyList<SystemAccount>> SearchAccountsAsync(string? query, string? domain, CancellationToken ct = default);
    Task<SystemAccount?> ValidateCredentialsAsync(string username, string password, string? domain, CancellationToken ct = default);
}
