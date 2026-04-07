using KAST.Core.Interfaces;
using KAST.Infrastructure;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Steam;
using KAST.Web.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;
using Microsoft.FluentUI.AspNetCore.Components;
using ModStatus = KAST.Core.Enums.ModStatus;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

// Auto-migrate database and reset any mods stuck in an in-progress state from a previous crash
using (var scope = app.Services.CreateScope())
{
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
