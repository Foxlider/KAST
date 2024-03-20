using System.Diagnostics;
using System.Runtime.Versioning;

namespace KAST.Core.Services
{
    public class ServerInfoService
    {
        private readonly PerformanceCounter? _cpuCounter;
        private readonly PerformanceCounter? _ramCounter;

        [SupportedOSPlatform("windows")]
        public ServerInfoService()
        {
            if (PerformanceCounterCategory.Exists("Processor") && PerformanceCounterCategory.CounterExists("% Processor Time", "Processor"))
            {
                _cpuCounter = new PerformanceCounter { CategoryName = "Processor", CounterName = "% Processor Time", InstanceName = "_Total" };
                Console.WriteLine("Processor Time: {0}%", _cpuCounter.NextValue());
            }

            if (PerformanceCounterCategory.Exists("Memory") && PerformanceCounterCategory.CounterExists("Available MBytes", "Memory"))
            {
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                Console.WriteLine("Available MBytes: {0}%", _ramCounter.NextValue());
            }
        }

        [SupportedOSPlatform ("windows")]
        public float GetCpuUsage()
        {
            if (_cpuCounter == null)
                return float.NaN;
            var val = _cpuCounter.NextValue();
            return val;
        }

        [SupportedOSPlatform("windows")]
        public float GetMemUsage()
        {
            if (_ramCounter == null)
                return float.NaN;
            var val = _ramCounter.NextValue();
            return val;
        }
    }
}
