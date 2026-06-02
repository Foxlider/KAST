namespace KAST.Core.Models;

public sealed record OidcProvisioningRequest(
    string? Issuer,
    string? Subject,
    string? DisplayName,
    string? Email,
    IReadOnlyCollection<string> Groups);
