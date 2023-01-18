namespace KAST.Data.Models
{
    public class Setting
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string? Value { get; set; }
    }

    public class GeneralSettings : SettingsBase
    {
        public bool DarkMode { get; set; }
    }

    public class ThemeSettings : SettingsBase
    {
        public string PalettePrimary { get; set; }
        public string PaletteSecondary { get; set; }
        public string PaletteBackground { get; set; }

        public bool DarkTheme { get; set; }
    }

    public class ModsSettings : SettingsBase
    {
        public string? NumberOfWorkers { get; set; }
        public string? ModStatingDir { get; set; }
    }

    public class AccountSettings : SettingsBase
    {
        public string? AccountName { get; set;}
        public string? AccountPassword { get; set;}
        public string? APIKey { get; set;}
    }
}
