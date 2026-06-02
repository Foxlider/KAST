using KAST.Core.Interfaces;

namespace KAST.UI.Services;

public static class AccountGateMiddlewareExtensions
{
    public static IApplicationBuilder UseKastAccountGate(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/setup/account"))
            {
                var authSettings = context.RequestServices.GetRequiredService<IConfiguration>().GetKastAuthSettings();
                if (!authSettings.IsLocalEnabled)
                {
                    context.Response.Redirect("/auth/oidc/login");
                    return;
                }

                var accountService = context.RequestServices.GetRequiredService<IUserAccountService>();
                if (await accountService.HasAnyUsersAsync(context.RequestAborted))
                {
                    context.Response.Redirect("/login");
                    return;
                }
            }

            if (IsAnonymousRequest(context.Request) ||
                IsApiOrHubRequest(context.Request) ||
                context.User.Identity?.IsAuthenticated == true)
            {
                await next();
                return;
            }

            var accounts = context.RequestServices.GetRequiredService<IUserAccountService>();
            var settings = context.RequestServices.GetRequiredService<IConfiguration>().GetKastAuthSettings();
            var returnUrl = $"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            if (settings.IsOidcConfigured &&
                (!settings.IsLocalEnabled || !await accounts.HasAnyUsersAsync(context.RequestAborted)))
            {
                context.Response.Redirect($"/auth/oidc/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
                return;
            }

            if (!await accounts.HasAnyUsersAsync(context.RequestAborted))
            {
                context.Response.Redirect("/setup/account");
                return;
            }

            context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        });

    public static bool IsApiOrHubRequest(HttpRequest request)
        => request.Path.StartsWithSegments("/api") ||
           request.Path.StartsWithSegments("/hubs");

    public static bool IsAnonymousRequest(HttpRequest request)
    {
        var path = request.Path;
        if (path.StartsWithSegments("/login") ||
            path.StartsWithSegments("/setup/account") ||
            path.StartsWithSegments("/auth") ||
            path.StartsWithSegments("/health") ||
            path.StartsWithSegments("/alive") ||
            path.StartsWithSegments("/_blazor") ||
            path.StartsWithSegments("/_framework") ||
            path.StartsWithSegments("/_content"))
        {
            return true;
        }

        return Path.HasExtension(path.Value);
    }
}
