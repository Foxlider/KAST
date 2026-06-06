namespace KAST.Core.Models;

public enum HostServiceStartupMode
{
    Automatic,
    Manual,
    Disabled
}

public enum HostServiceRunState
{
    Unsupported,
    NotInstalled,
    Stopped,
    StartPending,
    Running,
    StopPending,
    Paused,
    Unknown
}

public record HostServiceRecoveryOptions(
    bool RestartOnCrash,
    int RestartDelaySeconds,
    int ResetFailureCountAfterDays);

public record HostServiceStatus(
    bool IsSupported,
    bool IsAdministrator,
    string ServiceName,
    string DisplayName,
    HostServiceRunState State,
    HostServiceStartupMode StartupMode,
    HostServiceRecoveryOptions Recovery,
    string? ExecutablePath,
    string? Message);

public record HostServiceConfigureRequest(
    HostServiceStartupMode StartupMode,
    HostServiceRecoveryOptions Recovery);

public record HostServiceOperationResult(
    bool Success,
    string Message,
    HostServiceStatus? Status = null);

public record CrashReportSummary(
    string Id,
    DateTimeOffset CreatedAt,
    string Kind,
    int ProcessId,
    string Message,
    string Path);
