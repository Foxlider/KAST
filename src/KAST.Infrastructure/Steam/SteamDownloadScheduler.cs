using KAST.Core.Interfaces;
using KAST.Core.Models;

namespace KAST.Infrastructure.Steam;

/// <summary>
/// Fair, application-wide limiter for Steam CDN requests.
/// </summary>
public sealed class SteamDownloadScheduler : ISteamDownloadScheduler
{
    private readonly object _sync = new();
    private readonly Queue<Waiter> _waiters = new();
    private int _activeRequests;
    private int _maximumConcurrency = DownloadConcurrency.DefaultSteamWorkers;

    public int MaximumConcurrency
    {
        get
        {
            lock (_sync)
                return _maximumConcurrency;
        }
    }

    public ValueTask<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_activeRequests < _maximumConcurrency)
            {
                _activeRequests++;
                return ValueTask.FromResult<IDisposable>(new Permit(this));
            }

            var waiter = new Waiter(ct);
            _waiters.Enqueue(waiter);
            return new ValueTask<IDisposable>(AwaitPermitAsync(waiter));
        }
    }

    private static async Task<IDisposable> AwaitPermitAsync(Waiter waiter)
    {
        try
        {
            return await waiter.Task.ConfigureAwait(false);
        }
        finally
        {
            waiter.DisposeCancellationRegistration();
        }
    }

    public void SetMaximumConcurrency(int maximumConcurrency)
    {
        lock (_sync)
        {
            _maximumConcurrency = Math.Clamp(
                maximumConcurrency,
                DownloadConcurrency.MinimumSteamWorkers,
                DownloadConcurrency.MaximumSteamWorkers);
            GrantWaitingRequests();
        }
    }

    private void Release()
    {
        lock (_sync)
        {
            _activeRequests--;
            GrantWaitingRequests();
        }
    }

    private void GrantWaitingRequests()
    {
        while (_activeRequests < _maximumConcurrency && _waiters.TryDequeue(out var waiter))
        {
            if (!waiter.TryGrant(this))
                continue;

            _activeRequests++;
        }
    }

    private sealed class Permit(SteamDownloadScheduler scheduler) : IDisposable
    {
        private SteamDownloadScheduler? _scheduler = scheduler;

        public void Dispose()
        {
            Interlocked.Exchange(ref _scheduler, null)?.Release();
        }
    }

    private sealed class Waiter
    {
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly TaskCompletionSource<IDisposable> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Waiter(CancellationToken ct)
        {
            if (ct.CanBeCanceled)
                _cancellationRegistration = ct.Register(static state => ((Waiter)state!).Cancel(), this);
        }

        public Task<IDisposable> Task => _completion.Task;

        public bool TryGrant(SteamDownloadScheduler scheduler)
        {
            if (!_completion.TrySetResult(new Permit(scheduler)))
                return false;

            return true;
        }

        private void Cancel()
        {
            _completion.TrySetCanceled();
        }

        public void DisposeCancellationRegistration()
        {
            _cancellationRegistration.Dispose();
        }
    }
}
