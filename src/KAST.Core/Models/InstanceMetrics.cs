namespace KAST.Core.Models;

public class InstanceMetrics
{
    public int ServerInstanceId { get; set; }
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageBytes { get; set; }
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
