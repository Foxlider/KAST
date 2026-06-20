using KAST.Core.Events;
using Microsoft.AspNetCore.SignalR;

namespace KAST.UI.Hubs;

public class MonitoringHub : Hub
{
    public async Task SubscribeToHost()
        => await Groups.AddToGroupAsync(Context.ConnectionId, "host-metrics");

    public async Task UnsubscribeFromHost()
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, "host-metrics");

    public async Task SubscribeToInstance(int instanceId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"instance-{instanceId}");

    public async Task UnsubscribeFromInstance(int instanceId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"instance-{instanceId}");

    public async Task SubscribeToExternalProcesses()
        => await Groups.AddToGroupAsync(Context.ConnectionId, "external-processes");

    public async Task UnsubscribeFromExternalProcesses()
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, "external-processes");

    public static async Task BroadcastHostMetrics(IHubContext<MonitoringHub> hubContext, HostMetricsUpdatedEvent metrics)
        => await hubContext.Clients.Group("host-metrics").SendAsync("HostMetricsUpdated", metrics);

    public static async Task BroadcastInstanceMetrics(IHubContext<MonitoringHub> hubContext, InstanceMetricsUpdatedEvent metrics)
        => await hubContext.Clients.Group($"instance-{metrics.ServerInstanceId}").SendAsync("InstanceMetricsUpdated", metrics);

    public static async Task BroadcastServerStatus(IHubContext<MonitoringHub> hubContext, ServerStatusChangedEvent status)
        => await hubContext.Clients.All.SendAsync("ServerStatusChanged", status);

    public static async Task BroadcastLogEntry(IHubContext<MonitoringHub> hubContext, LogEntryEvent logEntry)
        => await hubContext.Clients.Group($"instance-{logEntry.ServerInstanceId}").SendAsync("LogEntry", logEntry);

    public static async Task BroadcastServerRuntimeEvent(IHubContext<MonitoringHub> hubContext, ServerRuntimeEvent runtimeEvent)
        => await hubContext.Clients.Group($"instance-{runtimeEvent.ServerInstanceId}").SendAsync("ServerRuntimeEvent", runtimeEvent);

    public static async Task BroadcastExternalProcesses(IHubContext<MonitoringHub> hubContext, ExternalProcessesUpdatedEvent processes)
        => await hubContext.Clients.Group("external-processes").SendAsync("ExternalProcessesUpdated", processes);
}
