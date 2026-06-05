using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KAST.Infrastructure.Services;

public class SettingsService(KastDbContext db, IConfiguration configuration) : ISettingsService
{
    public async Task<KastSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var settings = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (settings == null)
        {
            // Seed from appsettings.json on first run
            settings = new KastSettings
            {
                ModsDirectory = configuration["Kast:ModsDirectory"] ?? "./mods",
                ServersDirectory = configuration["Kast:ServersDirectory"] ?? "./servers",
                Arma3ServerAppId = int.TryParse(configuration["Kast:Arma3AppId"], out var appId) ? appId : 233780,
                UpdateChannelId = configuration["Kast:UpdateChannelId"] ?? "stable",
                AutoUpdateCheckEnabled = !bool.TryParse(configuration["Kast:AutoUpdateCheckEnabled"], out var autoCheck)
                    || autoCheck
            };
            db.Settings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        // Environment variable overrides (read-only, not persisted)
        var modsEnv = Environment.GetEnvironmentVariable("Kast__ModsDirectory");
        if (!string.IsNullOrEmpty(modsEnv)) settings.ModsDirectory = modsEnv;

        var serversEnv = Environment.GetEnvironmentVariable("Kast__ServersDirectory");
        if (!string.IsNullOrEmpty(serversEnv)) settings.ServersDirectory = serversEnv;

        return settings;
    }

    public async Task UpdateSettingsAsync(KastSettings settings, CancellationToken ct = default)
    {
        var existing = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (existing == null)
        {
            db.Settings.Add(settings);
        }
        else
        {
            existing.ModsDirectory = settings.ModsDirectory;
            existing.ServersDirectory = settings.ServersDirectory;
            existing.Arma3ServerAppId = settings.Arma3ServerAppId;
            existing.ThemeMode = settings.ThemeMode;
            existing.MetricsIntervalSeconds = settings.MetricsIntervalSeconds;
            existing.ParallelDownloads = settings.ParallelDownloads;
            existing.ParallelModDownloads = settings.ParallelModDownloads;
            existing.UpdateChannelId = settings.UpdateChannelId;
            existing.AutoUpdateCheckEnabled = settings.AutoUpdateCheckEnabled;
        }
        await db.SaveChangesAsync(ct);
    }
}
