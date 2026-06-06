using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using KAST.Core.Interfaces;
using KAST.Core.Models;

namespace KAST.Infrastructure.Services.SystemAccounts;

#pragma warning disable CA1416 // All AccountManagement calls are guarded by Windows runtime checks.
public sealed class WindowsSystemAccountProvider : ISystemAccountProvider
{
    public SystemAccountProviderStatus GetStatus()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new(true, "Windows", SupportsDomain: true, UnsupportedReason: null)
            : new(false, "Windows", SupportsDomain: true, "Windows account authentication requires Windows.");

    public Task<IReadOnlyList<SystemAccount>> SearchAccountsAsync(string? query, string? domain, CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.FromResult<IReadOnlyList<SystemAccount>>([]);

        return Task.Run(() =>
        {
            using var context = CreateContext(domain);
            using var example = new UserPrincipal(context)
            {
                SamAccountName = string.IsNullOrWhiteSpace(query) ? "*" : $"*{query.Trim()}*"
            };
            using var searcher = new PrincipalSearcher(example);
            var accounts = new List<SystemAccount>();

            foreach (var result in searcher.FindAll().OfType<UserPrincipal>())
            {
                ct.ThrowIfCancellationRequested();
                var account = ToSystemAccount(result, domain);
                if (account is not null)
                    accounts.Add(account);

                if (accounts.Count >= 100)
                    break;
            }

            return (IReadOnlyList<SystemAccount>)accounts
                .OrderBy(account => account.Username, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, ct);
    }

    public Task<SystemAccount?> ValidateCredentialsAsync(string username, string password, string? domain, CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult<SystemAccount?>(null);
        }

        return Task.Run(() =>
        {
            try
            {
                var accountName = NormalizeLoginName(username, domain);
                if (string.IsNullOrWhiteSpace(accountName))
                    return null;

                using var context = CreateContext(domain);
                if (!context.ValidateCredentials(accountName, password))
                    return null;

                using var principal = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, accountName);
                return principal is null ? null : ToSystemAccount(principal, domain);
            }
            catch (PrincipalException)
            {
                return null;
            }
        }, ct);
    }

    private static PrincipalContext CreateContext(string? domain)
        => string.IsNullOrWhiteSpace(domain)
            ? new PrincipalContext(ContextType.Machine)
            : new PrincipalContext(ContextType.Domain, domain.Trim());

    private static SystemAccount? ToSystemAccount(UserPrincipal user, string? domain)
    {
        if (string.IsNullOrWhiteSpace(user.SamAccountName) || user.Sid is null)
            return null;

        var isDomain = !string.IsNullOrWhiteSpace(domain);
        var issuer = isDomain ? domain!.Trim().ToUpperInvariant() : Environment.MachineName.ToUpperInvariant();
        var provider = isDomain ? "Windows AD" : "Windows Local";
        var username = isDomain ? $"{issuer}\\{user.SamAccountName}" : user.SamAccountName;
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? username : user.DisplayName;

        return new SystemAccount(username, displayName, issuer, user.Sid.Value, provider);
    }

    private static string? NormalizeLoginName(string username, string? configuredDomain)
    {
        var trimmed = username.Trim();
        if (string.IsNullOrWhiteSpace(configuredDomain))
            return NormalizeLocalLoginName(trimmed);

        var expectedDomain = configuredDomain.Trim();
        var slash = trimmed.IndexOf('\\');
        if (slash >= 0)
        {
            var suppliedDomain = trimmed[..slash];
            if (!string.Equals(suppliedDomain, expectedDomain, StringComparison.OrdinalIgnoreCase))
                return null;

            return trimmed[(slash + 1)..];
        }

        var at = trimmed.IndexOf('@');
        if (at >= 0)
        {
            var suppliedDomain = trimmed[(at + 1)..];
            if (!string.Equals(suppliedDomain, expectedDomain, StringComparison.OrdinalIgnoreCase))
                return null;

            return trimmed[..at];
        }

        return trimmed;
    }

    private static string? NormalizeLocalLoginName(string username)
    {
        var slash = username.IndexOf('\\');
        if (slash < 0)
            return username;

        var prefix = username[..slash];
        if (!string.Equals(prefix, ".", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(prefix, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return username[(slash + 1)..];
    }
}
#pragma warning restore CA1416
