using KAST.Core.Enums;
using KAST.Core.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KAST.UI.Services;

/// <summary>
/// Circuit-scoped service that accumulates monitoring history across page navigations.
/// Call <see cref="EnsureStarted"/> once to begin polling; subscribe to
/// <see cref="OnDataChanged"/> for UI update notifications.
/// </summary>
public sealed class MonitoringStateService : IAsyncDisposable
{
    public const int MaxPoints = 1_440;

    // ── Latest snapshot ──────────────────────────────────────────────────────
    public HostMetrics HostMetrics { get; private set; } = new();
    public IReadOnlyList<ServerInstance> Instances { get; private set; } = [];
    public Dictionary<int, InstanceMetrics> InstanceMetrics { get; private set; } = [];
    public bool IsMonitoringOk { get; private set; } = true;
    public bool IsDbOk { get; private set; } = true;
    public HealthStatus ApiStatus { get; private set; } = HealthStatus.Healthy;

    // ── History buffers ──────────────────────────────────────────────────────
    private readonly List<string> _hostLabels = new(MaxPoints);
    public IReadOnlyList<string> HostLabels => _hostLabels;
    private readonly List<double> _hostCpu = new(MaxPoints);
    public IReadOnlyList<double> HostCpu => _hostCpu;
    private readonly List<double> _hostMem = new(MaxPoints);
    public IReadOnlyList<double> HostMem => _hostMem;
    private readonly Dictionary<int, (List<double> Cpu, List<double> Mem)> _instanceHistory = [];
    public IReadOnlyDictionary<int, (List<double> Cpu, List<double> Mem)> InstanceHistory => _instanceHistory;

    // ── Notification ─────────────────────────────────────────────────────────
    public event Action? OnDataChanged;

    private readonly MonitoringSnapshotService _snapshots;
    private bool _started;
    private bool _disposed;

    public MonitoringStateService(MonitoringSnapshotService snapshots)
    {
        _snapshots = snapshots;
    }

    /// <summary>Starts the polling loop if not already running.</summary>
    public void EnsureStarted()
    {
        if (_started || _disposed) return;
        _started = true;
        _snapshots.Changed += HandleSnapshotChanged;
        ApplySnapshot(_snapshots.Current);
        OnDataChanged?.Invoke();
    }

    private void HandleSnapshotChanged()
    {
        if (_disposed) return;
        ApplySnapshot(_snapshots.Current);
        OnDataChanged?.Invoke();
    }

    private void ApplySnapshot(MonitoringSnapshot snapshot)
    {
        if (_disposed) return;

        HostMetrics = snapshot.HostMetrics;
        Instances = snapshot.Instances;
        InstanceMetrics = snapshot.InstanceMetrics.ToDictionary(m => m.ServerInstanceId);
        IsMonitoringOk = snapshot.IsMonitoringOk;
        IsDbOk = snapshot.IsDbOk;
        ApiStatus = snapshot.ApiStatus;

        // Append to history
        var label = DateTime.Now.ToString("HH:mm:ss");
        if (HostLabels.Count >= MaxPoints) { _hostLabels.RemoveAt(0); _hostCpu.RemoveAt(0); _hostMem.RemoveAt(0); }
        _hostLabels.Add(label);
        _hostCpu.Add(Math.Round(HostMetrics.CpuUsagePercent, 1));
        _hostMem.Add(Math.Round(HostMetrics.MemoryUsagePercent, 1));

        foreach (var (id, metrics) in InstanceMetrics)
        {
            if (!InstanceHistory.TryGetValue(id, out var hist))
            {
                hist = (new List<double>(MaxPoints), new List<double>(MaxPoints));
                _instanceHistory[id] = hist;
            }
            if (hist.Cpu.Count >= MaxPoints) { hist.Cpu.RemoveAt(0); hist.Mem.RemoveAt(0); }
            hist.Cpu.Add(Math.Round(metrics.CpuUsagePercent, 1));
            hist.Mem.Add(Math.Round(metrics.MemoryUsageBytes / 1_048_576.0, 1));
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_started)
            _snapshots.Changed -= HandleSnapshotChanged;
        return ValueTask.CompletedTask;
    }
}
