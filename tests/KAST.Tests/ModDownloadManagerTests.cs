using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services.Content;
using KAST.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KAST.Tests;

public class ModDownloadManagerTests
{
    [Fact]
    public async Task StartDownloadAsync_QueuesThroughOrchestratorAndSetsRunningStatus()
    {
        var mod = new SteamMod
        {
            Id = 12,
            WorkshopId = 101,
            Name = "ACE",
            Source = ModSource.SteamWorkshop,
            Status = ModStatus.NotInstalled
        };
        var modService = Substitute.For<IModService>();
        modService.GetModByIdAsync(12, Arg.Any<CancellationToken>()).Returns(mod);
        modService.UpdateModAsync(Arg.Any<SteamMod>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SteamMod>());
        var orchestrator = Substitute.For<IContentOrchestrator>();
        var manager = CreateManager(modService, orchestrator: orchestrator);

        var queued = await manager.StartDownloadAsync(12, isUpdate: false);

        Assert.True(queued);
        await modService.DidNotReceive().UpdateModAsync(Arg.Any<SteamMod>(), Arg.Any<CancellationToken>());
        orchestrator.Received(1).StartModInstall(
            12,
            ContentType.SteamMod,
            Path.Join("mods", "101"),
            101,
            null,
            Arg.Any<long>(),
            Arg.Any<Func<IServiceProvider, ContentInstallState, Task>>(),
            Arg.Any<Func<IServiceProvider, ContentInstallState, Task>>(),
            Arg.Any<Func<IServiceProvider, ContentInstallState, Exception, Task>>(),
            4,
            2);
    }

    [Fact]
    public async Task StartDownloadAsync_RejectsAlreadyRunningDownload()
    {
        var modService = Substitute.For<IModService>();
        var orchestrator = Substitute.For<IContentOrchestrator>();
        orchestrator.IsRunning(ContentProgressTracker.ModKey(7)).Returns(true);
        var manager = CreateManager(modService, orchestrator: orchestrator);

        var queued = await manager.StartDownloadAsync(7, isUpdate: false);

        Assert.False(queued);
        await modService.DidNotReceiveWithAnyArgs().GetModByIdAsync(default);
        orchestrator.DidNotReceiveWithAnyArgs().StartModInstall(default, default, default!, default, default, default, default, default, default, default);
    }

    [Fact]
    public void Cancel_DelegatesToOrchestrator()
    {
        var orchestrator = Substitute.For<IContentOrchestrator>();
        orchestrator.IsRunning(ContentProgressTracker.ModKey(3)).Returns(true);
        var manager = CreateManager(Substitute.For<IModService>(), orchestrator: orchestrator);

        Assert.True(manager.Cancel(3));

        orchestrator.Received(1).Cancel(ContentProgressTracker.ModKey(3));
    }

    [Fact]
    public async Task StartDownloadAsync_UsesUpdateStatusAndExistingPathWhenRequested()
    {
        var mod = new SteamMod
        {
            Id = 5,
            WorkshopId = 105,
            Name = "RHS",
            Source = ModSource.SteamWorkshop,
            Status = ModStatus.UpdateAvailable,
            LocalPath = "existing-mod"
        };
        var modService = Substitute.For<IModService>();
        modService.GetModByIdAsync(5, Arg.Any<CancellationToken>()).Returns(mod);
        modService.UpdateModAsync(Arg.Any<SteamMod>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SteamMod>());
        var orchestrator = Substitute.For<IContentOrchestrator>();
        var manager = CreateManager(modService, orchestrator: orchestrator);

        Assert.True(await manager.StartDownloadAsync(5, isUpdate: true));

        await modService.DidNotReceive().UpdateModAsync(Arg.Any<SteamMod>(), Arg.Any<CancellationToken>());
        orchestrator.Received(1).StartModInstall(
            5,
            ContentType.SteamMod,
            "existing-mod",
            105,
            null,
            Arg.Any<long>(),
            Arg.Any<Func<IServiceProvider, ContentInstallState, Task>>(),
            Arg.Any<Func<IServiceProvider, ContentInstallState, Task>>(),
            Arg.Any<Func<IServiceProvider, ContentInstallState, Exception, Task>>(),
            4,
            2);
    }

    [Fact]
    public async Task StartAllOutdatedAsync_QueuesEligibleWorkshopMods()
    {
        var mods = new List<SteamMod>
        {
            new() { Id = 1, WorkshopId = 101, Name = "New", Source = ModSource.SteamWorkshop, Status = ModStatus.NotInstalled },
            new() { Id = 2, WorkshopId = 102, Name = "Old", Source = ModSource.SteamWorkshop, Status = ModStatus.UpdateAvailable, LocalPath = "old-mod" },
            new() { Id = 3, WorkshopId = 103, Name = "Local", Source = ModSource.LocalFolder, Status = ModStatus.Error }
        };

        var modService = Substitute.For<IModService>();
        modService.GetAllModsAsync(Arg.Any<CancellationToken>()).Returns(mods);
        modService.GetModByIdAsync(1, Arg.Any<CancellationToken>()).Returns(mods[0]);
        modService.GetModByIdAsync(2, Arg.Any<CancellationToken>()).Returns(mods[1]);
        modService.UpdateModAsync(Arg.Any<SteamMod>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SteamMod>());
        var orchestrator = Substitute.For<IContentOrchestrator>();
        var manager = CreateManager(modService, orchestrator: orchestrator);

        var queued = await manager.StartAllOutdatedAsync();

        Assert.Equal(2, queued);
        orchestrator.Received(2).StartModInstall(
            Arg.Any<int>(),
            ContentType.SteamMod,
            Arg.Any<string>(),
            Arg.Any<long>(),
            null,
            Arg.Any<long>(),
            Arg.Any<Func<IServiceProvider, ContentInstallState, Task>>(),
            Arg.Any<Func<IServiceProvider, ContentInstallState, Task>>(),
            Arg.Any<Func<IServiceProvider, ContentInstallState, Exception, Task>>(),
            4,
            2);
    }

    private static ModDownloadManager CreateManager(
        IModService modService,
        IContentOrchestrator? orchestrator = null,
        ContentProgressTracker? tracker = null)
    {
        var services = new ServiceCollection();
        var settings = Substitute.For<ISettingsService>();
        settings.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new KastSettings { ModsDirectory = "mods", ParallelDownloads = 4, ParallelModDownloads = 2 });

        services.AddSingleton(modService);
        services.AddSingleton(settings);
        services.AddSingleton(Substitute.For<IFileSystemService>());
        services.AddSingleton(Substitute.For<IAppEventBroadcaster>());

        var provider = services.BuildServiceProvider();
        return new ModDownloadManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            orchestrator ?? Substitute.For<IContentOrchestrator>(),
            tracker ?? new ContentProgressTracker(),
            NullLogger<ModDownloadManager>.Instance);
    }
}
