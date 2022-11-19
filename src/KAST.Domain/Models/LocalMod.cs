using KAST.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
