using KAST.Core.Helpers;
using KAST.Core.Interfaces;
using KAST.Data;
using KAST.Data.Attributes;
using KAST.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace KAST.Core.Services
{
    public class ConfigService : TracedServiceBase, IConfigService
    {
        private readonly IConfiguration _configuration; // gives env vars + appsettings
        private readonly ApplicationDbContext _dbRepository;

        public ConfigService(IConfiguration configuration, ApplicationDbContext dbRepository, 
            ITracingNamingProvider namingProvider, ILogger<ConfigService> logger) 
            : base(namingProvider, logger)
        {
            _configuration = configuration;
            _dbRepository = dbRepository;
        }

        public async Task<KastSettings> GetConfigAsync()
        {
            return await ExecuteWithTelemetryAsync("ConfigService.GetConfig", async (activity) =>
            {
                // Start with DB values
                var config = await _dbRepository.Settings.FirstOrDefaultAsync() ?? new KastSettings();

                activity?.SetTag("config.isNew", config.Id == default(Guid));
                Logger.LogDebug("Retrieved config from database, isNew: {IsNew}", config.Id == default(Guid));

                // Apply environment variable overrides for properties annotated with EnvVariableAttribute
                var envOverrides = EnvAttributeBinder.ApplyEnvironmentVariables(config);
                activity?.SetTag("config.envOverrideCount", envOverrides);
                
                if (envOverrides > 0)
                {
                    Logger.LogInformation("Applied {Count} environment variable overrides to configuration", envOverrides);
                }

                return config;
            });
        }

        public async Task UpdateConfigAsync(KastSettings config)
        {
            await ExecuteWithTelemetryAsync("ConfigService.UpdateConfig", async (activity) =>
            {
                // Ensure we have the current DB record to avoid overwriting DB values with environment-provided overrides.
                var dbConfig = await _dbRepository.Settings.FirstOrDefaultAsync();
                var isNewConfig = dbConfig == null;

                activity?.SetTag("config.isNew", isNewConfig);
                Logger.LogDebug("Updating config, isNew: {IsNew}, ConfigId: {ConfigId}", isNewConfig, config.Id);

                if (dbConfig == null)
                {
                    // First-time insert if no config exists in DB
                    dbConfig = new KastSettings();
                    _dbRepository.Settings.Add(dbConfig);
                    Logger.LogInformation("Creating new configuration record");
                }

                // For any property annotated with EnvVariableAttribute where an environment variable exists,
                // restore the DB value so we don't persist the environment override into the database.
                var props = typeof(KastSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(p => p.CanRead && p.CanWrite);

                var envPropsSkipped = 0;
                var propsUpdated = 0;

                foreach (var prop in props)
                {
                    var attr = prop.GetCustomAttribute<EnvVariableAttribute>();
                    if (attr == null) continue;

                    var envVal = attr != null ? Environment.GetEnvironmentVariable(attr.Key) : null;

                    if (!string.IsNullOrEmpty(envVal))
                    {
                        // Skip environment-backed values → keep DB value unchanged
                        envPropsSkipped++;
                        continue;
                    }

                    // Copy from incoming config into the tracked entity
                    var newValue = prop.GetValue(config);
                    prop.SetValue(dbConfig, newValue);
                    propsUpdated++;
                }

                activity?.SetTag("config.propsUpdated", propsUpdated);
                activity?.SetTag("config.envPropsSkipped", envPropsSkipped);

                Logger.LogDebug("Updated {UpdatedCount} properties, skipped {SkippedCount} environment-backed properties", 
                    propsUpdated, envPropsSkipped);

                // Persist DB-only values (env overrides have been filtered out above)
                await _dbRepository.SaveChangesAsync();
                
                Logger.LogInformation("Configuration updated successfully");
            });
        }
    }
}
