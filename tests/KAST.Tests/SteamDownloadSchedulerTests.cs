using KAST.Infrastructure.Steam;

namespace KAST.Tests;

public class SteamDownloadSchedulerTests
{
    private static async Task<IDisposable> AcquirePermitAsync(
        SteamDownloadScheduler scheduler,
        CancellationToken cancellationToken = default) =>
        await scheduler.AcquireAsync(cancellationToken);

    [Fact]
    public async Task AcquireAsync_EnforcesOneLimitAcrossAllCallers()
    {
        var scheduler = new SteamDownloadScheduler();
        scheduler.SetMaximumConcurrency(2);
        IDisposable? first = await scheduler.AcquireAsync();
        using var second = await scheduler.AcquireAsync();

        try
        {
            var thirdRequest = scheduler.AcquireAsync().AsTask();
            Assert.False(thirdRequest.IsCompleted);

            first.Dispose();
            first = null;
            using var third = await thirdRequest.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            first?.Dispose();
        }
    }

    [Fact]
    public async Task SetMaximumConcurrency_LowersCapacityWithoutInterruptingActiveRequests()
    {
        var scheduler = new SteamDownloadScheduler();
        scheduler.SetMaximumConcurrency(2);
        IDisposable? first = await scheduler.AcquireAsync();
        IDisposable? second = await scheduler.AcquireAsync();

        try
        {
            scheduler.SetMaximumConcurrency(1);
            var thirdRequest = scheduler.AcquireAsync().AsTask();
            first.Dispose();
            first = null;
            Assert.False(thirdRequest.IsCompleted);

            second.Dispose();
            second = null;
            using var third = await thirdRequest.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            first?.Dispose();
            second?.Dispose();
        }
    }

    [Fact]
    public async Task AcquireAsync_CancellationRemovesWaitingRequestFromFutureCapacity()
    {
        var scheduler = new SteamDownloadScheduler();
        scheduler.SetMaximumConcurrency(1);
        IDisposable? first = await scheduler.AcquireAsync();
        using var cancellation = new CancellationTokenSource();
        var cancelledRequest = AcquirePermitAsync(scheduler, cancellation.Token);

        try
        {
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await cancelledRequest);

            first.Dispose();
            first = null;
            using var next = await scheduler.AcquireAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            first?.Dispose();
        }
    }
}
