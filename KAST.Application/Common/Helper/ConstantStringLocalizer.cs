using System.Globalization;
using System.Resources;

namespace KAST.Application.Common.Helper
{
    public static class ConstantStringLocalizer
    {
        public const string CONSTANTSTRINGRESOURCEID = "KAST.Application.Resources.Constants.ConstantString";
        private static readonly ResourceManager rm;
        static ConstantStringLocalizer()
        {
            rm = new ResourceManager(CONSTANTSTRINGRESOURCEID, typeof(ConstantStringLocalizer).Assembly);
        }
        public static string Localize(string key)
        {
            return rm.GetString(key, CultureInfo.CurrentCulture) ?? key;
        }
    }
}