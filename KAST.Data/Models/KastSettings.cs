using KAST.Data.Attributes;
using System.ComponentModel.DataAnnotations;

namespace KAST.Data.Models
{
    public class KastSettings
    {
        [Key]
        public Guid Id { get; set; } = Guid.AllBitsSet;

        /// <summary>
        /// Gets or sets the path to the folder where mods are stored.
        /// </summary>
        [EnvVariableAttribute("KAST_MOD_FOLDER_PATH")]
        public string? ModFolderPath {  get; set; }

        /// <summary>
        /// Gets or sets the theme accent color for the application.
        /// </summary>
        [MaxLength(10)]
        [EnvVariableAttribute("KAST_THEME_ACCENT")]
        public string? ThemeAccent { get; set; }

        /// <summary>
        /// Gets or sets the API key used for Steam requests.
        /// </summary>
        [EnvVariableAttribute("KAST_STEAM_API_KEY")]
        public string? ApiKey { get; set; }

        /// <summary>
        /// Gets or sets the default Arma server path.
        /// </summary>
        [EnvVariableAttribute("KAST_SERVER_DEFAULT_PATH")]
        public string? ServerDefaultPath { get; set; }

        /// <summary>
        /// Gets or sets the theme mode (light, dark, auto).
        /// </summary>
        [MaxLength(10)]
        [EnvVariableAttribute("KAST_THEME_MODE")]
        public string? ThemeMode { get; set; } = "auto";

        /// <summary>
        /// Gets or sets whether to check for updates on startup.
        /// </summary>
        [EnvVariableAttribute("KAST_CHECK_UPDATES")]
        public bool? CheckForUpdates { get; set; } = true;

        /// <summary>
        /// Gets or sets the UI language/locale.
        /// </summary>
        [MaxLength(10)]
        [EnvVariableAttribute("KAST_LANGUAGE")]
        public string? Language { get; set; } = "en";

        /// <summary>
        /// Gets or sets whether to enable debug logging.
        /// </summary>
        [EnvVariableAttribute("KAST_DEBUG_LOGGING")]
        public bool? DebugLogging { get; set; } = false;
    }
}
