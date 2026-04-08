using KAST.Core.Interfaces;
using KAST.Infrastructure;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Steam;
using KAST.Web.Hubs;
using KAST.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using ModStatus = KAST.Core.Enums.ModStatus;

var builder = WebApplication.CreateBuilder(args);

// In-memory log store (captures logs from startup for the UI console)
var kastLogStore = new KastLogStore();
builder.Services.AddSingleton(kastLogStore);
builder.Logging.AddProvider(new KastLoggerProvider(kastLogStore));

// Health checks
builder.Services.AddHealthChecks();

// Database
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=kast.db";
builder.Services.AddKastInfrastructure(connectionString);

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();

// API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// SignalR
builder.Services.AddSignalR();

// HttpClient for health checks (status footer)
builder.Services.AddHttpClient("HealthCheck", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<IAppEventBroadcaster, SignalREventBroadcaster>();

// Server install services
builder.Services.AddSingleton<ServerInstallProgressStore>();
builder.Services.AddSingleton<ServerInstallService>();

// Background services
builder.Services.AddHostedService<MetricsBackgroundService>();
builder.Services.AddHostedService<ProcessWatchdogService>();
builder.Services.AddHostedService<SchedulingBackgroundService>();

var app = builder.Build();

// Auto-migrate database and reset any mods stuck in an in-progress state from a previous crash
using (var scope = app.Services.CreateScope())
{
    // Ensure data directories exist (for dev .KAST_DATA/ or production /app/data/)
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var modsDir = config["Kast:ModsDirectory"] ?? "./mods";
    var serversDir = config["Kast:ServersDirectory"] ?? "./servers";
    Directory.CreateDirectory(modsDir);
    Directory.CreateDirectory(serversDir);

    // Ensure the SQLite directory exists
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

    // If the app crashed mid-download/update, reset stuck statuses so the user can retry
    var stuckMods = await db.Mods
        .Where(m => m.Status == ModStatus.Downloading || m.Status == ModStatus.Updating)
        .ToListAsync();
    foreach (var mod in stuckMods)
        mod.Status = mod.LocalPath != null ? ModStatus.UpdateAvailable : ModStatus.NotInstalled;
    if (stuckMods.Count > 0)
        await db.SaveChangesAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAntiforgery();

// Health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive");

// Map API controllers under /api
app.MapControllers();

// Map SignalR hubs
app.MapHub<MonitoringHub>("/hubs/monitoring");
app.MapHub<DownloadHub>("/hubs/downloads");

// Map Blazor
app.MapRazorComponents<KAST.Web.Components.App>()
    .AddInteractiveServerRenderMode();

// Steam startup — restore cached account if available, otherwise connect anonymously
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
            // Cached token was rejected — clear happened inside the service; fall through to anon
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
