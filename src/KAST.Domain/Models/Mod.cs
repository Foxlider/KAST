using KAST.Core.Enums;

namespace KAST.Core.Models
{
    public abstract class Mod
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Path { get; set; }
        public bool IsLocal { get; internal set; }
        public ModStatus? Status { get; set; }
        public ulong ActualSize { get; set; }
        public virtual Author? Author { get; set; }
    }
}
