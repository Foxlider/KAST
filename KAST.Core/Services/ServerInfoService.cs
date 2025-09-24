using KAST.Core.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace KAST.Core.Services
{
    public interface ISystemMetricsService
    {
        /// <summary>
        /// Retrieves the current CPU usage as a percentage across all processor cores.
        /// </summary>
        /// <returns>A double value representing the percentage of CPU usage across all cores.  The value ranges from 0.0 to
        /// 100.0, where 0.0 indicates no usage and 100.0 indicates full utilization.</returns>
        double GetCpuUsage();   // % CPU usage across all cores

        /// <summary>
        /// Retrieves the current memory usage of the system in kilobytes (KB).
        /// </summary>
        /// <remarks>This method provides a snapshot of the system's memory usage at the time of
        /// invocation. The returned value represents the total memory in use and may vary depending on system
        /// activity.</remarks>
        /// <returns>The amount of RAM currently in use, measured in kilobytes (KB).</returns>
        double GetMemUsage();   // RAM Memory in use in kb
    }


    public class ServerInfoService(ITracingNamingProvider namingProvider) : TracedServiceBase(namingProvider), ISystemMetricsService
    {
        private readonly Lock _initLock = new();
        private Task? _initTask;
        private volatile bool _initialized;

        /// <summary>
        /// Gets the name of the machine on which the application is running.
        /// </summary>
        public static string Hostname => Environment.MachineName;

        /// <summary>
        /// Gets the current operating system platform on which the application is running.
        /// </summary>
        /// <remarks>This property determines the platform by checking the runtime environment and returns
        /// the corresponding <see cref="OSPlatform"/> value.</remarks>
        public static OSPlatform Platform => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows :
                                  RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OSPlatform.Linux :
                                  RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX :
                                  OSPlatform.FreeBSD; // Fallback

        /// <summary>
        /// Gets a description of the current operating system.
        /// </summary>
        /// <remarks>The format and content of the description may vary depending on the operating system.
        /// For example, on Windows, it may include the version and build number, while on Linux, it may include the
        /// distribution name and version.</remarks>
        public static string OSDescription => RuntimeInformation.OSDescription;

        /// <summary>
        /// Gets the architecture of the operating system on which the application is running.
        /// </summary>
        /// <remarks>This property provides information about the underlying operating system's
        /// architecture, which can be useful for  making platform-specific decisions or optimizations in your
        /// application.</remarks>
        public static Architecture OSArchitecture => RuntimeInformation.OSArchitecture;

        /// <summary>
        /// Gets the number of processors available on the current machine.
        /// </summary>
        /// <remarks>This property retrieves the number of logical processors, which may include both
        /// physical cores and logical cores created by technologies such as hyper-threading. The value is determined by
        /// the underlying operating system.</remarks>
        public static int ProcessorCount => Environment.ProcessorCount;

        /// <summary>
        /// Retrieves the current CPU usage percentage for the system.
        /// </summary>
        /// <remarks>This method supports Windows and Linux platforms. On unsupported platforms,  a <see
        /// cref="PlatformNotSupportedException"/> is thrown. The returned value  represents the CPU usage as a
        /// percentage, where 0 indicates no usage and 100  indicates full utilization.</remarks>
        /// <returns>A <see cref="double"/> representing the current CPU usage percentage.</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown if the operating system is not Windows or Linux.</exception>
        public double GetCpuUsage()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetCpuUsageWindows();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return GetCpuUsageLinux();

            throw new PlatformNotSupportedException("Only Windows and Linux are supported.");
        }

        /// <summary>
        /// Retrieves the current memory usage of the system.
        /// </summary>
        /// <remarks>This method determines the memory usage based on the operating system.  It supports
        /// Windows and Linux platforms. If called on an unsupported platform,  a <see
        /// cref="PlatformNotSupportedException"/> is thrown.</remarks>
        /// <returns>A <see cref="double"/> representing the current memory usage in megabytes.</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown if the method is called on an unsupported platform.</exception>
        public double GetMemUsage()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetMemUsageWindows();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return GetMemUsageLinux();

            throw new PlatformNotSupportedException("Only Windows and Linux are supported.");
        }

        /// <summary>
        /// Represents the maximum memory, in bytes, that can be allocated or used.
        /// </summary>
        /// <remarks>This field is intended to store a memory limit value. Ensure that the value assigned
        /// is appropriate for the application's memory requirements.</remarks>
        public ulong MaxMem;

        // ----------------------
        // Windows implementation
        // ----------------------

        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;

        [SupportedOSPlatform("windows")]
        private void EnsureInitializationStarted()
        {
            if (_initialized || _initTask != null)
                return;

            lock (_initLock)
            {
                if (_initialized || _initTask != null)
                    return;

                // Start initialization on a background thread so DI or UI startup is not blocked.
                _initTask = Task.Run(() =>
                {
                    try
                    {
                        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                            return;

                        if (PerformanceCounterCategory.Exists("Processor") &&
                            PerformanceCounterCategory.CounterExists("% Processor Time", "Processor"))
                        {
                            _cpuCounter = new PerformanceCounter
                            {
                                CategoryName = "Processor",
                                CounterName = "% Processor Time",
                                InstanceName = "_Total"
                            };
                            // Prime the counter once (safe on background thread).
                            try { _cpuCounter.NextValue(); } catch { /* ignore */ }
                        }

                        if (PerformanceCounterCategory.Exists("Memory") &&
                            PerformanceCounterCategory.CounterExists("Available MBytes", "Memory"))
                        {
                            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                            try { _ramCounter.NextValue(); } catch { /* ignore */ }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Do not throw during background init; log and continue with NaN results.
                        Console.WriteLine($"ServerInfoService initialization failed: {ex}");
                    }
                    finally
                    {
                        _initialized = true;
                    }
                });
            }
        }

        [SupportedOSPlatform("windows")]
        public double GetCpuUsageWindows()
        {
            EnsureInitializationStarted();

            if (!_initialized || _cpuCounter == null)
                return float.NaN;

            try
            {
                return _cpuCounter.NextValue();
            }
            catch
            {
                return float.NaN;
            }
        }

        [SupportedOSPlatform("windows")]
        public double GetMemUsageWindows()
        {
            EnsureInitializationStarted();

            if (!_initialized || _ramCounter == null)
                return float.NaN;

            try
            {
                return _ramCounter.NextValue();
            }
            catch
            {
                return float.NaN;
            }
        }

        // ----------------------
        // Linux implementation
        // ----------------------
        // Linux state
        private ulong _prevIdle = 0, _prevTotal = 0;

        private double GetCpuUsageLinux()
        {
            ReadCpuStat(out var idle, out var total);

            var idleDiff = idle - _prevIdle;
            var totalDiff = total - _prevTotal;

            _prevIdle = idle;
            _prevTotal = total;

            if (totalDiff == 0) return 0;

            return Math.Round(100.0 * (1.0 - ((double)idleDiff / totalDiff)), 2);
        }

        private void ReadCpuStat(out ulong idle, out ulong total)
        {
            var parts = System.IO.File.ReadAllText("/proc/stat")
                .Split('\n')[0]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            ulong user = ulong.Parse(parts[1]);
            ulong nice = ulong.Parse(parts[2]);
            ulong system = ulong.Parse(parts[3]);
            idle = ulong.Parse(parts[4]);
            ulong iowait = ulong.Parse(parts[5]);
            ulong irq = ulong.Parse(parts[6]);
            ulong softirq = ulong.Parse(parts[7]);
            ulong steal = parts.Length > 8 ? ulong.Parse(parts[8]) : 0;

            total = user + nice + system + idle + iowait + irq + softirq + steal;
        }

        private double GetMemUsageLinux()
        {
            var lines = System.IO.File.ReadAllLines("/proc/meminfo");
            ulong available = 0;
            foreach (var line in lines)
            {
                if (line.StartsWith("MemTotal:")) MaxMem = ParseMemLine(line);
                else if (line.StartsWith("MemAvailable:")) available = ParseMemLine(line);
            }

            if (MaxMem == 0) return 0;
            ulong used = MaxMem - available;
            return Math.Round((double)used, 2);
        }

        private ulong ParseMemLine(string line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return ulong.Parse(parts[1]) * 1024; // kB -> bytes
        }
    }
}
