using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KAST.Infrastructure.Services;

public class SettingsService(KastDbContext db, IConfiguration configuration, IHostEnvironment? hostEnvironment = null) : ISettingsService
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
                    || autoCheck,
                SystemAuthEnabled = bool.TryParse(configuration["Auth:System:Enabled"], out var systemEnabled) && systemEnabled,
                SystemAuthDomain = configuration["Auth:System:Domain"],
                SystemAuthSource = configuration["Auth:System:Source"] ?? "Auto"
            };
            db.Settings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        var effective = new KastSettings
        {
            Id = settings.Id,
            ModsDirectory = settings.ModsDirectory,
            ServersDirectory = settings.ServersDirectory,
            Arma3ServerAppId = settings.Arma3ServerAppId,
            ThemeMode = settings.ThemeMode,
            MetricsIntervalSeconds = settings.MetricsIntervalSeconds,
            ParallelDownloads = settings.ParallelDownloads,
            ParallelModDownloads = settings.ParallelModDownloads,
            UpdateChannelId = settings.UpdateChannelId,
            AutoUpdateCheckEnabled = settings.AutoUpdateCheckEnabled,
            SystemAuthEnabled = settings.SystemAuthEnabled,
            SystemAuthDomain = settings.SystemAuthDomain,
            SystemAuthSource = settings.SystemAuthSource
        };

        // Environment variable overrides (read-only, not persisted)
        var modsEnv = Environment.GetEnvironmentVariable("Kast__ModsDirectory");
        if (!string.IsNullOrEmpty(modsEnv)) effective.ModsDirectory = modsEnv;

        var serversEnv = Environment.GetEnvironmentVariable("Kast__ServersDirectory");
        if (!string.IsNullOrEmpty(serversEnv)) effective.ServersDirectory = serversEnv;

        effective.ModsDirectory = ResolveConfiguredPath(effective.ModsDirectory);
        effective.ServersDirectory = ResolveConfiguredPath(effective.ServersDirectory);

        return effective;
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
            existing.SystemAuthEnabled = settings.SystemAuthEnabled;
            existing.SystemAuthDomain = string.IsNullOrWhiteSpace(settings.SystemAuthDomain)
                ? null
                : settings.SystemAuthDomain.Trim();
            existing.SystemAuthSource = string.IsNullOrWhiteSpace(settings.SystemAuthSource)
                ? "Auto"
                : settings.SystemAuthSource.Trim();
        }
        await db.SaveChangesAsync(ct);
    }

    private string ResolveConfiguredPath(string path)
    {
        if (hostEnvironment is null || string.IsNullOrWhiteSpace(path))
            return path;

        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.GetFullPath(
            Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(hostEnvironment.ContentRootPath, expanded));
    }
}
