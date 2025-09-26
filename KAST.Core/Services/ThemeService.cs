using KAST.Data.Models;

namespace KAST.Core.Services
{
    public class ThemeService
    {
        private readonly ConfigService _configService;
        private bool _isDarkMode = false;
        
        public event Action<bool>? OnThemeChanged;

        public ThemeService(ConfigService configService)
        {
            _configService = configService;
        }

        public bool IsDarkMode => _isDarkMode;

        public async Task InitializeAsync()
        {
            var config = await _configService.GetConfigAsync();
            await UpdateThemeFromSettings(config.ThemeMode);
        }

        public async Task SetThemeModeAsync(string themeMode)
        {
            var config = await _configService.GetConfigAsync();
            config.ThemeMode = themeMode;
            await _configService.UpdateConfigAsync(config);
            await UpdateThemeFromSettings(themeMode);
        }

        public async Task ToggleDarkModeAsync()
        {
            var newMode = _isDarkMode ? "light" : "dark";
            await SetThemeModeAsync(newMode);
        }

        private async Task UpdateThemeFromSettings(string? themeMode)
        {
            bool newDarkMode = themeMode switch
            {
                "dark" => true,
                "light" => false,
                "auto" => await GetSystemPreferenceAsync(),
                _ => false
            };

            if (_isDarkMode != newDarkMode)
            {
                _isDarkMode = newDarkMode;
                OnThemeChanged?.Invoke(_isDarkMode);
            }
        }

        private async Task<bool> GetSystemPreferenceAsync()
        {
            // For now, default to light mode for "auto"
            // In a real implementation, this could detect system preference
            await Task.CompletedTask;
            return false;
        }
    }
}