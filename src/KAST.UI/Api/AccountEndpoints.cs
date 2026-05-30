using KAST.Core.Interfaces;
using KAST.UI.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace KAST.UI.Api;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/setup", async (HttpContext http, IUserAccountService accounts, CancellationToken ct)
            => await SetupAsync(http, accounts, ct)).DisableAntiforgery();
        endpoints.MapPost("/auth/login", async (HttpContext http, IUserAccountService accounts, CancellationToken ct)
            => await LoginAsync(http, accounts, ct)).DisableAntiforgery();
        endpoints.MapPost("/auth/logout", async (HttpContext http)
            => await LogoutAsync(http)).DisableAntiforgery();

        endpoints.MapGet("/account/avatars/{fileName}", GetAvatarAsync)
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> SetupAsync(HttpContext http, IUserAccountService accounts, CancellationToken ct)
    {
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

    private static async Task<IResult> LoginAsync(HttpContext http, IUserAccountService accounts, CancellationToken ct)
    {
        var form = await http.Request.ReadFormAsync(ct);
        var username = form["username"].ToString();
        var password = form["password"].ToString();
        var returnUrl = NormalizeReturnUrl(form["returnUrl"].ToString());

        if (!await accounts.HasAnyUsersAsync(ct))
            return Results.Redirect("/setup/account");

        var user = await accounts.ValidateCredentialsAsync(username, password, ct);
        if (user is null)
            return Results.Redirect($"/login?error={Uri.EscapeDataString("Invalid username or password.")}&returnUrl={Uri.EscapeDataString(returnUrl)}");

        await SignInAsync(http, user);
        return Results.Redirect(returnUrl);
    }

    private static async Task<IResult> LogoutAsync(HttpContext http)
    {
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
}
