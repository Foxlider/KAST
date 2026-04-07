using KAST.Core.Interfaces;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Services;
using KAST.Infrastructure.Steam;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KAST.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKastInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<KastDbContext>(options =>
            options.UseSqlite(connectionString));

        // Steam services
        services.AddSingleton<ISteamService>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SteamClientService>>();
            return new SteamClientService(logger);
        });

        services.AddSingleton<IProcessManagerService, ProcessManagerService>();
        services.AddScoped<IModService, ModService>();
        services.AddScoped<IServerInstanceService, ServerInstanceService>();
        services.AddScoped<IMonitoringService, MonitoringService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddSingleton<IServerConfigService, ServerConfigService>();

        return services;
    }
}
