using KAST.Core.Models;

namespace KAST.Core.Events;

public record ModDownloadProgressEvent(
    int ModId,
    long WorkshopId,
    double ProgressPercent,
    long BytesDownloaded,
    long TotalBytes,
    IReadOnlyList<DownloadFileProgress>? Files = null);
public record ModStatusChangedEvent(int ModId, string NewStatus);
public record ServerStatusChangedEvent(int ServerInstanceId, string NewStatus);
public record HostMetricsUpdatedEvent(double CpuPercent, double MemoryPercent);
public record InstanceMetricsUpdatedEvent(int ServerInstanceId, double CpuPercent, long MemoryBytes, int PlayerCount);
public record LogEntryEvent(int ServerInstanceId, string Line, DateTime Timestamp);

public enum ServerRuntimeEventSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public enum ServerRuntimeEventKind
{
    SteamInitialized,
    SteamConnected,
    SteamQueryOverflow,
    AdminLogin,
    AdminLogout,
    MissionStarted,
    MissionHeaderMissing,
    MissingDownloadableContent,
    Warning,
    Error
}

public record ServerRuntimeEvent(
    int ServerInstanceId,
    DateTime Timestamp,
    ServerRuntimeEventSeverity Severity,
    ServerRuntimeEventKind Kind,
    string Title,
    string Message,
    string? SourceLine = null,
    string? MissionFile = null,
    string? MissionWorld = null,
    string? MissionDirectory = null,
    string? PlayerName = null,
    string? PlayerUid = null,
    string? PlayerIp = null,
    int? GamePort = null,
    int? SteamQueryPort = null);
