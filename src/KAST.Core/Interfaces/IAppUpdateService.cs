using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IAppUpdateService
{
    IReadOnlyList<AppUpdateChannel> GetChannels();
    Task<AppUpdateCheckResult> CheckForUpdatesAsync(string channelId, CancellationToken ct = default);
    Task<AppUpdateDownloadResult> DownloadUpdateAsync(
        string channelId,
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken ct = default);
    Task<AppUpdateApplyResult> ApplyStagedUpdateAsync(string stagedUpdateId, CancellationToken ct = default);
}
