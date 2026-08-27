using KAST.Core.Interfaces;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Services;
using KAST.Infrastructure.Services.Content;
using KAST.Infrastructure.Steam;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KAST.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKastInfrastructure(
        this IServiceCollection services,
        string connectionString,
        Uri steamWebApiBaseAddress)
    {
        services.AddDbContext<KastDbContext>(options =>
            options.UseSqlite(connectionString,
                o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

        // Steam services
        services.TryAddSingleton<IOutputSanitizer, OutputSanitizer>();
        services.AddSingleton<ISteamDownloadScheduler, SteamDownloadScheduler>();
        services.AddSingleton<SteamClientService>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SteamClientService>>();
            var sanitizer = sp.GetRequiredService<IOutputSanitizer>();
            var downloadScheduler = sp.GetRequiredService<ISteamDownloadScheduler>();
            return new SteamClientService(logger, sanitizer, downloadScheduler);
        });
        services.AddSingleton<ISteamAuthenticationService>(sp => sp.GetRequiredService<SteamClientService>());
        services.AddSingleton<ISteamWorkshopCatalogService>(sp => sp.GetRequiredService<SteamClientService>());
        services.AddSingleton<ISteamWorkshopDownloadService>(sp => sp.GetRequiredService<SteamClientService>());
        services.AddSingleton<ISteamAppDownloadService>(sp => sp.GetRequiredService<SteamClientService>());
        services.AddSingleton<ISteamDownloadBenchmarkService>(sp => sp.GetRequiredService<SteamClientService>());

        services.AddSingleton<IProcessManagerService, ProcessManagerService>();
        services.AddSingleton<IModDownloadCancellationRegistry, ModDownloadCancellationRegistry>();
        services.AddScoped<IModService, ModService>();
        services.AddScoped<IModUpdateCoordinator, CoordinatedModService>();
        services.AddScoped<IServerInstanceService, ServerInstanceService>();
        services.AddScoped<IMonitoringService, MonitoringService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddSingleton<IServerConfigService, ServerConfigService>();

        // Content install system — lives entirely in Infrastructure
        services.AddHttpClient<SteamWebApiClient>(client =>
        {
            client.BaseAddress = steamWebApiBaseAddress;
        });
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IContentInstaller, LocalModInstaller>();
        services.AddSingleton<IContentInstaller, SteamModInstaller>();
        services.AddSingleton<IContentInstaller, ServerInstaller>();
        services.AddSingleton<ContentProgressTracker>();
        services.AddSingleton<IContentOrchestrator, ContentOrchestrator>();

        return services;
    }
}
