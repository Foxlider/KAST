using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace KAST.Tests;

public class CoordinatedModServiceTests
{
    [Fact]
    public async Task UpdateAll_CancellingParentStopsEveryActiveWorker()
    {
        using var provider = CreateProvider(out var steam, out var registry);
        var (first, second) = await AddPendingModsAsync(provider);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ConfigureBlockingDownload(steam, first.WorkshopId, firstStarted, firstCancelled);
        ConfigureBlockingDownload(steam, second.WorkshopId, secondStarted, secondCancelled);

        using var parentCts = new CancellationTokenSource();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IModUpdateCoordinator>();
        var updateAll = service.UpdateAllOutdatedModsAsync(parentCts.Token);
        await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(TimeSpan.FromSeconds(2));

        parentCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updateAll);
        await Task.WhenAll(firstCancelled.Task, secondCancelled.Task).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(registry.Cancel(first.Id));
        Assert.False(registry.Cancel(second.Id));
    }

    [Fact]
    public async Task UpdateAll_CancellingOneWorkerLeavesTheOtherWorkerRunning()
    {
        using var provider = CreateProvider(out var steam, out var registry);
        var (first, second) = await AddPendingModsAsync(provider);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSecondToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken secondToken = default;

        ConfigureBlockingDownload(steam, first.WorkshopId, firstStarted, firstCancelled);
        steam.DownloadWorkshopItemAsync(second.WorkshopId, Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>(), Arg.Any<int>())
            .Returns(async call =>
            {
                secondToken = call.Arg<CancellationToken>();
                secondStarted.TrySetResult();
                await allowSecondToComplete.Task;
                return 2UL;
            });

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IModUpdateCoordinator>();
        var updateAll = service.UpdateAllOutdatedModsAsync();
        await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(registry.Cancel(first.Id));
        await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(secondToken.IsCancellationRequested);

        allowSecondToComplete.TrySetResult();
        await updateAll;

        using var verificationScope = provider.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<KastDbContext>();
        Assert.Equal(ModStatus.NotInstalled, (await db.Mods.FindAsync(first.Id))!.Status);
        Assert.Equal(ModStatus.Installed, (await db.Mods.FindAsync(second.Id))!.Status);
    }

    [Fact]
    public async Task UpdateAll_UsesDedicatedModQueueLimit()
    {
        using var provider = CreateProvider(
            out var steam,
            out var registry,
            parallelDownloads: 16,
            bulkModDownloadConcurrency: 1);
        var (first, second) = await AddPendingModsAsync(provider);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ConfigureBlockingDownload(steam, first.WorkshopId, firstStarted, firstCancelled);
        steam.DownloadWorkshopItemAsync(second.WorkshopId, Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>(), 16)
            .Returns(call =>
            {
                secondStarted.TrySetResult();
                return Task.FromResult(2UL);
            });

        using var scope = provider.CreateScope();
        var updateAll = scope.ServiceProvider.GetRequiredService<IModUpdateCoordinator>().UpdateAllOutdatedModsAsync();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        await steam.DidNotReceive().DownloadWorkshopItemAsync(
            second.WorkshopId, Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>(), Arg.Any<int>());

        Assert.True(registry.Cancel(first.Id));
        await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await updateAll;
    }

    private static ServiceProvider CreateProvider(
        out ISteamService steam,
        out IModDownloadCancellationRegistry registry,
        int parallelDownloads = 2,
        int bulkModDownloadConcurrency = 2)
    {
        steam = Substitute.For<ISteamService>();
        var settings = Substitute.For<ISettingsService>();
        settings.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new KastSettings
            {
                ModsDirectory = ".KAST_DATA/mods",
                ParallelDownloads = parallelDownloads,
                BulkModDownloadConcurrency = bulkModDownloadConcurrency
            });

        var broadcaster = Substitute.For<IAppEventBroadcaster>();
        registry = new ModDownloadCancellationRegistry();
        var services = new ServiceCollection();
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<KastDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        services.AddSingleton(steam);
        services.AddSingleton(settings);
        services.AddSingleton(broadcaster);
        services.AddSingleton<IOutputSanitizer, OutputSanitizer>();
        services.AddSingleton(registry);
        services.AddSingleton<ILogger<ModService>>(Substitute.For<ILogger<ModService>>());
        services.AddSingleton<ILogger<CoordinatedModService>>(Substitute.For<ILogger<CoordinatedModService>>());
        services.AddScoped<IModService, ModService>();
        services.AddScoped<IModUpdateCoordinator, CoordinatedModService>();
        return services.BuildServiceProvider();
    }

    private static async Task<(SteamMod First, SteamMod Second)> AddPendingModsAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var first = new SteamMod { Name = "First", WorkshopId = 101, Status = ModStatus.NotInstalled };
        var second = new SteamMod { Name = "Second", WorkshopId = 102, Status = ModStatus.NotInstalled };
        db.Mods.AddRange(first, second);
        await db.SaveChangesAsync();
        return (first, second);
    }

    private static void ConfigureBlockingDownload(
        ISteamService steam,
        long workshopId,
        TaskCompletionSource started,
        TaskCompletionSource cancelled)
    {
        steam.DownloadWorkshopItemAsync(workshopId, Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>(), Arg.Any<int>())
            .Returns(async call =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    throw;
                }
                return 0UL;
            });
    }
}
