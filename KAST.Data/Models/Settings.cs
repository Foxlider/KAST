using System.ComponentModel.DataAnnotations;

namespace KAST.Data.Models
{
    public class Settings
    {
        [Key]
        public Guid Id { get; set; }

        public string ModFolderPath {  get; set; }


        [MaxLength(10)]
        public string ThemeAccent { get; set; }
    }
}
