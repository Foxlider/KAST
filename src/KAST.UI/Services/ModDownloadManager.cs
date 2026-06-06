using KAST.Core.Interfaces;

namespace KAST.UI.Services;

public sealed class ModDownloadManager(IModDownloadQueueService queue)
{
    public int ActiveCount => queue.ActiveCount;

    public bool IsActive(int modId) => queue.IsActive(modId);

    public Task<int> StartAllOutdatedAsync(CancellationToken ct = default)
        => queue.QueueAllOutdatedAsync(ct);

    public async Task<bool> StartDownloadAsync(int modId, bool isUpdate, CancellationToken ct = default)
        => await queue.QueueDownloadAsync(modId, isUpdate, ct) is not null;

    public bool StartDownload(int modId, bool isUpdate)
        => queue.QueueDownloadAsync(modId, isUpdate).GetAwaiter().GetResult() is not null;

    public bool Cancel(int modId)
        => queue.CancelAsync(modId).GetAwaiter().GetResult();

    public void CancelAll()
        => queue.CancelAllAsync().GetAwaiter().GetResult();
}
