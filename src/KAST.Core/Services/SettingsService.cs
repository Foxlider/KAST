using KAST.Core.Interfaces;
using KAST.Data;
using KAST.Data.Models;

namespace KAST.Core.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly KastDbContext _context;


        private readonly Lazy<GeneralSettings> _generalSettings;
        public GeneralSettings General { get { return _generalSettings.Value; } }

        private readonly Lazy<ThemeSettings> _themeSettings;
        public ThemeSettings Theme { get { return _themeSettings.Value; } }

        private readonly Lazy<ModsSettings> _modsSettings;
        public ModsSettings Mods { get { return _modsSettings.Value; } }

        private readonly Lazy<AccountSettings> _accountSettings;
        public AccountSettings Account { get { return _accountSettings.Value; } }


        public SettingsService(KastDbContext context)
        {
            _context = context;

            _generalSettings = new Lazy<GeneralSettings>(CreateSettings<GeneralSettings>);
            _themeSettings = new Lazy<ThemeSettings>(CreateSettings<ThemeSettings>);
            _modsSettings = new Lazy<ModsSettings>(CreateSettings<ModsSettings>);
            _accountSettings = new Lazy<AccountSettings>(CreateSettings<AccountSettings>);
        }

        public void Save()
        {
            // only save changes to settings that have been loaded
            if (_generalSettings.IsValueCreated)
                _generalSettings.Value.Save(_context);

            if (_themeSettings.IsValueCreated)
                _themeSettings.Value.Save(_context);

            if (_modsSettings.IsValueCreated)
                _modsSettings.Value.Save(_context);

            if (_accountSettings.IsValueCreated)
                _accountSettings.Value.Save(_context);

            _context.SaveChanges();
        }

        private T CreateSettings<T>() where T : SettingsBase, new()
        {
            var settings = new T();
            settings.Load(_context);
            return settings;
        }
    }


}
