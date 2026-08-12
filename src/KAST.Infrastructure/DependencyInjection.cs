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
    public static IServiceCollection AddKastInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<KastDbContext>(options =>
            options.UseSqlite(connectionString,
                o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

        // Steam services
        services.TryAddSingleton<IOutputSanitizer, OutputSanitizer>();
        services.AddSingleton<ISteamService>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SteamClientService>>();
            var sanitizer = sp.GetRequiredService<IOutputSanitizer>();
            return new SteamClientService(logger, sanitizer);
        });

        services.AddSingleton<IProcessManagerService, ProcessManagerService>();
        services.AddScoped<IModService, ModService>();
        services.AddScoped<IServerInstanceService, ServerInstanceService>();
        services.AddScoped<IMonitoringService, MonitoringService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddSingleton<IServerConfigService, ServerConfigService>();

        // Content install system — lives entirely in Infrastructure
        services.AddHttpClient<SteamWebApiClient>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IContentInstaller, LocalModInstaller>();
        services.AddSingleton<IContentInstaller, SteamModInstaller>();
        services.AddSingleton<IContentInstaller, ServerInstaller>();
        services.AddSingleton<ContentProgressTracker>();
        services.AddSingleton<IContentOrchestrator, ContentOrchestrator>();

        return services;
    }
}
