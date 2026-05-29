using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KAST.UI.Services;

/// <summary>
/// Periodically collects host and instance metrics and broadcasts them via SignalR.
/// </summary>
public class MetricsBackgroundService(
    IServiceScopeFactory scopeFactory,
    IAppEventBroadcaster broadcaster,
    MonitoringSnapshotService snapshots,
    ILogger<MetricsBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Metrics background service started (interval: {Interval}s)", Interval.TotalSeconds);

        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var monitoring = scope.ServiceProvider.GetRequiredService<IMonitoringService>();
                var serverInstances = scope.ServiceProvider.GetRequiredService<IServerInstanceService>();

                var apiStatus = await GetApiStatusAsync(scope.ServiceProvider, stoppingToken);
                var dbOk = await CanConnectToDatabaseAsync(scope.ServiceProvider, stoppingToken);
                var host = new HostMetrics();
                IReadOnlyList<ServerInstance> instances = [];
                IReadOnlyList<InstanceMetrics> instanceMetrics = [];
                var monitoringOk = true;

                try
                {
                    host = await monitoring.GetHostMetricsAsync(stoppingToken);
                    instances = (await serverInstances.GetAllInstancesAsync(stoppingToken))
                        .Where(s => s.Status != ServerInstanceStatus.Stopped)
                        .ToList();
                    instanceMetrics = await monitoring.GetAllInstanceMetricsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    monitoringOk = false;
                    logger.LogWarning(ex, "Error collecting metrics");
                }

                snapshots.Update(new MonitoringSnapshot(
                    host,
                    instances,
                    instanceMetrics,
                    monitoringOk,
                    dbOk,
                    apiStatus));

                if (monitoringOk)
                {
                    await broadcaster.BroadcastHostMetricsAsync(
                        new HostMetricsUpdatedEvent(host.CpuUsagePercent, host.MemoryUsagePercent));

                    foreach (var inst in instanceMetrics)
                    {
                        await broadcaster.BroadcastInstanceMetricsAsync(
                            new InstanceMetricsUpdatedEvent(
                                inst.ServerInstanceId,
                                inst.CpuUsagePercent,
                                inst.MemoryUsageBytes,
                                inst.PlayerCount));
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            { break; }
            catch (Exception ex)
            { logger.LogWarning(ex, "Error collecting metrics"); }
        }

        logger.LogInformation("Metrics background service stopped");
    }

    private static async Task<HealthStatus> GetApiStatusAsync(IServiceProvider services, CancellationToken ct)
    {
        try
        {
            var healthChecks = services.GetRequiredService<HealthCheckService>();
            var health = await healthChecks.CheckHealthAsync(ct);
            return health.Status;
        }
        catch
        {
            return HealthStatus.Unhealthy;
        }
    }

    private static async Task<bool> CanConnectToDatabaseAsync(IServiceProvider services, CancellationToken ct)
    {
        try
        {
            var db = services.GetRequiredService<KastDbContext>();
            return await db.Database.CanConnectAsync(ct);
        }
        catch
        {
            return false;
        }
    }
}
