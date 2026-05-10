using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
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

    private readonly IMonitoringService _monitoring;
    private readonly IServerInstanceService _serverInstances;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HealthCheckService _healthChecks;
    private CancellationTokenSource? _cts;
    private Task? _refreshTask;
    private bool _started;
    private bool _disposed;

    public MonitoringStateService(IMonitoringService monitoring, IServerInstanceService serverInstances, IServiceScopeFactory scopeFactory, HealthCheckService healthChecks)
    {
        _monitoring = monitoring;
        _serverInstances = serverInstances;
        _scopeFactory = scopeFactory;
        _healthChecks = healthChecks;
    }

    /// <summary>Starts the polling loop if not already running.</summary>
    public void EnsureStarted()
    {
        if (_started || _disposed) return;
        _started = true;
        _cts = new CancellationTokenSource();
        _refreshTask = RunLoopAsync(_cts.Token);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        await FetchAndPushAsync(ct);
        OnDataChanged?.Invoke();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (_disposed) break;
                await FetchAndPushAsync(ct);
                OnDataChanged?.Invoke();
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (ObjectDisposedException) { /* ignore */ }
    }

    private async Task FetchAndPushAsync(CancellationToken ct)
    {
        if (_disposed || ct.IsCancellationRequested) return;

        // Overall health check (drives API indicator)
        try
        {
            var health = await _healthChecks.CheckHealthAsync(ct);
            ApiStatus = health.Status;
        }
        catch { ApiStatus = HealthStatus.Unhealthy; }

        // DB connectivity check
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
            IsDbOk = await db.Database.CanConnectAsync(ct);
        }
        catch { IsDbOk = false; }

        // Metrics fetch
        try
        {
            HostMetrics = await _monitoring.GetHostMetricsAsync(ct);
            var all = await _serverInstances.GetAllInstancesAsync(ct);
            Instances = all.Where(s => s.Status != ServerInstanceStatus.Stopped).ToList();
            var allMetrics = await _monitoring.GetAllInstanceMetricsAsync(ct);
            InstanceMetrics = allMetrics.ToDictionary(m => m.ServerInstanceId);
            IsMonitoringOk = true;
        }
        catch (OperationCanceledException) { return; }
        catch (ObjectDisposedException) { return; }
        catch { IsMonitoringOk = false; return; }

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

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_cts is not null)
            await _cts.CancelAsync();
        if (_refreshTask is not null)
            try { await _refreshTask; } catch { /* ignore */ }
        _cts?.Dispose();
    }
}
