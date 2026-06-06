using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IModDownloadQueueService
{
    int ActiveCount { get; }
    bool IsActive(int modId);
    Task<DownloadTask?> QueueDownloadAsync(int modId, bool isUpdate, CancellationToken ct = default);
    Task<int> QueueAllOutdatedAsync(CancellationToken ct = default);
    Task<bool> CancelAsync(int modId, CancellationToken ct = default);
    Task<int> CancelAllAsync(CancellationToken ct = default);
    Task<DownloadQueueSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

public sealed record DownloadQueueSnapshot(
    int ActiveCount,
    int QueuedCount,
    int CompletedCount,
    int FailedCount,
    int CancelledCount,
    IReadOnlyList<DownloadTask> Tasks);
