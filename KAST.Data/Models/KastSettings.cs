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
    }
}
