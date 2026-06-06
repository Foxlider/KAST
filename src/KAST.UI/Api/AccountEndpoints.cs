using KAST.Core.Interfaces;
using KAST.UI.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace KAST.UI.Api;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/setup", async (HttpContext http, IUserAccountService accounts, IConfiguration configuration, CancellationToken ct)
            => await SetupAsync(http, accounts, configuration, ct)).DisableAntiforgery();
        endpoints.MapPost("/auth/login", async (HttpContext http, IUserAccountService accounts, ISettingsService settings, IConfiguration configuration, CancellationToken ct)
            => await LoginAsync(http, accounts, settings, configuration, ct)).DisableAntiforgery();
        endpoints.MapGet("/auth/oidc/login", (HttpContext http, IConfiguration configuration)
            => OidcLogin(http, configuration));
        endpoints.MapGet("/auth/oidc/signed-out", () => Results.Redirect("/login"));
        endpoints.MapPost("/auth/logout", async (HttpContext http, IConfiguration configuration)
            => await LogoutAsync(http, configuration)).DisableAntiforgery();

        endpoints.MapGet("/account/avatars/{fileName}", GetAvatarAsync)
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> SetupAsync(HttpContext http, IUserAccountService accounts, IConfiguration configuration, CancellationToken ct)
    {
        var authSettings = configuration.GetKastAuthSettings();
        if (!authSettings.IsLocalEnabled)
            return RedirectToOidcOrLogin(authSettings, "/");

        if (await accounts.HasAnyUsersAsync(ct))
            return Results.Redirect("/login");

        var form = await http.Request.ReadFormAsync(ct);
        var username = form["username"].ToString();
        var password = form["password"].ToString();
        var avatar = form.Files.GetFile("avatar");

        try
        {
            await using var avatarStream = avatar?.OpenReadStream();
            var user = await accounts.CreateInitialAdminAsync(
                username,
                password,
                avatarStream,
                avatar?.FileName,
                avatar?.Length ?? 0,
                ct);

            await SignInAsync(http, user);
            return Results.Redirect("/");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return Results.Redirect($"/setup/account?error={Uri.EscapeDataString(ex.Message)}");
        }
    }

    private static async Task<IResult> LoginAsync(
        HttpContext http,
        IUserAccountService accounts,
        ISettingsService settings,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var authSettings = configuration.GetKastAuthSettings();
        var appSettings = await settings.GetSettingsAsync(ct);
        var form = await http.Request.ReadFormAsync(ct);
        var username = form["username"].ToString();
        var password = form["password"].ToString();
        var returnUrl = NormalizeReturnUrl(form["returnUrl"].ToString());

        if (!authSettings.IsLocalEnabled && !appSettings.SystemAuthEnabled)
            return RedirectToOidcOrLogin(authSettings, returnUrl);

        if (!await accounts.HasAnyUsersAsync(ct))
            return Results.Redirect("/setup/account");

        KAST.Core.Models.KastUser? user = null;
        if (authSettings.IsLocalEnabled)
            user = await accounts.ValidateCredentialsAsync(username, password, ct);

        if (user is null && appSettings.SystemAuthEnabled)
            user = await accounts.ValidateSystemCredentialsAsync(username, password, ct);

        if (user is null)
            return Results.Redirect($"/login?error={Uri.EscapeDataString("Invalid username or password.")}&returnUrl={Uri.EscapeDataString(returnUrl)}");

        await SignInAsync(http, user);
        return Results.Redirect(returnUrl);
    }

    private static IResult OidcLogin(HttpContext http, IConfiguration configuration)
    {
        var authSettings = configuration.GetKastAuthSettings();
        var returnUrl = NormalizeReturnUrl(http.Request.Query["returnUrl"].ToString());
        if (!authSettings.IsOidcConfigured)
        {
            return Results.Redirect($"/login?error={Uri.EscapeDataString("OpenID Connect is not configured.")}&returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = returnUrl
        };

        return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, IConfiguration configuration)
    {
        var authSettings = configuration.GetKastAuthSettings();
        if (http.User.GetAuthSource() == KAST.Core.Models.KastUser.OidcAuthSource && authSettings.IsOidcConfigured)
        {
            return Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/login" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        }

        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login");
    }

    private static async Task<IResult> GetAvatarAsync(
        [FromRoute] string fileName,
        IUserAccountService accounts,
        CancellationToken ct)
    {
        var path = await accounts.GetAvatarPathAsync(fileName, ct);
        if (path is null)
            return Results.NotFound();

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        return Results.File(path, contentType);
    }

    private static async Task SignInAsync(HttpContext http, KAST.Core.Models.KastUser user)
    {
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
            AllowRefresh = true
        };

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AccountClaims.CreatePrincipal(user),
            authProperties);
    }

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//"))
            return "/";

        return returnUrl;
    }

    private static IResult RedirectToOidcOrLogin(KastAuthSettings authSettings, string returnUrl)
    {
        if (authSettings.IsOidcConfigured)
            return Results.Redirect($"/auth/oidc/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

        return Results.Redirect($"/login?error={Uri.EscapeDataString("Local sign-in is disabled and OpenID Connect is not configured.")}");
    }
}
