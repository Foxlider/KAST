namespace KAST.Core.Models;

public class HostMetrics
{
    public double CpuUsagePercent { get; set; }
    public long TotalMemoryBytes { get; set; }
    public long UsedMemoryBytes { get; set; }
    public double MemoryUsagePercent { get; set; }
    public long TotalDiskBytes { get; set; }
    public long UsedDiskBytes { get; set; }
    public double DiskUsagePercent { get; set; }
    public double NetworkSentBytesPerSec { get; set; }
    public double NetworkReceivedBytesPerSec { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
