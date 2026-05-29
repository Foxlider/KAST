using System.Diagnostics;
using System.Runtime.InteropServices;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace KAST.Infrastructure.Services;

public class MonitoringService(IProcessManagerService processManager, Data.KastDbContext db) : IMonitoringService
{
    public async Task<HostMetrics> GetHostMetricsAsync(CancellationToken ct = default)
    {
        var metrics = new HostMetrics();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await ReadLinuxCpuAsync(metrics, ct);
            ReadLinuxMemory(metrics);
            ReadLinuxDisk(metrics);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await ReadWindowsMetricsAsync(metrics, ct);
        }

        return metrics;
    }

    public async Task<InstanceMetrics?> GetInstanceMetricsAsync(int serverInstanceId, CancellationToken ct = default)
    {
        var instance = await db.ServerInstances.FindAsync([serverInstanceId], ct);
        if (instance?.ProcessId == null)
            return null;

        var processMetrics = await processManager.GetProcessMetricsAsync(instance.ProcessId.Value, ct);
        if (processMetrics == null)
            return null;

        return new InstanceMetrics
        {
            ServerInstanceId = serverInstanceId,
            CpuUsagePercent = processMetrics.Value.CpuPercent,
            MemoryUsageBytes = processMetrics.Value.MemoryBytes
        };
    }

    public async Task<IReadOnlyList<InstanceMetrics>> GetAllInstanceMetricsAsync(CancellationToken ct = default)
    {
        var instances = await db.ServerInstances
            .AsNoTracking()
            .Where(s => s.ProcessId != null)
            .Select(s => new { s.Id, ProcessId = s.ProcessId!.Value })
            .ToListAsync(ct);

        var samples = await Task.WhenAll(instances.Select(async instance =>
        {
            var processMetrics = await processManager.GetProcessMetricsAsync(instance.ProcessId, ct);
            return processMetrics is null
                ? null
                : new InstanceMetrics
                {
                    ServerInstanceId = instance.Id,
                    CpuUsagePercent = processMetrics.Value.CpuPercent,
                    MemoryUsageBytes = processMetrics.Value.MemoryBytes
                };
        }));

        return samples.Where(m => m is not null).Select(m => m!).ToList();
    }

    private static async Task ReadLinuxCpuAsync(HostMetrics metrics, CancellationToken ct)
    {
        var stat1 = await File.ReadAllTextAsync("/proc/stat", ct);
        await Task.Delay(500, ct);
        var stat2 = await File.ReadAllTextAsync("/proc/stat", ct);

        static long[] ParseCpuLine(string statContent)
        {
            var line = statContent.Split('\n')[0]; // "cpu  ..."
            return line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).Select(long.Parse).ToArray();
        }

        var v1 = ParseCpuLine(stat1);
        var v2 = ParseCpuLine(stat2);

        var idle1 = v1[3] + v1[4];
        var idle2 = v2[3] + v2[4];
        var total1 = v1.Sum();
        var total2 = v2.Sum();

        var totalDiff = total2 - total1;
        var idleDiff = idle2 - idle1;

        metrics.CpuUsagePercent = totalDiff == 0 ? 0 : (1.0 - (double)idleDiff / totalDiff) * 100;
    }

    private static void ReadLinuxMemory(HostMetrics metrics)
    {
        var memInfo = File.ReadAllLines("/proc/meminfo");
        long total = 0, available = 0;

        foreach (var line in memInfo)
        {
            if (line.StartsWith("MemTotal:"))
                total = ParseKb(line);
            else if (line.StartsWith("MemAvailable:"))
                available = ParseKb(line);
        }

        metrics.TotalMemoryBytes = total * 1024;
        metrics.UsedMemoryBytes = (total - available) * 1024;
        metrics.MemoryUsagePercent = total == 0 ? 0 : (double)(total - available) / total * 100;

        static long ParseKb(string line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? long.Parse(parts[1]) : 0;
        }
    }

    private static void ReadLinuxDisk(HostMetrics metrics)
    {
        var drive = new DriveInfo("/");
        metrics.TotalDiskBytes = drive.TotalSize;
        metrics.UsedDiskBytes = drive.TotalSize - drive.AvailableFreeSpace;
        metrics.DiskUsagePercent = drive.TotalSize == 0 ? 0 : (double)metrics.UsedDiskBytes / drive.TotalSize * 100;
    }

    private static async Task ReadWindowsMetricsAsync(HostMetrics metrics, CancellationToken ct)
    {
        // CPU via simple Process timing (cross-platform fallback)
        var cpuCounter = Process.GetCurrentProcess();
        var start = cpuCounter.TotalProcessorTime;
        await Task.Delay(500, ct);
        var end = cpuCounter.TotalProcessorTime;
        metrics.CpuUsagePercent = (end - start).TotalMilliseconds / (500.0 * Environment.ProcessorCount) * 100;

        // Memory
        var gcInfo = GC.GetGCMemoryInfo();
        metrics.TotalMemoryBytes = gcInfo.TotalAvailableMemoryBytes;
        metrics.UsedMemoryBytes = metrics.TotalMemoryBytes - gcInfo.TotalAvailableMemoryBytes + Process.GetCurrentProcess().WorkingSet64;
        metrics.MemoryUsagePercent = metrics.TotalMemoryBytes == 0 ? 0 : (double)metrics.UsedMemoryBytes / metrics.TotalMemoryBytes * 100;

        // Disk
        var drive = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:\\");
        metrics.TotalDiskBytes = drive.TotalSize;
        metrics.UsedDiskBytes = drive.TotalSize - drive.AvailableFreeSpace;
        metrics.DiskUsagePercent = drive.TotalSize == 0 ? 0 : (double)metrics.UsedDiskBytes / drive.TotalSize * 100;
    }
}
