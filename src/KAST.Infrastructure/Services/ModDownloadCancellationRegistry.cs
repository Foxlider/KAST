using System.Collections.Concurrent;
using KAST.Core.Interfaces;

namespace KAST.Infrastructure.Services;

/// <summary>
/// Application-wide registry for individually cancellable bulk-mod workers.
/// </summary>
public sealed class ModDownloadCancellationRegistry : IModDownloadCancellationRegistry
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _active = new();
    private readonly ConcurrentDictionary<int, byte> _pendingCancellations = new();

    public IDisposable Register(int modId, CancellationTokenSource cancellationSource)
    {
        ArgumentNullException.ThrowIfNull(cancellationSource);

        if (!_active.TryAdd(modId, cancellationSource))
            throw new InvalidOperationException($"A download is already active for mod {modId}.");

        if (_pendingCancellations.TryRemove(modId, out _))
            cancellationSource.Cancel();

        return new Registration(_active, modId, cancellationSource);
    }

    public bool Cancel(int modId)
    {
        if (!_active.TryGetValue(modId, out var cancellationSource))
            return false;

        cancellationSource.Cancel();
        return true;
    }

    public bool CancelOrMarkPending(int modId)
        => Cancel(modId) || _pendingCancellations.TryAdd(modId, 0);

    public void ClearPendingCancellation(int modId)
        => _pendingCancellations.TryRemove(modId, out _);

    private sealed class Registration(
        ConcurrentDictionary<int, CancellationTokenSource> active,
        int modId,
        CancellationTokenSource cancellationSource) : IDisposable
    {
        public void Dispose()
        {
            ((ICollection<KeyValuePair<int, CancellationTokenSource>>)active)
                .Remove(new KeyValuePair<int, CancellationTokenSource>(modId, cancellationSource));
        }
    }
}
