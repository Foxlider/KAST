namespace KAST.Core.Models;

public enum AppUpdateReleaseSelection
{
    LatestStable,
    Tag
}

public enum AppUpdateStage
{
    Preparing,
    Downloading,
    Verifying,
    Extracting,
    Staged
}

public enum AppUpdateRestartMode
{
    None,
    DirectProcess,
    WindowsService
}

public record AppUpdateChannel(
    string Id,
    string Label,
    string Owner,
    string Repository,
    AppUpdateReleaseSelection ReleaseSelection,
    string? TagName,
    bool IsPrerelease);

public record AppUpdateAsset(
    string Name,
    long SizeBytes,
    string DownloadUrl);

public record AppUpdateCheckResult(
    AppUpdateChannel Channel,
    string CurrentVersion,
    string RuntimeIdentifier,
    bool IsNativeSupported,
    bool IsDocker,
    AppUpdateRestartMode RestartMode,
    bool IsChannelAvailable,
    bool IsUpdateAvailable,
    string Message,
    string? ReleaseTag,
    string? ReleaseName,
    string? ReleaseVersion,
    DateTimeOffset? PublishedAt,
    string? ReleaseUrl,
    AppUpdateAsset? Asset);

public record AppUpdateProgress(
    AppUpdateStage Stage,
    double? Percent,
    string Message);

public record AppUpdateDownloadResult(
    bool Success,
    string Message,
    string? StagedUpdateId,
    string? StagedPath,
    bool ChecksumVerified);

public record AppUpdateApplyResult(
    bool Success,
    string Message);
