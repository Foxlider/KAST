using KAST.Core.Interfaces;
using KAST.Infrastructure;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Steam;
using KAST.Infrastructure.Services;
using KAST.Infrastructure.Telemetry;
using KAST.UI.Api;
using KAST.UI.Components;
using KAST.UI.Hubs;
using KAST.UI.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using MudBlazor;
using MudBlazor.Services;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ModStatus = KAST.Core.Enums.ModStatus;

var builder = WebApplication.CreateBuilder(args);
var contentRoot = builder.Environment.ContentRootPath;
var outputSanitizer = new OutputSanitizer(
    new OutputSanitizer.VirtualPathRoot(contentRoot, "KAST"),
    new OutputSanitizer.VirtualPathRoot(ResolveConfiguredPath(contentRoot, builder.Configuration["Kast:ModsDirectory"] ?? "./mods"), "mods"),
    new OutputSanitizer.VirtualPathRoot(ResolveConfiguredPath(contentRoot, builder.Configuration["Kast:ServersDirectory"] ?? "./servers"), "server"));
builder.Services.AddSingleton<IOutputSanitizer>(outputSanitizer);
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "KAST Panel";
});

// ── In-memory log capture (UI console) ──────────────────────────────────────
var kastLogStore = new KastLogStore();
builder.Services.AddSingleton(kastLogStore);
builder.Logging.AddProvider(new KastLoggerProvider(kastLogStore, outputSanitizer));

// ── Health checks ────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddDbContextCheck<KastDbContext>("database");

// ── Database ─────────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=kast.db";
builder.Services.AddKastInfrastructure(connectionString);

// ── MudBlazor + Blazor Server ────────────────────────────────────────────────
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomLeft;
    config.SnackbarConfiguration.VisibleStateDuration = 3000;
    config.SnackbarConfiguration.HideTransitionDuration = 200;
    config.SnackbarConfiguration.ShowTransitionDuration = 150;
});
builder.Services.AddScoped<KAST.UI.Services.ThemeService>();
builder.Services.AddScoped<KastInternalUrlProvider>();
builder.Services.AddSingleton<ModDownloadManager>();
builder.Services.AddScoped<KeyboardShortcutService>();
builder.Services.AddSingleton<InternalHubTokenService>();
var authSettings = builder.Configuration.GetKastAuthSettings();
var authenticationBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "KAST.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Events.OnValidatePrincipal = async context =>
        {
            var userId = context.Principal?.GetUserId();
            if (userId is null)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var accounts = context.HttpContext.RequestServices.GetRequiredService<IUserAccountService>();
            var user = await accounts.GetByIdAsync(userId.Value, context.HttpContext.RequestAborted);
            if (user is null)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
        options.Events.OnRedirectToLogin = async context =>
        {
            if (AccountGateMiddlewareExtensions.IsApiOrHubRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var accounts = context.HttpContext.RequestServices.GetRequiredService<IUserAccountService>();
            var returnUrl = $"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            var destination = await accounts.HasAnyUsersAsync()
                ? $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}"
                : "/setup/account";
            context.Response.Redirect(destination);
        };
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, InternalHubAuthenticationHandler>(
        InternalHubAuthenticationDefaults.AuthenticationScheme,
        _ => { });

if (authSettings.IsOidcConfigured)
{
    authenticationBuilder.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, authSettings.DisplayName, options =>
    {
        options.Authority = authSettings.Authority;
        options.ClientId = authSettings.ClientId;
        options.ClientSecret = authSettings.ClientSecret;
        options.ResponseType = "code";
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.CallbackPath = "/auth/oidc/callback";
        options.SignedOutCallbackPath = "/auth/oidc/signed-out";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = authSettings.NameClaim;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.ClaimActions.MapJsonKey(authSettings.GroupClaim, authSettings.GroupClaim);
        options.ClaimActions.MapJsonKey(authSettings.NameClaim, authSettings.NameClaim);
        options.ClaimActions.MapJsonKey("name", "name");
        options.ClaimActions.MapJsonKey("email", "email");
        options.Events.OnTokenValidated = async context =>
        {
            var principal = context.Principal
                ?? throw new InvalidOperationException("OIDC token validation produced no principal.");
            var issuer = FirstNonBlank(
                principal.FindFirstValue("iss"),
                context.SecurityToken?.Issuer,
                authSettings.Authority);
            var subject = principal.FindFirstValue("sub");
            var displayName = FirstClaimValue(principal, authSettings.NameClaim, "preferred_username", "name", "email", "sub");
            var email = principal.FindFirstValue("email");
            var groups = ReadClaimValues(principal, authSettings.GroupClaim);

            var accounts = context.HttpContext.RequestServices.GetRequiredService<IUserAccountService>();
            var user = await accounts.ProvisionOidcAdminAsync(
                new KAST.Core.Models.OidcProvisioningRequest(issuer, subject, displayName, email, groups),
                context.HttpContext.RequestAborted);

            context.Principal = AccountClaims.CreatePrincipal(user);
        };
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            var message = string.IsNullOrWhiteSpace(context.Failure?.Message)
                ? "OpenID Connect sign-in failed."
                : context.Failure.Message;
            context.Response.Redirect($"/login?error={Uri.EscapeDataString(message)}");
            return Task.CompletedTask;
        };
    });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOrInternalHub", policy =>
    {
        policy.AddAuthenticationSchemes(
            CookieAuthenticationDefaults.AuthenticationScheme,
            InternalHubAuthenticationDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Minimal API ──────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();

// ── SignalR ──────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ── Server console history (survives page navigation) ───────────────────────
builder.Services.AddSingleton<ServerConsoleStore>();

// ── Event broadcaster (bridges domain events → SignalR) ──────────────────────
builder.Services.AddSingleton<IAppEventBroadcaster, SignalREventBroadcaster>();
builder.Services.AddSingleton<MonitoringSnapshotService>();

// ── Monitoring state (circuit-scoped, survives page navigation) ──────────────
builder.Services.AddScoped<MonitoringStateService>();

// ── Background services ──────────────────────────────────────────────────────
builder.Services.AddHostedService<MetricsBackgroundService>();
builder.Services.AddHostedService<ProcessWatchdogService>();
builder.Services.AddHostedService<SchedulingBackgroundService>();

// ── OpenTelemetry tracing ────────────────────────────────────────────────────
var telemetry = builder.Configuration.GetSection("Telemetry");
if (telemetry.GetValue("Enabled", true))
{
    var otlpEndpoint = telemetry["OtlpEndpoint"] ?? "http://localhost:4317";
    var serviceName = telemetry["ServiceName"] ?? KastActivitySources.ServiceName;

    // Attach ILogger calls as span events so they appear inside traces in Jaeger
    builder.Logging.AddProvider(new ActivityEventLoggerProvider(outputSanitizer));

    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault().AddService(serviceName))
            .AddAspNetCoreInstrumentation(opts =>
            {
                // Skip noisy health-check and static-asset spans
                opts.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments("/health") &&
                    !ctx.Request.Path.StartsWithSegments("/alive");
            })
            .AddHttpClientInstrumentation(opts =>
            {
                // Steam CDN downloads issue thousands of GET requests per session
                // (one per depot chunk). Only keep non-GET calls (Steam Web API POSTs).
                opts.FilterHttpRequestMessage = req => req.Method != HttpMethod.Get;
            })
            .AddEntityFrameworkCoreInstrumentation()
            .AddSource(KastActivitySources.Content.Name)
            .AddSource(KastActivitySources.Process.Name)
            .AddSource(KastActivitySources.Mods.Name)
            .AddSource(KastActivitySources.Steam.Name)
            .AddSource(KastActivitySources.Instances.Name)
            .AddOtlpExporter(opts =>
            {
                opts.Endpoint = new Uri(otlpEndpoint);
            }));
}

var app = builder.Build();

// ── Database migration & startup cleanup ─────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    // Ensure data directories exist
    var modsDir = config["Kast:ModsDirectory"] ?? "./mods";
    var serversDir = config["Kast:ServersDirectory"] ?? "./servers";
    Directory.CreateDirectory(modsDir);
    Directory.CreateDirectory(serversDir);

    // Ensure SQLite directory exists
    var cs = config.GetConnectionString("Default") ?? "";
    var dbPath = cs.Replace("Data Source=", "");
    if (!string.IsNullOrEmpty(dbPath))
    {
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir))
            Directory.CreateDirectory(dbDir);
    }

    var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
    await db.Database.MigrateAsync();

    // Reset any mods stuck in-progress from a previous crash
    var stuckMods = await db.Mods
        .Where(m => m.Status == ModStatus.Downloading || m.Status == ModStatus.Updating)
        .ToListAsync();
    foreach (var mod in stuckMods)
        mod.Status = !string.IsNullOrWhiteSpace(mod.LocalPath) && Directory.Exists(mod.LocalPath)
            ? ModStatus.UpdateAvailable
            : ModStatus.NotInstalled;
    if (stuckMods.Count > 0)
        await db.SaveChangesAsync();
}

// ── Middleware ────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api") &&
               !context.Request.Path.StartsWithSegments("/hubs") &&
               !context.Request.Path.StartsWithSegments("/_blazor"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                       ForwardedHeaders.XForwardedHost |
                       ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseKastAccountGate();
app.UseAuthorization();

// ── Health checks ─────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive");

app.MapAccountEndpoints();

// ── Minimal API groups ────────────────────────────────────────────────────────
app.MapGroup("/api").MapKastApi().RequireAuthorization();

// ── SignalR hubs ──────────────────────────────────────────────────────────────
app.MapHub<MonitoringHub>("/hubs/monitoring").RequireAuthorization("AdminOrInternalHub");
app.MapHub<DownloadHub>("/hubs/downloads").RequireAuthorization("AdminOrInternalHub");

// ── Blazor ────────────────────────────────────────────────────────────────────
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ── Steam startup ─────────────────────────────────────────────────────────────
_ = Task.Run(async () =>
{
    var steam = app.Services.GetRequiredService<ISteamService>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    try
    {
        var cache = SteamClientService.PeekTokenCache();
        if (cache is { } c)
        {
            logger.LogInformation("Restoring Steam session for {Username}", c.Username);
            var ok = await steam.LoginWithTokenAsync(c.Username, c.RefreshToken);
            if (ok)
            {
                logger.LogInformation("Steam session restored: {Username}", c.Username);
                return;
            }
            logger.LogWarning("Cached Steam token invalid — falling back to anonymous");
        }

        var anon = await steam.LoginAnonymousAsync();
        logger.LogInformation("Steam connected: {Result}", anon ? "anonymous" : "failed");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Steam startup connection failed");
    }
});

app.Run();

static string? FirstClaimValue(ClaimsPrincipal principal, params string[] claimTypes)
    => claimTypes
        .Select(principal.FindFirstValue)
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

static string? FirstNonBlank(params string?[] values)
    => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

static IReadOnlyCollection<string> ReadClaimValues(ClaimsPrincipal principal, string claimType)
    => principal.FindAll(claimType)
        .SelectMany(claim => SplitClaimValue(claim.Value))
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

static IEnumerable<string> SplitClaimValue(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        return [];

    var trimmed = value.Trim();
    if (trimmed.StartsWith('['))
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(trimmed) ?? [];
        }
        catch (JsonException)
        {
            return [trimmed];
        }
    }

    return [trimmed];
}

static string ResolveConfiguredPath(string contentRoot, string path) =>
    Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path));
