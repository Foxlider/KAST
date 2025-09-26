using KAST.Core.Helpers;
using KAST.Data.Models;
using Microsoft.Extensions.Logging;

namespace KAST.Core.Services
{
    public class ThemeService : TracedServiceBase
    {
        private readonly ConfigService _configService;
        private bool _isDarkMode = false;
        
        public event Action<bool>? OnThemeChanged;

        public ThemeService(ConfigService configService, ITracingNamingProvider namingProvider, 
            ILogger<ThemeService> logger) : base(namingProvider, logger)
        {
            _configService = configService;
        }

        public bool IsDarkMode => _isDarkMode;

        public async Task InitializeAsync()
        {
            await ExecuteWithTelemetryAsync("ThemeService.Initialize", async (activity) =>
            {
                var config = await _configService.GetConfigAsync();
                activity?.SetTag("theme.mode", config.ThemeMode);
                
                await UpdateThemeFromSettings(config.ThemeMode);
                
                activity?.SetTag("theme.isDarkMode", _isDarkMode);
                Logger.LogInformation("Theme service initialized with mode: {ThemeMode}, isDark: {IsDark}", 
                    config.ThemeMode, _isDarkMode);
            });
        }

        public async Task SetThemeModeAsync(string themeMode)
        {
            await ExecuteWithTelemetryAsync("ThemeService.SetThemeMode", async (activity) =>
            {
                activity?.SetTag("theme.newMode", themeMode);
                activity?.SetTag("theme.currentMode", _isDarkMode ? "dark" : "light");
                
                var config = await _configService.GetConfigAsync();
                config.ThemeMode = themeMode;
                await _configService.UpdateConfigAsync(config);
                await UpdateThemeFromSettings(themeMode);
                
                activity?.SetTag("theme.finalMode", _isDarkMode ? "dark" : "light");
                Logger.LogInformation("Theme mode changed to: {ThemeMode}, resulting in dark mode: {IsDark}", 
                    themeMode, _isDarkMode);
            }, new[] { new KeyValuePair<string, object?>("theme.mode", themeMode) });
        }

        public async Task ToggleDarkModeAsync()
        {
            await ExecuteWithTelemetryAsync("ThemeService.ToggleDarkMode", async (activity) =>
            {
                var currentMode = _isDarkMode ? "dark" : "light";
                var newMode = _isDarkMode ? "light" : "dark";
                
                activity?.SetTag("theme.from", currentMode);
                activity?.SetTag("theme.to", newMode);
                
                await SetThemeModeAsync(newMode);
                
                Logger.LogInformation("Theme toggled from {FromMode} to {ToMode}", currentMode, newMode);
            });
        }

        private async Task UpdateThemeFromSettings(string? themeMode)
        {
            await ExecuteWithTelemetryAsync("ThemeService.UpdateThemeFromSettings", async (activity) =>
            {
                activity?.SetTag("theme.settingsMode", themeMode);
                
                bool newDarkMode = themeMode switch
                {
                    "dark" => true,
                    "light" => false,
                    "auto" => await GetSystemPreferenceAsync(),
                    _ => false
                };

                var changed = _isDarkMode != newDarkMode;
                activity?.SetTag("theme.changed", changed);
                activity?.SetTag("theme.newDarkMode", newDarkMode);

                if (changed)
                {
                    _isDarkMode = newDarkMode;
                    OnThemeChanged?.Invoke(_isDarkMode);
                    Logger.LogDebug("Theme updated from settings: {ThemeMode} -> isDark: {IsDark}", themeMode, _isDarkMode);
                }
            });
        }

        private async Task<bool> GetSystemPreferenceAsync()
        {
            return await ExecuteWithTelemetryAsync("ThemeService.GetSystemPreference", async (activity) =>
            {
                // For now, default to light mode for "auto"
                // In a real implementation, this could detect system preference
                await Task.CompletedTask;
                
                var preference = false; // Default to light
                activity?.SetTag("system.darkMode", preference);
                
                Logger.LogDebug("Retrieved system theme preference: {IsDark}", preference);
                return preference;
            });
        }
    }
}