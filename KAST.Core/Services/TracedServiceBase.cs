using KAST.Core.Helpers;
using System.Diagnostics;

namespace KAST.Core.Services
{
    public abstract class TracedServiceBase
    {
        protected readonly ActivitySource ActivitySource;


        protected TracedServiceBase(ITracingNamingProvider namingProvider)
        {
            var sourceName = namingProvider.GetActivitySourceName(GetType());
            ActivitySource = new ActivitySource(sourceName);
        }
    }
}
