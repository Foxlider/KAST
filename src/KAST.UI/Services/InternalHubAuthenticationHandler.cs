using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace KAST.UI.Services;

public static class InternalHubAuthenticationDefaults
{
    public const string AuthenticationScheme = "KastInternalHub";
    public const string HeaderName = "X-KAST-Internal-Hub-Token";
}

public sealed class InternalHubAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    InternalHubTokenService tokenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers[InternalHubAuthenticationDefaults.HeaderName].FirstOrDefault();

        if (!tokenService.IsValid(token))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "internal-hub-client"),
            new Claim(ClaimTypes.Name, "Internal Hub Client")
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
