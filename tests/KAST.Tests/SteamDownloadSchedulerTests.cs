using KAST.Infrastructure.Steam;

namespace KAST.Tests;

public class SteamDownloadSchedulerTests
{
    [Fact]
    public async Task AcquireAsync_EnforcesOneLimitAcrossAllCallers()
    {
        var scheduler = new SteamDownloadScheduler();
        scheduler.SetMaximumConcurrency(2);
        using var first = await scheduler.AcquireAsync();
        using var second = await scheduler.AcquireAsync();

        var thirdRequest = scheduler.AcquireAsync().AsTask();
        Assert.False(thirdRequest.IsCompleted);

        first.Dispose();
        using var third = await thirdRequest.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SetMaximumConcurrency_LowersCapacityWithoutInterruptingActiveRequests()
    {
        var scheduler = new SteamDownloadScheduler();
        scheduler.SetMaximumConcurrency(2);
        using var first = await scheduler.AcquireAsync();
        using var second = await scheduler.AcquireAsync();
        scheduler.SetMaximumConcurrency(1);

        var thirdRequest = scheduler.AcquireAsync().AsTask();
        first.Dispose();
        Assert.False(thirdRequest.IsCompleted);

        second.Dispose();
        using var third = await thirdRequest.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AcquireAsync_CancellationRemovesWaitingRequestFromFutureCapacity()
    {
        var scheduler = new SteamDownloadScheduler();
        scheduler.SetMaximumConcurrency(1);
        using var first = await scheduler.AcquireAsync();
        using var cancellation = new CancellationTokenSource();
        var cancelledRequest = scheduler.AcquireAsync(cancellation.Token).AsTask();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledRequest);

        first.Dispose();
        using var next = await scheduler.AcquireAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
    }
}
