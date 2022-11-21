using KAST.Core.Enums;

namespace KAST.Core.Models
{
    public class LocalMod : Mod
    {
        public LocalMod()
        {
            IsLocal= true;
            Status = ModStatus.Local;
        }
    }
}
