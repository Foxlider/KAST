using System.Collections.Concurrent;
using KAST.Core.Interfaces;

namespace KAST.UI.Services;

public sealed class ModDownloadManager(
    IServiceScopeFactory scopeFactory,
    ILogger<ModDownloadManager> logger)
{
    private readonly ConcurrentDictionary<int, DownloadOperation> _downloads = new();

    public int ActiveCount => _downloads.Count;

    public bool IsActive(int modId) => _downloads.ContainsKey(modId);

    public bool StartDownload(int modId, bool isUpdate)
    {
        var cts = new CancellationTokenSource();
        var operation = new DownloadOperation(cts);

        if (!_downloads.TryAdd(modId, operation))
        {
            cts.Dispose();
            return false;
        }

        _ = Task.Run(() => RunDownloadAsync(modId, isUpdate, cts.Token));
        return true;
    }

    public bool Cancel(int modId)
    {
        if (!_downloads.TryGetValue(modId, out var operation))
            return false;

        operation.Cancellation.Cancel();
        return true;
    }

    public void CancelAll()
    {
        foreach (var operation in _downloads.Values)
            operation.Cancellation.Cancel();
    }

    private async Task RunDownloadAsync(int modId, bool isUpdate, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var modService = scope.ServiceProvider.GetRequiredService<IModService>();

            if (isUpdate)
                await modService.UpdateModFilesAsync(modId, progress: null, ct);
            else
                await modService.DownloadModAsync(modId, progress: null, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Mod download {ModId} was cancelled.", modId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Mod download {ModId} failed.", modId);
        }
        finally
        {
            if (_downloads.TryRemove(modId, out var operation))
                operation.Dispose();
        }
    }

    private sealed class DownloadOperation(CancellationTokenSource cancellation) : IDisposable
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public void Dispose() => Cancellation.Dispose();
    }
}
