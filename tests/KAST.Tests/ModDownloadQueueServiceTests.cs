using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Services;
using KAST.Infrastructure.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KAST.Tests;

public class ModDownloadQueueServiceTests
{
    [Fact]
    public async Task QueueAllOutdatedAsync_WithLargeBatch_RespectsParallelModDownloadLimit()
    {
        var dbName = Guid.NewGuid().ToString();
        var orchestrator = new CountingOrchestrator(delay: TimeSpan.FromMilliseconds(25));
        var (provider, service) = BuildService(dbName, orchestrator, parallelModDownloads: 2);
        await SeedModsAsync(provider, 150);

        var queued = await service.QueueAllOutdatedAsync();
        await service.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(async () =>
            {
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
                return await db.DownloadTasks.CountAsync(t =>
                    t.Status == DownloadStatus.Completed ||
                    t.Status == DownloadStatus.Failed ||
                    t.Status == DownloadStatus.Cancelled) == 150;
            });
        }
        catch (Exception ex) when (ex is TimeoutException or TaskCanceledException)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
            var queuedCount = await db.DownloadTasks.CountAsync(t => t.Status == DownloadStatus.Queued);
            var runningCount = await db.DownloadTasks.CountAsync(t => t.Status == DownloadStatus.Downloading);
            var completedCount = await db.DownloadTasks.CountAsync(t => t.Status == DownloadStatus.Completed);
            var failedCount = await db.DownloadTasks.CountAsync(t => t.Status == DownloadStatus.Failed);
            throw new TimeoutException(
                $"Queue did not drain. queued={queuedCount}, running={runningCount}, completed={completedCount}, failed={failedCount}, started={orchestrator.StartedCount}, finished={orchestrator.CompletedCount}, active={service.ActiveCount}",
                ex);
        }

        await service.StopAsync(CancellationToken.None);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<KastDbContext>();
        var completed = await verifyDb.DownloadTasks.CountAsync(t => t.Status == DownloadStatus.Completed);
        var failed = await verifyDb.DownloadTasks.CountAsync(t => t.Status == DownloadStatus.Failed);

        Assert.Equal(150, queued);
        Assert.Equal(150, completed);
        Assert.Equal(0, failed);
        Assert.True(orchestrator.MaxObserved <= 2, $"Observed {orchestrator.MaxObserved} concurrent downloads.");
        Assert.Equal(2, orchestrator.MaxParallelModDownloadsRequested);
    }

    [Fact]
    public async Task Startup_RequeuesInterruptedDownloadTasks()
    {
        var dbName = Guid.NewGuid().ToString();
        var orchestrator = new CountingOrchestrator(delay: TimeSpan.FromMilliseconds(200));
        var (provider, service) = BuildService(dbName, orchestrator, parallelModDownloads: 1);
        await SeedModsAsync(provider, 1, ModStatus.Downloading);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
            db.DownloadTasks.Add(new DownloadTask
            {
                ModId = 1,
                WorkshopId = 1001,
                Name = "Mod 1",
                Status = DownloadStatus.Downloading,
                MaxRetries = 3,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await service.StartAsync(CancellationToken.None);

        await WaitUntilAsync(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
            var task = await db.DownloadTasks.AsNoTracking().FirstAsync();
            return task.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Completed;
        });

        await service.StopAsync(CancellationToken.None);
    }

    private static (ServiceProvider Provider, ModDownloadQueueService Service) BuildService(
        string dbName,
        IContentOrchestrator orchestrator,
        int parallelModDownloads)
    {
        var services = new ServiceCollection();
        services.AddDbContext<KastDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddSingleton<ISettingsService>(BuildSettingsService(parallelModDownloads));
        services.AddSingleton(Substitute.For<IModService>());
        services.AddSingleton(Substitute.For<IFileSystemService>());
        services.AddSingleton(BuildBroadcaster());
        services.AddSingleton<ContentProgressTracker>();
        services.AddSingleton(orchestrator);
        services.AddSingleton<ILogger<ModDownloadQueueService>>(NullLogger<ModDownloadQueueService>.Instance);
        services.AddSingleton<ModDownloadQueueService>();
        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<ModDownloadQueueService>());
    }

    private static ISettingsService BuildSettingsService(int parallelModDownloads)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new KastSettings
            {
                ModsDirectory = "mods",
                ParallelDownloads = 1,
                ParallelModDownloads = parallelModDownloads
            });
        return settings;
    }

    private static IAppEventBroadcaster BuildBroadcaster()
    {
        var broadcaster = Substitute.For<IAppEventBroadcaster>();
        broadcaster.BroadcastModStatusChangedAsync(Arg.Any<ModStatusChangedEvent>())
            .Returns(Task.CompletedTask);
        broadcaster.BroadcastDownloadProgressAsync(Arg.Any<ModDownloadProgressEvent>())
            .Returns(Task.CompletedTask);
        broadcaster.BroadcastServerStatusChangedAsync(Arg.Any<ServerStatusChangedEvent>())
            .Returns(Task.CompletedTask);
        broadcaster.BroadcastHostMetricsAsync(Arg.Any<HostMetricsUpdatedEvent>())
            .Returns(Task.CompletedTask);
        broadcaster.BroadcastInstanceMetricsAsync(Arg.Any<InstanceMetricsUpdatedEvent>())
            .Returns(Task.CompletedTask);
        broadcaster.BroadcastLogEntryAsync(Arg.Any<LogEntryEvent>())
            .Returns(Task.CompletedTask);
        return broadcaster;
    }

    private static async Task SeedModsAsync(ServiceProvider provider, int count, ModStatus status = ModStatus.NotInstalled)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        for (var i = 1; i <= count; i++)
        {
            db.Mods.Add(new SteamMod
            {
                Id = i,
                WorkshopId = 1000 + i,
                Name = $"Mod {i}",
                Source = ModSource.SteamWorkshop,
                Status = status,
                ExpectedSizeBytes = 1024
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            if (await condition())
                return;
            await Task.Delay(25, timeout.Token);
        }

        throw new TimeoutException("Condition was not met.");
    }

    private sealed class CountingOrchestrator(TimeSpan delay) : IContentOrchestrator
    {
        private int _active;
        private int _startedCount;
        private int _completedCount;
        public int MaxObserved { get; private set; }
        public int MaxParallelModDownloadsRequested { get; private set; }
        public int StartedCount => _startedCount;
        public int CompletedCount => _completedCount;

        public bool IsRunning(string key) => false;

        public ContentInstallState StartServerInstall(int instanceId, string installPath, ServerInstance instance, int maxParallelDownloads)
            => throw new NotSupportedException();

        public ContentInstallState StartModInstall(int modId, ContentType type, string destinationPath, long workshopId = 0, string? sourcePath = null, long expectedSizeBytes = 0, Func<IServiceProvider, ContentInstallState, Task>? onStarted = null, Func<IServiceProvider, ContentInstallState, Task>? onComplete = null, Func<IServiceProvider, ContentInstallState, Exception, Task>? onError = null, int maxParallelDownloads = 4, int maxParallelModDownloads = 1)
            => throw new NotSupportedException();

        public async Task<ContentInstallState> RunModInstallAsync(int modId, ContentType type, string destinationPath, long workshopId = 0, string? sourcePath = null, long expectedSizeBytes = 0, Func<IServiceProvider, ContentInstallState, Task>? onStarted = null, Func<IServiceProvider, ContentInstallState, Task>? onComplete = null, Func<IServiceProvider, ContentInstallState, Exception, Task>? onError = null, int maxParallelDownloads = 4, int maxParallelModDownloads = 1, CancellationToken ct = default)
        {
            var nowActive = Interlocked.Increment(ref _active);
            Interlocked.Increment(ref _startedCount);
            MaxObserved = Math.Max(MaxObserved, nowActive);
            MaxParallelModDownloadsRequested = Math.Max(MaxParallelModDownloadsRequested, maxParallelModDownloads);
            try
            {
                await Task.Delay(delay, ct);
                Interlocked.Increment(ref _completedCount);
                return new ContentInstallState
                {
                    Key = ContentProgressTracker.ModKey(modId),
                    Type = ContentType.SteamMod,
                    Label = $"Mod {modId}",
                    IsComplete = true,
                    Steps = [new ContentStep { Name = "Download", Status = ContentStepStatus.Completed, Progress = 100 }]
                };
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public void Cancel(string key)
        {
        }

        public IReadOnlyList<ContentValidationResult> Validate(ContentType type, ContentInstallRequest request) => [];

        public ContentInstallState? GetState(string key) => null;
    }
}
