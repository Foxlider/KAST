using System.Diagnostics;
using System.Reflection;

namespace KAST.Core.Models
{
    public static class Utilities
    {
        private static readonly string[] Alist = { "Arma", "Amazing", "Advanced" };
        private static readonly string[] Tlist = { "Tool", "Thing" };

        public static string NameGenerator()
        {
            var _r = new Random();

            return $"Keelah's {Alist[_r.Next(0, Alist.Length)]} Server {Tlist[_r.Next(0, Tlist.Length)]}";
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        public static string GetAppDataPath()
        {
            var appdata = Environment.SpecialFolder.LocalApplicationData;
            var assembly = Assembly.GetExecutingAssembly().GetName();
            var versionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            var path = Environment.GetFolderPath(appdata);
            return Path.Combine(path, versionInfo.CompanyName, assembly.Name);
        }
    }
}
