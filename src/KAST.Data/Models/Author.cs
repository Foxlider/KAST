using System.ComponentModel.DataAnnotations;

namespace KAST.Data.Models
{
    public class Author
    {
        public Guid Id { get; set; }
        public ulong AuthorID { get; set; }
        public string Name { get; set; }
        public string? URL { get ; set; }
    }
}
