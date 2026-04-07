using KAST.Infrastructure;
using KAST.Infrastructure.Data;
using KAST.Web.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.FluentUI.AspNetCore.Components;

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

// Auto-migrate database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
    await db.Database.MigrateAsync();
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

app.Run();
