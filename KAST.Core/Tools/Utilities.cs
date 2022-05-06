using System;
using System.Collections.Generic;
using System.Text;

namespace KAST.Core.Tools
{
    internal static class Utilities
    {
        private static readonly string[] Alist = { "Arma", "Amazing", "Advanced"};
        private static readonly string[] Tlist = { "Tool", "Thing"};



        internal static string NameGenerator()
        {
            var _r = new Random();

            return $"Keelah's {Alist[_r.Next(0, Alist.Length)]} Server {Tlist[_r.Next(0, Tlist.Length)]}";
        }
    }
}
