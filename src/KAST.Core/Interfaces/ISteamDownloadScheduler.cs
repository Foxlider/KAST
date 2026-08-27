namespace KAST.Core.Interfaces;

/// <summary>
/// Limits concurrent Steam CDN requests across every content transfer.
/// </summary>
public interface ISteamDownloadScheduler
{
    /// <summary>Acquires one global Steam CDN request permit.</summary>
    ValueTask<IDisposable> AcquireAsync(CancellationToken ct = default);

    /// <summary>Updates the number of permits available to new requests.</summary>
    void SetMaximumConcurrency(int maximumConcurrency);

    int MaximumConcurrency { get; }
}
