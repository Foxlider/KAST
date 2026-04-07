namespace KAST.Core.Events;

public record ModDownloadProgressEvent(int ModId, long WorkshopId, double ProgressPercent, long BytesDownloaded, long TotalBytes);
public record ModStatusChangedEvent(int ModId, string NewStatus);
public record ServerStatusChangedEvent(int ServerInstanceId, string NewStatus);
public record HostMetricsUpdatedEvent(double CpuPercent, double MemoryPercent);
public record InstanceMetricsUpdatedEvent(int ServerInstanceId, double CpuPercent, long MemoryBytes, int PlayerCount);
public record LogEntryEvent(int ServerInstanceId, string Line, DateTime Timestamp);
