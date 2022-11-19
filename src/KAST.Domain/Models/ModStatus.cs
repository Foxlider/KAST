using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KAST.Core.Models
{
    public static class ArmaModStatus
    {
        public static string Unknown => "Unknown";
        public static string NotComplete => "Download Not Complete";
        public static string UpToDate => "Up To Date";
        public static string UpdateRequired => "Update Required";
        public static string Local => "Local Mod";
    }
}
