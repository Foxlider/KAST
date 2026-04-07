using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace KAST.Web.Services;

/// <summary>
/// Bridges domain events from Infrastructure services to SignalR hubs.
/// </summary>
public class SignalREventBroadcaster(
    IHubContext<DownloadHub> downloadHub,
    IHubContext<MonitoringHub> monitoringHub) : IAppEventBroadcaster
{
    public Task BroadcastDownloadProgressAsync(ModDownloadProgressEvent progress)
        => DownloadHub.BroadcastDownloadProgress(downloadHub, progress);

    public Task BroadcastModStatusChangedAsync(ModStatusChangedEvent status)
        => DownloadHub.BroadcastModStatusChanged(downloadHub, status);

    public Task BroadcastServerStatusChangedAsync(ServerStatusChangedEvent status)
        => MonitoringHub.BroadcastServerStatus(monitoringHub, status);

    public Task BroadcastHostMetricsAsync(HostMetricsUpdatedEvent metrics)
        => MonitoringHub.BroadcastHostMetrics(monitoringHub, metrics);

    public Task BroadcastInstanceMetricsAsync(InstanceMetricsUpdatedEvent metrics)
        => MonitoringHub.BroadcastInstanceMetrics(monitoringHub, metrics);

    public Task BroadcastLogEntryAsync(LogEntryEvent logEntry)
        => MonitoringHub.BroadcastLogEntry(monitoringHub, logEntry);
}
