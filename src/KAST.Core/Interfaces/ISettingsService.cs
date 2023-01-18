using KAST.Data.Models;

namespace KAST.Core.Interfaces
{
    public interface ISettingsService
    {
        GeneralSettings General { get; }
        ThemeSettings Theme { get; }
        ModsSettings Mods { get; }
        AccountSettings Account { get; }
        void Save();
    }
}
