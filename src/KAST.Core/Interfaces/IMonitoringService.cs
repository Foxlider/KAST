using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IMonitoringService
{
    Task<HostMetrics> GetHostMetricsAsync(CancellationToken ct = default);
    Task<InstanceMetrics?> GetInstanceMetricsAsync(int serverInstanceId, CancellationToken ct = default);
    Task<IReadOnlyList<InstanceMetrics>> GetAllInstanceMetricsAsync(CancellationToken ct = default);
}
