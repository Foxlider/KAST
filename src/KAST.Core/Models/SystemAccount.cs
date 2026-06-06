namespace KAST.Core.Models;

public sealed record SystemAccount(
    string Username,
    string DisplayName,
    string Issuer,
    string Subject,
    string Provider);

public sealed record SystemAccountProviderStatus(
    bool IsSupported,
    string Provider,
    bool SupportsDomain,
    string? UnsupportedReason);
