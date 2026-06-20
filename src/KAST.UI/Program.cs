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
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using System.Net;
using MudBlazor;
using MudBlazor.Services;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ModStatus = KAST.Core.Enums.ModStatus;
using ServerInstanceStatus = KAST.Core.Enums.ServerInstanceStatus;

var builder = WebApplication.CreateBuilder(args);
var contentRoot = builder.Environment.ContentRootPath;
var outputSanitizer = new OutputSanitizer(
    new OutputSanitizer.VirtualPathRoot(contentRoot, "KAST"),
    new OutputSanitizer.VirtualPathRoot(ResolveConfiguredPath(contentRoot, builder.Configuration["Kast:ModsDirectory"] ?? "./mods"), "mods"),
    new OutputSanitizer.VirtualPathRoot(ResolveConfiguredPath(contentRoot, builder.Configuration["Kast:ServersDirectory"] ?? "./servers"), "server"));
builder.Services.AddSingleton<IOutputSanitizer>(outputSanitizer);
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "KAST";
});

// ── In-memory log capture (UI console) ──────────────────────────────────────
var kastLogStore = new KastLogStore();
builder.Services.AddSingleton(kastLogStore);
builder.Logging.AddProvider(new KastLoggerProvider(kastLogStore, outputSanitizer));
builder.Services.AddSingleton<ICrashReportService, CrashReportService>();

// ── Health checks ────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddDbContextCheck<KastDbContext>("database");

// ── Database ─────────────────────────────────────────────────────────────────
var connectionString = ResolveSqliteConnectionString(
    contentRoot,
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=kast.db");
builder.Services.AddKastInfrastructure(connectionString);

// Persist Data Protection keys so cookies and ProtectedBrowserStorage survive
// restarts, reinstalls, and Windows service account/context changes.
var dataProtectionKeysDirectory = ResolveDataProtectionKeysDirectory(
    contentRoot,
    builder.Configuration["Kast:DataProtectionKeysDirectory"]);
Directory.CreateDirectory(dataProtectionKeysDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDirectory))
    .SetApplicationName("KAST.UI");

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
builder.Services.AddSingleton<ServerRuntimeEventStore>();

// ── Event broadcaster (bridges domain events → SignalR) ──────────────────────
builder.Services.AddSingleton<IAppEventBroadcaster, SignalREventBroadcaster>();
builder.Services.AddSingleton<MonitoringSnapshotService>();

// ── Monitoring state (circuit-scoped, survives page navigation) ──────────────
builder.Services.AddScoped<MonitoringStateService>();

// ── Background services ──────────────────────────────────────────────────────
builder.Services.AddHostedService<MetricsBackgroundService>();
builder.Services.AddHostedService<ProcessWatchdogService>();
builder.Services.AddHostedService<SchedulingBackgroundService>();
builder.Services.AddHostedService<SteamStartupService>();

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
                    !ctx.Request.Path.StartsWithSegments("/alive") &&
                    !ctx.Request.Path.StartsWithSegments("/ready");
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
var lifecycleLogger = app.Services.GetRequiredService<ILogger<Program>>();
var crashReports = app.Services.GetRequiredService<ICrashReportService>();
crashReports.RecordProcessStart();
app.Lifetime.ApplicationStarted.Register(() =>
    lifecycleLogger.LogInformation(
        "KAST web host started. ProcessId={ProcessId}, WorkingSetMB={WorkingSetMB:F1}",
        Environment.ProcessId,
        Environment.WorkingSet / 1_048_576.0));
app.Lifetime.ApplicationStopping.Register(() =>
{
    lifecycleLogger.LogInformation(
        "KAST web host stopping. ProcessId={ProcessId}, WorkingSetMB={WorkingSetMB:F1}",
        Environment.ProcessId,
        Environment.WorkingSet / 1_048_576.0);

    // Stop all managed Arma server processes so they don't become orphans.
    // Use force-kill (not the graceful-then-force StopProcessAsync) to ensure
    // fast shutdown — Windows services have a ~30s stop timeout, and the update
    // system's apply script needs KAST to exit promptly.
    var shutdownScope = app.Services.CreateScope();
    try
    {
        var shutdownDb = shutdownScope.ServiceProvider.GetRequiredService<KastDbContext>();
        var shutdownProcessManager = shutdownScope.ServiceProvider.GetRequiredService<IProcessManagerService>();

        var running = shutdownDb.ServerInstances
            .Where(s => s.Status == ServerInstanceStatus.Running && s.ProcessId != null)
            .ToList();

        foreach (var instance in running)
        {
            try
            {
                var killTask = shutdownProcessManager.KillProcessAsync(instance.ProcessId!.Value, CancellationToken.None);
                var completed = killTask.Wait(TimeSpan.FromSeconds(10));
                var killed = completed && killTask.Result;
                if (!killed)
                {
                    lifecycleLogger.LogWarning("Failed to kill instance {Id} (PID {Pid}) during shutdown; leaving as Running",
                        instance.Id, instance.ProcessId);
                    continue;
                }

                var history = shutdownDb.ServerInstanceProcessHistories
                    .Where(h => h.ServerInstanceId == instance.Id
                             && h.ProcessId == instance.ProcessId!.Value
                             && h.EndedAt == null)
                    .OrderByDescending(h => h.StartedAt)
                    .FirstOrDefault();
                if (history is not null)
                {
                    history.EndedAt = DateTime.UtcNow;
                    history.TerminationReason = "Shutdown";
                }

                instance.ProcessId = null;
                instance.Status = ServerInstanceStatus.Stopped;
                instance.StartedAt = null;
            }
            catch (Exception ex)
            {
                lifecycleLogger.LogWarning(ex, "Failed to stop instance {Id} during shutdown", instance.Id);
            }
        }

        if (running.Count > 0)
            shutdownDb.SaveChanges();
    }
    catch (Exception ex)
    {
        lifecycleLogger.LogError(ex, "Error during shutdown process cleanup");
    }
    finally
    {
        shutdownScope.Dispose();
    }
});
app.Lifetime.ApplicationStopped.Register(() =>
{
    lifecycleLogger.LogInformation("KAST web host stopped. ProcessId={ProcessId}", Environment.ProcessId);
    crashReports.RecordCleanShutdown();
});
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    var exception = e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception");
    crashReports.RecordCrash(exception, "UnhandledException", e.IsTerminating);
    lifecycleLogger.LogCritical(exception, "Unhandled AppDomain exception. IsTerminating={IsTerminating}", e.IsTerminating);
};
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    crashReports.RecordCrash(e.Exception, "UnobservedTaskException", false);
    lifecycleLogger.LogError(e.Exception, "Unobserved task exception");
    e.SetObserved();
};

// ── Database migration & startup cleanup ─────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    // Ensure SQLite directory exists
    var dbPath = GetSqliteDataSource(connectionString);
    if (!string.IsNullOrWhiteSpace(dbPath))
    {
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir))
            Directory.CreateDirectory(dbDir);
    }

    var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
    await db.Database.MigrateAsync();

    // Ensure persisted data directories exist after settings have been loaded.
    // Relative paths are resolved against ContentRootPath so service installs
    // and updater scripts do not accidentally switch to a different working
    // directory.
    var settings = await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetSettingsAsync();
    Directory.CreateDirectory(settings.ModsDirectory);
    Directory.CreateDirectory(settings.ServersDirectory);

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

    // Reset any server instances stuck in transitional states from a previous crash.
    // Orphaned Arma processes that survived the crash are killed; their PIDs are
    // logged as Orphaned in the process history before the status is reset to Stopped.
    var processManager = scope.ServiceProvider.GetRequiredService<IProcessManagerService>();
    var stuckInstances = await db.ServerInstances
        .Where(s => s.Status == ServerInstanceStatus.Running
                 || s.Status == ServerInstanceStatus.Starting
                 || s.Status == ServerInstanceStatus.Stopping
                 || s.Status == ServerInstanceStatus.Restarting)
        .ToListAsync();

    foreach (var instance in stuckInstances)
    {
        if (instance.ProcessId.HasValue)
        {
            try
            {
                await processManager.KillProcessAsync(instance.ProcessId.Value, CancellationToken.None);
            }
            catch { /* process already dead or inaccessible */ }

            var history = db.ServerInstanceProcessHistories
                .Where(h => h.ServerInstanceId == instance.Id
                         && h.ProcessId == instance.ProcessId.Value
                         && h.EndedAt == null)
                .OrderByDescending(h => h.StartedAt)
                .FirstOrDefault();
            if (history is not null)
            {
                history.EndedAt = DateTime.UtcNow;
                history.TerminationReason = "Orphaned";
            }
        }

        instance.ProcessId = null;
        instance.Status = ServerInstanceStatus.Stopped;
        instance.StartedAt = null;
    }

    // Also reset orphaned headless clients
    var stuckHCs = await db.HeadlessClients
        .Where(h => h.Status == ServerInstanceStatus.Running && h.ProcessId != null)
        .ToListAsync();
    foreach (var hc in stuckHCs)
    {
        try { await processManager.KillProcessAsync(hc.ProcessId!.Value, CancellationToken.None); }
        catch { }

        hc.ProcessId = null;
        hc.Status = ServerInstanceStatus.Stopped;
    }

    if (stuckInstances.Count > 0 || stuckHCs.Count > 0)
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
                       ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};
foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ??
                      ["127.0.0.1", "::1"])
{
    if (IPAddress.TryParse(proxy, out var address))
        forwardedHeadersOptions.KnownProxies.Add(address);
}
app.UseForwardedHeaders(forwardedHeadersOptions);
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseKastAccountGate();
app.UseAuthorization();

// ── Health checks ─────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapGet("/alive", () => Results.Ok(new
{
    status = "alive",
    timestamp = DateTimeOffset.UtcNow
}));
app.MapGet("/ready", async (IModDownloadQueueService queue, CancellationToken ct) =>
{
    var snapshot = await queue.GetSnapshotAsync(ct);
    return Results.Ok(new
    {
        status = "ready",
        queueActive = snapshot.ActiveCount,
        queueQueued = snapshot.QueuedCount,
        queueFailed = snapshot.FailedCount,
        timestamp = DateTimeOffset.UtcNow
    });
});

app.MapAccountEndpoints();

app.MapMissionDownloadEndpoints();

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
/*_ = Task.Run(async () =>
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
});*/

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

static string ResolveSqliteConnectionString(string contentRoot, string connectionString)
{
    var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
    if (!TryGetSqliteDataSource(builder, out var dataSource) || string.IsNullOrWhiteSpace(dataSource))
        return connectionString;

    var expanded = Environment.ExpandEnvironmentVariables(dataSource);
    if (IsSpecialSqliteDataSource(expanded) || Path.IsPathRooted(expanded))
        return connectionString;

    builder["Data Source"] = Path.GetFullPath(Path.Combine(contentRoot, expanded));
    return builder.ConnectionString;
}

static string? GetSqliteDataSource(string connectionString)
{
    var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
    return TryGetSqliteDataSource(builder, out var dataSource) ? dataSource : null;
}

static bool TryGetSqliteDataSource(DbConnectionStringBuilder builder, out string dataSource)
{
    foreach (string key in builder.Keys)
    {
        if (!string.Equals(key, "Data Source", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(key, "DataSource", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        dataSource = builder[key]?.ToString() ?? "";
        return true;
    }

    dataSource = "";
    return false;
}

static bool IsSpecialSqliteDataSource(string dataSource)
    => string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
       || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

static string ResolveDataProtectionKeysDirectory(string contentRoot, string? configuredPath)
{
    if (!string.IsNullOrWhiteSpace(configuredPath))
        return ResolveConfiguredPath(contentRoot, configuredPath);

    var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    return !string.IsNullOrWhiteSpace(commonAppData)
        ? Path.Combine(commonAppData, "KAST", "DataProtection-Keys")
        : Path.Combine(contentRoot, ".KAST_DATA", "DataProtection-Keys");
}
