using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KAST.Core.Models
{
    public class SteamMod : Mod
    {
        public ulong SteamID;
        public string? Url { get; set; }
        public DateTime? SteamLastUpdated { get; set; }
        public DateTime? LocalLastUpdated { get; set; }
        public ulong? ExpectedSize { get; set; }


        public SteamMod() : base()
        {
            IsLocal = false;
        }
    }
}
