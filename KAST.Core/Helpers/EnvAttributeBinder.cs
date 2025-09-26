using KAST.Data.Attributes;
using System.Globalization;
using System.Reflection;

namespace KAST.Core.Helpers
{
    public static class EnvAttributeBinder
    {
        /// <summary>
        /// Populate public writable properties on the instance from environment variables
        /// based on the EnvVariableAttribute applied to those properties.
        /// Supports string, primitive types, nullable primitives and enums.
        /// </summary>
        /// <returns>The number of properties that were overridden by environment variables</returns>
        public static int ApplyEnvironmentVariables<T>(T instance) where T : class
        {
            ArgumentNullException.ThrowIfNull(instance);

            var type = instance.GetType();
            var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                            .Where(p => p.CanWrite);

            var overrideCount = 0;

            foreach (var prop in props)
            {
                var attr = prop.GetCustomAttribute<EnvVariableAttribute>();
                if (attr == null) continue;

                var key = attr.Key;
                var raw = Environment.GetEnvironmentVariable(key);

                if (string.IsNullOrEmpty(raw))
                {
                    raw = attr.DefaultValue;
                    if (raw == null) continue; // nothing to set
                }

                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                object? converted;
                try
                {
                    if (targetType == typeof(string))
                    {
                        converted = raw;
                    }
                    else if (targetType.IsEnum)
                    {
                        converted = Enum.Parse(targetType, raw, ignoreCase: true);
                    }
                    else
                    {
                        converted = Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
                    }

                    prop.SetValue(instance, converted);
                    overrideCount++;
                }
                catch
                {
                    // swallow conversion errors — optionally log or rethrow in your app
                }
            }

            return overrideCount;
        }
    }
}
