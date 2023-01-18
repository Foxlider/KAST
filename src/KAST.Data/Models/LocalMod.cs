
using KAST.Data.Enums;

namespace KAST.Data.Models
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
