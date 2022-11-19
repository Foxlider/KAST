using KAST.Core.Enums;

namespace KAST.Core.Models
{
    public abstract class Mod
    {
        public Guid Id { get; set; }
        public string? Name;
        public string? Path;
        public bool IsLocal { get; internal set; }
        public ModStatus? Status;
        public ulong ActualSize;
        public virtual Author? Author { get; set; }
    }
}
