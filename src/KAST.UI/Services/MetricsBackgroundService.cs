using KAST.Core.Events;
using KAST.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KAST.UI.Services;

/// <summary>
/// Periodically collects host and instance metrics and broadcasts them via SignalR.
/// </summary>
public class MetricsBackgroundService(
    IServiceScopeFactory scopeFactory,
    IAppEventBroadcaster broadcaster,
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

                var host = await monitoring.GetHostMetricsAsync(stoppingToken);
                await broadcaster.BroadcastHostMetricsAsync(
                    new HostMetricsUpdatedEvent(host.CpuUsagePercent, host.MemoryUsagePercent));

                var instances = await monitoring.GetAllInstanceMetricsAsync(stoppingToken);
                foreach (var inst in instances)
                {
                    await broadcaster.BroadcastInstanceMetricsAsync(
                        new InstanceMetricsUpdatedEvent(
                            inst.ServerInstanceId,
                            inst.CpuUsagePercent,
                            inst.MemoryUsageBytes,
                            inst.PlayerCount));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error collecting metrics");
            }
        }

        logger.LogInformation("Metrics background service stopped");
    }
}
