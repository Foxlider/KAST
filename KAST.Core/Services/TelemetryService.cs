using KAST.Core.Helpers;
using System.Diagnostics;

namespace KAST.Core.Services
{
    public interface ITelemetryService
    {
        Activity? StartActivity(string name, object callerInstance);
    }

    public class TelemetryService : ITelemetryService
    {
        private readonly ITracingNamingProvider _namingProvider;

        public TelemetryService(ITracingNamingProvider namingProvider)
        {
            _namingProvider = namingProvider;
        }

        public Activity? StartActivity(string name, object callerInstance)
        {
            var sourceName = _namingProvider.GetActivitySourceName(callerInstance.GetType());
            var source = new ActivitySource(sourceName);
            return source.StartActivity(name, ActivityKind.Internal);
        }
    }
}
