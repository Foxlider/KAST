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
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using MudBlazor.Services;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ModStatus = KAST.Core.Enums.ModStatus;

var builder = WebApplication.CreateBuilder(args);
var outputSanitizer = new OutputSanitizer(
    new OutputSanitizer.VirtualPathRoot(AppContext.BaseDirectory, "KAST"),
    new OutputSanitizer.VirtualPathRoot(ResolveConfiguredPath(builder.Configuration["Kast:ModsDirectory"] ?? "./mods"), "mods"),
    new OutputSanitizer.VirtualPathRoot(ResolveConfiguredPath(builder.Configuration["Kast:ServersDirectory"] ?? "./servers"), "server"));
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
    config.SnackbarConfiguration.VisibleStateDuration = 2000;
    config.SnackbarConfiguration.HideTransitionDuration = 200;
    config.SnackbarConfiguration.ShowTransitionDuration = 150;
});
builder.Services.AddScoped<KAST.UI.Services.ThemeService>();
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
    var serviceName  = telemetry["ServiceName"]  ?? KastActivitySources.ServiceName;

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
        mod.Status = mod.LocalPath != null ? ModStatus.UpdateAvailable : ModStatus.NotInstalled;
    if (stuckMods.Count > 0)
        await db.SaveChangesAsync();
}

// ── Middleware ────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseStaticFiles();
app.UseAntiforgery();

// ── Health checks ─────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive");

// ── Minimal API groups ────────────────────────────────────────────────────────
app.MapGroup("/api").MapKastApi();

// ── SignalR hubs ──────────────────────────────────────────────────────────────
app.MapHub<MonitoringHub>("/hubs/monitoring");
app.MapHub<DownloadHub>("/hubs/downloads");

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

static string ResolveConfiguredPath(string path) =>
    Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path));

