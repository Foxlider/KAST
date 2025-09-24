using KAST.Core.Helpers;
using KAST.Data;
using KAST.Data.Attributes;
using KAST.Data.Interfaces;
using KAST.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace KAST.Core.Services
{
    public class ConfigService : IConfigService
    {
        private readonly IConfiguration _configuration; // gives env vars + appsettings
        private readonly ApplicationDbContext _dbRepository;

        public ConfigService(IConfiguration configuration, ApplicationDbContext dbRepository)
        {
            _configuration = configuration;
            _dbRepository = dbRepository;
        }

        public async Task<KastSettings> GetConfigAsync()
        {
            // Start with DB values
            var config = await _dbRepository.Settings.FirstOrDefaultAsync() ?? new KastSettings();

            // Apply environment variable overrides for properties annotated with EnvVariableAttribute
            EnvAttributeBinder.ApplyEnvironmentVariables(config);

            return config;
        }

        public async Task UpdateConfigAsync(KastSettings config)
        {
            // Ensure we have the current DB record to avoid overwriting DB values with environment-provided overrides.
            var dbConfig = await _dbRepository.Settings.FirstOrDefaultAsync();

            if (dbConfig == null)
            {
                // First-time insert if no config exists in DB
                dbConfig = new KastSettings();
                _dbRepository.Settings.Add(dbConfig);
            }

            // For any property annotated with EnvVariableAttribute where an environment variable exists,
            // restore the DB value so we don't persist the environment override into the database.
            var props = typeof(KastSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead && p.CanWrite);

            foreach (var prop in props)
            {
                var attr = prop.GetCustomAttribute<EnvVariableAttribute>();
                if (attr == null) continue;

                var envVal = attr != null ? Environment.GetEnvironmentVariable(attr.Key) : null;

                if (!string.IsNullOrEmpty(envVal))
                {
                    // Skip environment-backed values → keep DB value unchanged
                    continue;
                }

                // Copy from incoming config into the tracked entity
                var newValue = prop.GetValue(config);
                prop.SetValue(dbConfig, newValue);
            }

            // Persist DB-only values (env overrides have been filtered out above)
            await _dbRepository.SaveChangesAsync();
        }
    }
}
