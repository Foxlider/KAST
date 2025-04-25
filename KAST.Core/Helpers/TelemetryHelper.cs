using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace KAST.Core.Helpers
{
    public interface ITracingNamingProvider
    {
        string GetActivitySourceName(Type serviceType);
    }

    public class TracingNamingProvider : ITracingNamingProvider
    {
        private readonly string _applicationName;

        public TracingNamingProvider(IHostEnvironment environment)
        {
            _applicationName = environment.ApplicationName;
        }

        public string GetActivitySourceName(Type serviceType)
        {
            return $"{_applicationName}.{serviceType.FullName ?? "UnknownService"}";
        }
    }

    public static class Telemetry
    {
        public static readonly ActivitySource Source = new("KAST");
    
        public static Activity? StartActivity(
            string name,
            ActivityKind kind = ActivityKind.Internal,
            IEnumerable<KeyValuePair<string, object?>>? tags = null)
        {
            var activity = Telemetry.Source.StartActivity(name, kind);

            if (activity == null)
                return null;

            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    activity.SetTag(tag.Key, tag.Value);
                }
            }

            return activity;
        }
    }
}
