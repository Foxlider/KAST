namespace KAST.Core.Interfaces;

/// <summary>
/// Tracks active bulk-update workers so one mod can be cancelled without
/// cancelling unrelated mod downloads.
/// </summary>
public interface IModDownloadCancellationRegistry
{
    /// <summary>Registers a mod's private cancellation source for its active operation.</summary>
    IDisposable Register(int modId, CancellationTokenSource cancellationSource);

    /// <summary>Cancels only the active operation for the specified mod.</summary>
    bool Cancel(int modId);

    /// <summary>Marks a queued bulk operation for cancellation if it is not active yet.</summary>
    bool CancelOrMarkPending(int modId);

    /// <summary>Clears a cancellation mark for a bulk operation that never registered.</summary>
    void ClearPendingCancellation(int modId);
}
