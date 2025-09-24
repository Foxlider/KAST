using KAST.Data.Attributes;
using KAST.Data.Models;
using System.Reflection;

namespace KAST.Core.Extensions
{
    public static class SettingsExtensions
    {
        public static bool IsEnvLocked(this KastSettings settings, string propertyName)
        {
            var prop = typeof(KastSettings).GetProperty(propertyName);
            if (prop == null) return false;

            var attr = prop.GetCustomAttribute<EnvVariableAttribute>();
            return attr?.IsOverridden() ?? false;
        }
    }
}
