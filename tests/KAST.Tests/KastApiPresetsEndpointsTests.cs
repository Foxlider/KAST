using System.Net;
using System.Net.Http.Json;
using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services.Content;
using KAST.UI.Api;
using KAST.UI.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KAST.Tests;

public class KastApiPresetsEndpointsTests
{
    [Fact]
    public async Task GetPresets_ReturnsFromService()
    {
        var presetService = Substitute.For<IModPresetService>();
        var presets = new List<ModPreset>
        {
            new() { Id = 1, Name = "My Preset", Type = ModPresetType.Kast },
            new() { Id = 2, Name = "Arma Import", Type = ModPresetType.Arma }
        };
        presetService.GetPresetsForInstanceAsync(5, Arg.Any<CancellationToken>())
            .Returns(presets);

        await using var app = await CreateApiAppAsync(presetsService: presetService);

        var response = await app.Client.GetAsync("/api/presets/instance/5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<List<ModPreset>>();
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
    }

    [Fact]
    public async Task GetPresetById_NotFound_Returns404()
    {
        var presetService = Substitute.For<IModPresetService>();
        ModPreset? missingPreset = null;
        presetService.GetPresetByIdAsync(999, Arg.Any<CancellationToken>())
            .Returns(missingPreset);

        await using var app = await CreateApiAppAsync(presetsService: presetService);

        var response = await app.Client.GetAsync("/api/presets/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPresetById_Found_ReturnsWithEntries()
    {
        var presetService = Substitute.For<IModPresetService>();
        var preset = new ModPreset
        {
            Id = 1,
            Name = "Full Layout",
            Type = ModPresetType.Kast,
            Entries = { new ModPresetEntry { SteamModId = 10, IsClientSide = true } }
        };
        presetService.GetPresetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(preset);

        await using var app = await CreateApiAppAsync(presetsService: presetService);

        var response = await app.Client.GetAsync("/api/presets/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ModPreset>();
        Assert.NotNull(result);
        Assert.Equal("Full Layout", result!.Name);
    }

    [Fact]
    public async Task SaveKastPreset_DelegatesToService()
    {
        var presetService = Substitute.For<IModPresetService>();
        var preset = new ModPreset { Id = 7, Name = "Saved Layout", Type = ModPresetType.Kast };
        presetService.SaveInstanceAsKastPresetAsync(5, "Saved Layout", Arg.Any<CancellationToken>())
            .Returns(preset);

        await using var app = await CreateApiAppAsync(presetsService: presetService);

        var response = await app.Client.PostAsJsonAsync("/api/presets/instance/5/save",
            new SaveKastPresetRequest("Saved Layout"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/presets/7", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ImportArmaPreset_DelegatesToService()
    {
        var presetService = Substitute.For<IModPresetService>();
        var preset = new ModPreset { Id = 3, Name = "My Import", Type = ModPresetType.Arma };
        presetService.ImportFromArmaHtmlAsync(5, "My Import", "<html>content</html>", Arg.Any<CancellationToken>())
            .Returns(preset);

        await using var app = await CreateApiAppAsync(presetsService: presetService);

        var response = await app.Client.PostAsJsonAsync("/api/presets/instance/5/import",
            new ImportPresetRequest("My Import", "<html>content</html>"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/presets/3", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task UpdatePreset_ReturnsUpdated()
    {
        var presetService = Substitute.For<IModPresetService>();
        presetService.UpdatePresetAsync(Arg.Any<ModPreset>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<ModPreset>()));

        await using var app = await CreateApiAppAsync(presetsService: presetService);

        var response = await app.Client.PutAsJsonAsync("/api/presets/1",
            new ModPreset { Name = "Renamed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await presetService.Received(1).UpdatePresetAsync(
            Arg.Is<ModPreset>(p => p.Id == 1 && p.Name == "Renamed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeletePreset_ReturnsNoContent()
    {
        var presetService = Substitute.For<IModPresetService>();

        await using var app = await CreateApiAppAsync(presetsService: presetService);

        var response = await app.Client.DeleteAsync("/api/presets/1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await presetService.Received(1).DeletePresetAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyPreset_ReturnsOk()
    {
        var presetService = Substitute.For<IModPresetService>();

        await using var app = await CreateApiAppAsync(presetsService: presetService);

        var response = await app.Client.PostAsync("/api/presets/1/apply/5", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await presetService.Received(1).ApplyPresetAsync(1, 5, Arg.Any<CancellationToken>());
    }

    private static async Task<ApiAppContext> CreateApiAppAsync(
        IModPresetService? presetsService = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(Substitute.For<IModService>());
        builder.Services.AddSingleton(Substitute.For<IServerInstanceService>());
        builder.Services.AddSingleton(Substitute.For<IMonitoringService>());
        builder.Services.AddSingleton(Substitute.For<IApiKeyService>());
        builder.Services.AddSingleton(Substitute.For<ISettingsService>());
        builder.Services.AddSingleton(Substitute.For<IContentOrchestrator>());
        builder.Services.AddSingleton<ContentProgressTracker>();
        builder.Services.AddSingleton(Substitute.For<IFileSystemService>());
        builder.Services.AddSingleton(Substitute.For<IAppEventBroadcaster>());
        builder.Services.AddSingleton(Substitute.For<IMissionService>());
        builder.Services.AddSingleton(presetsService ?? Substitute.For<IModPresetService>());
        builder.Services.AddSingleton(Substitute.For<IModDownloadQueueService>());
        builder.Services.AddSingleton(sp => new ModDownloadManager(
            sp.GetRequiredService<IModDownloadQueueService>()));

        var app = builder.Build();
        app.MapGroup("/api").MapKastApi();
        await app.StartAsync();

        return new ApiAppContext(app, app.GetTestClient());
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
