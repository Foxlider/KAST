using System.Security.Claims;
using KAST.Core.Models;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace KAST.UI.Services;

public static class AccountClaims
{
    public const string AuthSourceClaim = "kast:auth_source";
    public const string ExternalProviderClaim = "kast:external_provider";

    public static int? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }

    public static string GetAuthSource(this ClaimsPrincipal principal)
        => principal.FindFirstValue(AuthSourceClaim) ?? KastUser.LocalAuthSource;

    public static ClaimsPrincipal CreatePrincipal(KastUser user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(AuthSourceClaim, user.AuthSource)
        };

        if (!string.IsNullOrWhiteSpace(user.ExternalProvider))
            claims.Add(new Claim(ExternalProviderClaim, user.ExternalProvider));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
