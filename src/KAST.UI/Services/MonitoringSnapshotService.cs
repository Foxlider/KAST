using KAST.Core.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KAST.UI.Services;

public sealed class MonitoringSnapshotService
{
    private readonly object _lock = new();
    private MonitoringSnapshot _snapshot = MonitoringSnapshot.Empty;

    public event Action? Changed;

    public MonitoringSnapshot Current
    {
        get
        {
            lock (_lock)
                return _snapshot;
        }
    }

    public void Update(MonitoringSnapshot snapshot)
    {
        lock (_lock)
            _snapshot = snapshot;

        Changed?.Invoke();
    }

    public void UpdateExternalProcesses(IReadOnlyList<RunningProcessInfo> processes)
    {
        lock (_lock)
            _snapshot = _snapshot with { ExternalProcesses = processes };

        Changed?.Invoke();
    }
}

public sealed record MonitoringSnapshot(
    HostMetrics HostMetrics,
    IReadOnlyList<ServerInstance> Instances,
    IReadOnlyList<InstanceMetrics> InstanceMetrics,
    bool IsMonitoringOk,
    bool IsDbOk,
    HealthStatus ApiStatus,
    IReadOnlyList<RunningProcessInfo>? ExternalProcesses = null)
{
    public static MonitoringSnapshot Empty { get; } = new(
        new HostMetrics(),
        [],
        [],
        IsMonitoringOk: true,
        IsDbOk: true,
        HealthStatus.Healthy);
}
