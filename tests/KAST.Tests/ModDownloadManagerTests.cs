using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KAST.Tests;

public class ModDownloadManagerTests
{
    [Fact]
    public async Task StartDownload_RunsDownloadInBackgroundScope()
    {
        var modService = Substitute.For<IModService>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        modService.DownloadModAsync(12, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.SetResult();
                return release.Task;
            });
        var manager = CreateManager(modService);

        var queued = manager.StartDownload(12, isUpdate: false);

        Assert.True(queued);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(manager.IsActive(12));
        release.SetResult();
        await WaitForAsync(() => !manager.IsActive(12));
        await modService.Received(1).DownloadModAsync(12, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartDownload_RejectsDuplicateActiveDownload()
    {
        var modService = Substitute.For<IModService>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        modService.DownloadModAsync(7, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.SetResult();
                return release.Task;
            });
        var manager = CreateManager(modService);

        Assert.True(manager.StartDownload(7, isUpdate: false));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(manager.StartDownload(7, isUpdate: false));

        release.SetResult();
        await WaitForAsync(() => !manager.IsActive(7));
        await modService.Received(1).DownloadModAsync(7, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_CancelsActiveDownloadToken()
    {
        var modService = Substitute.For<IModService>();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        modService.DownloadModAsync(3, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ct = call.ArgAt<CancellationToken>(2);
                started.SetResult(ct);
                return release.Task;
            });
        var manager = CreateManager(modService);

        Assert.True(manager.StartDownload(3, isUpdate: false));
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(manager.Cancel(3));
        Assert.True(token.IsCancellationRequested);

        release.SetCanceled(token);
        await WaitForAsync(() => !manager.IsActive(3));
    }

    [Fact]
    public async Task StartDownload_UsesUpdateWhenRequested()
    {
        var modService = Substitute.For<IModService>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        modService.UpdateModFilesAsync(5, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.SetResult();
                return Task.CompletedTask;
            });
        var manager = CreateManager(modService);

        Assert.True(manager.StartDownload(5, isUpdate: true));

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForAsync(() => !manager.IsActive(5));
        await modService.Received(1).UpdateModFilesAsync(5, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAllOutdatedAsync_QueuesEachDownloadInSeparateScope()
    {
        var state = new ScopedModServiceState
        {
            Mods =
            [
                new SteamMod { Id = 1, WorkshopId = 101, Name = "New", Source = ModSource.SteamWorkshop, Status = ModStatus.NotInstalled },
                new SteamMod { Id = 2, WorkshopId = 102, Name = "Old", Source = ModSource.SteamWorkshop, Status = ModStatus.UpdateAvailable },
                new SteamMod { Id = 3, WorkshopId = 103, Name = "Local", Source = ModSource.LocalFolder, Status = ModStatus.Error }
            ]
        };
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddScoped<IModService, ScopedModService>();
        var provider = services.BuildServiceProvider();
        var manager = new ModDownloadManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ModDownloadManager>.Instance);

        var queued = await manager.StartAllOutdatedAsync();

        Assert.Equal(2, queued);
        await WaitForAsync(() => state.OperationCount == 2);
        Assert.Equal(2, state.OperationScopeCount);
    }

    private static ModDownloadManager CreateManager(IModService modService)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => modService);
        var provider = services.BuildServiceProvider();
        return new ModDownloadManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ModDownloadManager>.Instance);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class ScopedModServiceState
    {
        private readonly object _lock = new();

        public IReadOnlyList<SteamMod> Mods { get; init; } = [];
        private HashSet<Guid> DownloadScopeIds { get; } = [];
        private HashSet<Guid> UpdateScopeIds { get; } = [];

        public int OperationCount
        {
            get
            {
                lock (_lock)
                    return DownloadScopeIds.Count + UpdateScopeIds.Count;
            }
        }

        public int OperationScopeCount
        {
            get
            {
                lock (_lock)
                    return DownloadScopeIds.Concat(UpdateScopeIds).Distinct().Count();
            }
        }

        public void RecordDownload(Guid scopeId)
        {
            lock (_lock)
                DownloadScopeIds.Add(scopeId);
        }

        public void RecordUpdate(Guid scopeId)
        {
            lock (_lock)
                UpdateScopeIds.Add(scopeId);
        }
    }

    private sealed class ScopedModService(ScopedModServiceState state) : IModService
    {
        private readonly Guid _scopeId = Guid.NewGuid();

        public Task<IReadOnlyList<SteamMod>> GetAllModsAsync(CancellationToken ct = default)
            => Task.FromResult(state.Mods);

        public Task DownloadModAsync(int id, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            state.RecordDownload(_scopeId);
            return Task.CompletedTask;
        }

        public Task UpdateModFilesAsync(int id, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            state.RecordUpdate(_scopeId);
            return Task.CompletedTask;
        }

        public Task<SteamMod?> GetModByIdAsync(int id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SteamMod?> GetModByWorkshopIdAsync(long workshopId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SteamMod> AddWorkshopModAsync(long workshopId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SteamMod> ImportLocalModAsync(string path, string name, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteModAsync(int id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SteamMod> UpdateModAsync(SteamMod mod, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CheckForUpdatesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task CheckModForUpdateAsync(int id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAllOutdatedModsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }
}
