using System.Net;
using System.Net.Http.Json;
using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services;
using KAST.Infrastructure.Services.Content;
using KAST.UI.Api;
using KAST.UI.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KAST.Tests;

public class KastApiModsEndpointsTests
{
    private const long EnhancedMovementWorkshopId = 333310405;

    [Fact]
    public async Task AddWorkshopMod_WithKnownWorkshopId_ReturnsCreatedAndCallsService()
    {
        var modService = Substitute.For<IModService>();
        modService.AddWorkshopModAsync(EnhancedMovementWorkshopId, Arg.Any<CancellationToken>())
            .Returns(new SteamMod { Id = 7, WorkshopId = EnhancedMovementWorkshopId, Name = "Small Test Mod" });

        await using var app = await CreateApiAppAsync(modService: modService);

        var response = await app.Client.PostAsync($"/api/mods/workshop/{EnhancedMovementWorkshopId}", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/mods/7", response.Headers.Location?.OriginalString);
        await modService.Received(1).AddWorkshopModAsync(EnhancedMovementWorkshopId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMods_ReturnsEntriesAcrossAllStatuses()
    {
        var modService = Substitute.For<IModService>();
        var mods = new List<SteamMod>
        {
            new() { Id = 1, WorkshopId = EnhancedMovementWorkshopId, Name = "Not Installed", Status = ModStatus.NotInstalled },
            new() { Id = 2, WorkshopId = EnhancedMovementWorkshopId + 1, Name = "Downloading", Status = ModStatus.Downloading },
            new() { Id = 3, WorkshopId = EnhancedMovementWorkshopId + 2, Name = "Installed", Status = ModStatus.Installed },
            new() { Id = 4, WorkshopId = EnhancedMovementWorkshopId + 3, Name = "Update Available", Status = ModStatus.UpdateAvailable },
            new() { Id = 5, WorkshopId = EnhancedMovementWorkshopId + 4, Name = "Updating", Status = ModStatus.Updating },
            new() { Id = 6, WorkshopId = EnhancedMovementWorkshopId + 5, Name = "Error", Status = ModStatus.Error }
        };

        modService.GetAllModsAsync(Arg.Any<CancellationToken>()).Returns(mods);

        await using var app = await CreateApiAppAsync(modService: modService);

        var response = await app.Client.GetAsync("/api/mods/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseMods = await response.Content.ReadFromJsonAsync<List<SteamMod>>();
        Assert.NotNull(responseMods);
        Assert.Equal(6, responseMods!.Count);
        Assert.Contains(responseMods, m => m.Status == ModStatus.NotInstalled);
        Assert.Contains(responseMods, m => m.Status == ModStatus.Downloading);
        Assert.Contains(responseMods, m => m.Status == ModStatus.Installed);
        Assert.Contains(responseMods, m => m.Status == ModStatus.UpdateAvailable);
        Assert.Contains(responseMods, m => m.Status == ModStatus.Updating);
        Assert.Contains(responseMods, m => m.Status == ModStatus.Error);

        await modService.Received(1).GetAllModsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadMod_WhenMissing_ReturnsNotFound()
    {
        var modService = Substitute.For<IModService>();
        modService.GetModByIdAsync(42, Arg.Any<CancellationToken>())
            .Returns((SteamMod?)null);

        var orchestrator = Substitute.For<IContentOrchestrator>();

        await using var app = await CreateApiAppAsync(
            modService: modService,
            orchestrator: orchestrator);

        var response = await app.Client.PostAsync("/api/mods/42/download", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        orchestrator.DidNotReceiveWithAnyArgs().StartModInstall(default, default, default!, default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task DownloadMod_WhenFound_SetsDownloadingBroadcastsAndStartsInstall()
    {
        const int modId = 10;

        var mod = new SteamMod
        {
            Id = modId,
            Name = "ACE",
            WorkshopId = EnhancedMovementWorkshopId,
            Status = ModStatus.NotInstalled
        };

        var modService = Substitute.For<IModService>();
        modService.GetModByIdAsync(modId, Arg.Any<CancellationToken>()).Returns(mod);
        modService.UpdateModAsync(Arg.Any<SteamMod>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<SteamMod>());

        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new KastSettings { ModsDirectory = "/tmp/kast-mods" });

        var broadcaster = Substitute.For<IAppEventBroadcaster>();
        var queue = Substitute.For<IModDownloadQueueService>();
        queue.QueueDownloadAsync(modId, false, Arg.Any<CancellationToken>())
            .Returns(new DownloadTask { Id = 1, ModId = modId, Status = DownloadStatus.Queued });

        await using var app = await CreateApiAppAsync(
            modService: modService,
            settingsService: settingsService,
            broadcaster: broadcaster,
            downloadQueue: queue);

        var response = await app.Client.PostAsync($"/api/mods/{modId}/download", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await modService.DidNotReceive().UpdateModAsync(Arg.Any<SteamMod>(), Arg.Any<CancellationToken>());
        await broadcaster.DidNotReceive().BroadcastModStatusChangedAsync(Arg.Any<ModStatusChangedEvent>());

        await queue.Received(1).QueueDownloadAsync(modId, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateMod_WhenMissing_ReturnsNotFound()
    {
        var modService = Substitute.For<IModService>();
        modService.GetModByIdAsync(12, Arg.Any<CancellationToken>())
            .Returns((SteamMod?)null);

        var queue = Substitute.For<IModDownloadQueueService>();

        await using var app = await CreateApiAppAsync(
            modService: modService,
            downloadQueue: queue);

        var response = await app.Client.PostAsync("/api/mods/12/update", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await queue.DidNotReceiveWithAnyArgs().QueueDownloadAsync(default, default, default);
    }

    [Fact]
    public async Task UpdateMod_WhenFound_UsesLocalPathAndSetsUpdating()
    {
        const int modId = 33;
        const string localPath = "/tmp/kast-mods/333310405";

        var mod = new SteamMod
        {
            Id = modId,
            Name = "RHS",
            WorkshopId = EnhancedMovementWorkshopId,
            LocalPath = localPath,
            Status = ModStatus.Installed
        };

        var modService = Substitute.For<IModService>();
        modService.GetModByIdAsync(modId, Arg.Any<CancellationToken>()).Returns(mod);
        modService.UpdateModAsync(Arg.Any<SteamMod>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<SteamMod>());

        var broadcaster = Substitute.For<IAppEventBroadcaster>();
        var queue = Substitute.For<IModDownloadQueueService>();
        queue.QueueDownloadAsync(modId, true, Arg.Any<CancellationToken>())
            .Returns(new DownloadTask { Id = 2, ModId = modId, Status = DownloadStatus.Queued });

        await using var app = await CreateApiAppAsync(
            modService: modService,
            broadcaster: broadcaster,
            downloadQueue: queue);

        var response = await app.Client.PostAsync($"/api/mods/{modId}/update", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await modService.DidNotReceive().UpdateModAsync(Arg.Any<SteamMod>(), Arg.Any<CancellationToken>());
        await broadcaster.DidNotReceive().BroadcastModStatusChangedAsync(Arg.Any<ModStatusChangedEvent>());

        await queue.Received(1).QueueDownloadAsync(modId, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckUpdates_DelegatesToService()
    {
        var modService = Substitute.For<IModService>();
        await using var app = await CreateApiAppAsync(modService: modService);

        var response = await app.Client.PostAsync("/api/mods/check-updates", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await modService.Received(1).CheckForUpdatesAsync(Arg.Any<CancellationToken>());
    }

    private static async Task<ApiAppContext> CreateApiAppAsync(
        IModService? modService = null,
        IContentOrchestrator? orchestrator = null,
        ISettingsService? settingsService = null,
        IFileSystemService? fileSystemService = null,
        IAppEventBroadcaster? broadcaster = null,
        IModDownloadQueueService? downloadQueue = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(modService ?? Substitute.For<IModService>());
        builder.Services.AddSingleton(Substitute.For<IServerInstanceService>());
        builder.Services.AddSingleton(Substitute.For<IMonitoringService>());
        builder.Services.AddSingleton(Substitute.For<IApiKeyService>());
        builder.Services.AddSingleton(Substitute.For<IModPresetService>());
        builder.Services.AddSingleton(Substitute.For<IMissionService>());
        builder.Services.AddSingleton(orchestrator ?? Substitute.For<IContentOrchestrator>());
        builder.Services.AddSingleton<ContentProgressTracker>();
        builder.Services.AddSingleton(settingsService ?? BuildDefaultSettingsService());
        builder.Services.AddSingleton(fileSystemService ?? Substitute.For<IFileSystemService>());
        builder.Services.AddSingleton(broadcaster ?? BuildDefaultBroadcaster());
        builder.Services.AddSingleton(downloadQueue ?? Substitute.For<IModDownloadQueueService>());
        builder.Services.AddSingleton(sp => new ModDownloadManager(
            sp.GetRequiredService<IModDownloadQueueService>()));
        builder.Services.AddSingleton<IOutputSanitizer, OutputSanitizer>();

        var app = builder.Build();
        app.MapGroup("/api").MapKastApi();
        await app.StartAsync();

        return new ApiAppContext(app, app.GetTestClient());
    }

    private static ISettingsService BuildDefaultSettingsService()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new KastSettings { ModsDirectory = "/tmp/kast-mods" });
        return settings;
    }

    private static IAppEventBroadcaster BuildDefaultBroadcaster()
    {
        var events = Substitute.For<IAppEventBroadcaster>();
        events.BroadcastDownloadProgressAsync(Arg.Any<ModDownloadProgressEvent>()).Returns(Task.CompletedTask);
        events.BroadcastModStatusChangedAsync(Arg.Any<ModStatusChangedEvent>()).Returns(Task.CompletedTask);
        events.BroadcastServerStatusChangedAsync(Arg.Any<ServerStatusChangedEvent>()).Returns(Task.CompletedTask);
        events.BroadcastHostMetricsAsync(Arg.Any<HostMetricsUpdatedEvent>()).Returns(Task.CompletedTask);
        events.BroadcastInstanceMetricsAsync(Arg.Any<InstanceMetricsUpdatedEvent>()).Returns(Task.CompletedTask);
        events.BroadcastLogEntryAsync(Arg.Any<LogEntryEvent>()).Returns(Task.CompletedTask);
        return events;
    }

    private sealed class ApiAppContext : IAsyncDisposable
    {
        public ApiAppContext(WebApplication app, HttpClient client)
        {
            App = app;
            Client = client;
        }

        public WebApplication App { get; }
        public HttpClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
}
