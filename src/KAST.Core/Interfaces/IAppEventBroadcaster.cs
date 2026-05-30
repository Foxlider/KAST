using KAST.Core.Events;

namespace KAST.Core.Interfaces;

/// <summary>
/// Abstracts real-time event broadcasting so Infrastructure services
/// can push events without referencing SignalR directly.
/// Implemented by the Web layer using IHubContext.
/// </summary>
public interface IAppEventBroadcaster
{
    Task BroadcastDownloadProgressAsync(ModDownloadProgressEvent progress);
    Task BroadcastModStatusChangedAsync(ModStatusChangedEvent status);
    Task BroadcastServerStatusChangedAsync(ServerStatusChangedEvent status);
    Task BroadcastHostMetricsAsync(HostMetricsUpdatedEvent metrics);
    Task BroadcastInstanceMetricsAsync(InstanceMetricsUpdatedEvent metrics);
    Task BroadcastLogEntryAsync(LogEntryEvent logEntry);

    /// <summary>In-process subscriptions for Blazor Server components (no HTTP needed).</summary>
    event Action<ModDownloadProgressEvent>? OnModDownloadProgress;
    event Action<ModStatusChangedEvent>? OnModStatusChanged;
    event Action<ServerStatusChangedEvent>? OnServerStatusChanged;
}
