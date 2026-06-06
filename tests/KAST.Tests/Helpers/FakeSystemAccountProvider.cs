using KAST.Core.Interfaces;
using KAST.Core.Models;

namespace KAST.Tests.Helpers;

public sealed class FakeSystemAccountProvider : ISystemAccountProvider
{
    private readonly Dictionary<string, (string Password, SystemAccount Account)> _accounts =
        new(StringComparer.OrdinalIgnoreCase);

    public void Add(string username, string password, SystemAccount account)
        => _accounts[username] = (password, account);

    public SystemAccountProviderStatus GetStatus()
        => new(true, "Fake System", SupportsDomain: true, UnsupportedReason: null);

    public Task<IReadOnlyList<SystemAccount>> SearchAccountsAsync(string? query, string? domain, CancellationToken ct = default)
    {
        var accounts = _accounts.Values
            .Select(value => value.Account)
            .Where(account => string.IsNullOrWhiteSpace(query) ||
                              account.Username.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                              account.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Task.FromResult<IReadOnlyList<SystemAccount>>(accounts);
    }

    public Task<SystemAccount?> ValidateCredentialsAsync(string username, string password, string? domain, CancellationToken ct = default)
    {
        if (!_accounts.TryGetValue(username, out var entry) || entry.Password != password)
            return Task.FromResult<SystemAccount?>(null);

        return Task.FromResult<SystemAccount?>(entry.Account);
    }
}
