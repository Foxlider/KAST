using KAST.Core.Interfaces;
using KAST.Core.Models;

namespace KAST.Infrastructure.Services.SystemAccounts;

public sealed class UnsupportedSystemAccountProvider(string reason) : ISystemAccountProvider
{
    public SystemAccountProviderStatus GetStatus()
        => new(false, "Unsupported", SupportsDomain: false, reason);

    public Task<IReadOnlyList<SystemAccount>> SearchAccountsAsync(string? query, string? domain, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SystemAccount>>([]);

    public Task<SystemAccount?> ValidateCredentialsAsync(string username, string password, string? domain, CancellationToken ct = default)
        => Task.FromResult<SystemAccount?>(null);
}
