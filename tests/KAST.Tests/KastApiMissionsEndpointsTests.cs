using System.Net;
using System.Net.Http.Json;
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

public class KastApiMissionsEndpointsTests
{
    [Fact]
    public async Task GetMissions_ReturnsFromService()
    {
        var missionService = Substitute.For<IMissionService>();
        var missions = new List<Mission>
        {
            new() { Id = 1, FileName = "co40_tanoa.pbo", DisplayName = "Operation Tanoa" },
            new() { Id = 2, FileName = "zeus_altis.pbo", DisplayName = "Zeus Altis" }
        };
        missionService.SearchMissionsAsync(5, null, null, null, Arg.Any<CancellationToken>())
            .Returns(missions);

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.GetAsync("/api/missions/instance/5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<List<Mission>>();
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
    }

    [Fact]
    public async Task GetMissionById_NotFound_Returns404()
    {
        var missionService = Substitute.For<IMissionService>();
        missionService.GetMissionByIdAsync(999, Arg.Any<CancellationToken>())
            .Returns((Mission?)null);

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.GetAsync("/api/missions/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMission_DelegatesToService()
    {
        var missionService = Substitute.For<IMissionService>();
        missionService.UpdateMissionAsync(Arg.Any<Mission>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<Mission>()));

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.PutAsJsonAsync("/api/missions/1",
            new Mission { DisplayName = "Updated Name", MapName = "Tanoa" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await missionService.Received(1).UpdateMissionAsync(
            Arg.Is<Mission>(m => m.Id == 1 && m.DisplayName == "Updated Name" && m.MapName == "Tanoa"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteMission_ReturnsNoContent()
    {
        var missionService = Substitute.For<IMissionService>();

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.DeleteAsync("/api/missions/1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await missionService.Received(1).DeleteMissionAsync(1, Arg.Any<CancellationToken>());
    }

    // ── Tags ──

    [Fact]
    public async Task GetTags_ReturnsFromService()
    {
        var missionService = Substitute.For<IMissionService>();
        var tags = new List<MissionTag>
        {
            new() { Id = 1, Name = "pve" },
            new() { Id = 2, Name = "zeus" }
        };
        missionService.GetTagsForInstanceAsync(5, Arg.Any<CancellationToken>())
            .Returns(tags);

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.GetAsync("/api/missions/instance/5/tags");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<List<MissionTag>>();
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
    }

    [Fact]
    public async Task CreateTag_ReturnsCreated()
    {
        var missionService = Substitute.For<IMissionService>();
        missionService.CreateTagAsync(5, "night-ops", Arg.Any<CancellationToken>())
            .Returns(new MissionTag { Id = 7, Name = "night-ops" });

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.PostAsJsonAsync("/api/missions/instance/5/tags",
            new CreateTagRequest("night-ops"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AssignTag_ReturnsOk()
    {
        var missionService = Substitute.For<IMissionService>();

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.PostAsync("/api/missions/1/tags/3", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await missionService.Received(1).AssignTagAsync(1, 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveTag_ReturnsNoContent()
    {
        var missionService = Substitute.For<IMissionService>();

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.DeleteAsync("/api/missions/1/tags/3");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await missionService.Received(1).RemoveTagAsync(1, 3, Arg.Any<CancellationToken>());
    }

    // ── Campaigns ──

    [Fact]
    public async Task GetCampaigns_ReturnsFromService()
    {
        var missionService = Substitute.For<IMissionService>();
        missionService.GetCampaignsForInstanceAsync(5, Arg.Any<CancellationToken>())
            .Returns(new List<Campaign> { new() { Id = 1, Title = "Eastern Front" } });

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.GetAsync("/api/campaigns/instance/5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<List<Campaign>>();
        Assert.NotNull(result);
        Assert.Single(result!);
    }

    [Fact]
    public async Task CreateCampaign_ReturnsCreated()
    {
        var missionService = Substitute.For<IMissionService>();
        missionService.CreateCampaignAsync(Arg.Any<Campaign>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var c = ci.Arg<Campaign>();
                c.Id = 10;
                return Task.FromResult(c);
            });

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.PostAsJsonAsync("/api/campaigns/instance/5",
            new Campaign { Title = "Weekend Ops" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith("/10", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AddMissionToCampaign_ReturnsOk()
    {
        var missionService = Substitute.For<IMissionService>();

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.PostAsJsonAsync("/api/campaigns/1/missions",
            new AddCampaignMissionRequest(42, 0));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await missionService.Received(1).AddMissionToCampaignAsync(1, 42, 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReorderCampaignMissions_ReturnsOk()
    {
        var missionService = Substitute.For<IMissionService>();

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.PutAsJsonAsync("/api/campaigns/1/missions/reorder",
            new ReorderRequest(new List<int> { 3, 1, 2 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await missionService.Received(1).ReorderCampaignMissionsAsync(
            1, Arg.Is<List<int>>(l => l.Count == 3 && l[0] == 3), Arg.Any<CancellationToken>());
    }

    // ── Sets ──

    [Fact]
    public async Task GetSets_ReturnsFromService()
    {
        var missionService = Substitute.For<IMissionService>();
        missionService.GetSetsForInstanceAsync(5, Arg.Any<CancellationToken>())
            .Returns(new List<Set> { new() { Id = 1, Title = "My Set" } });

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.GetAsync("/api/sets/instance/5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<List<Set>>();
        Assert.NotNull(result);
        Assert.Single(result!);
    }

    [Fact]
    public async Task CreateSet_ReturnsCreated()
    {
        var missionService = Substitute.For<IMissionService>();
        missionService.CreateSetAsync(Arg.Any<Set>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var s = ci.Arg<Set>();
                s.Id = 15;
                return Task.FromResult(s);
            });

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.PostAsJsonAsync("/api/sets/instance/5",
            new Set { Title = "Quick Play" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith("/15", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AddMissionToSet_ReturnsOk()
    {
        var missionService = Substitute.For<IMissionService>();

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.PostAsJsonAsync("/api/sets/1/missions",
            new AddSetMissionRequest(99));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await missionService.Received(1).AddMissionToSetAsync(1, 99, Arg.Any<CancellationToken>());
    }

    // ── Search ──

    [Fact]
    public async Task SearchMissions_WithQueryParameters_ParsesTags()
    {
        var missionService = Substitute.For<IMissionService>();
        var missions = new List<Mission>();
        missionService.SearchMissionsAsync(
            5,
            "tanoa",
            Arg.Is<List<int>>(l => l.Count == 2 && l[0] == 1 && l[1] == 2),
            "Altis",
            Arg.Any<CancellationToken>())
            .Returns(missions);

        await using var app = await CreateApiAppAsync(missionService: missionService);

        var response = await app.Client.GetAsync("/api/missions/instance/5?search=tanoa&tags=1,2&map=Altis");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await missionService.Received(1).SearchMissionsAsync(
            5, "tanoa", Arg.Any<List<int>>(), "Altis", Arg.Any<CancellationToken>());
    }

    private static async Task<ApiAppContext> CreateApiAppAsync(
        IMissionService? missionService = null)
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
        builder.Services.AddSingleton(Substitute.For<IModPresetService>());
        builder.Services.AddSingleton(missionService ?? Substitute.For<IMissionService>());
        builder.Services.AddSingleton(sp => new ModDownloadManager(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IContentOrchestrator>(),
            sp.GetRequiredService<ContentProgressTracker>(),
            NullLogger<ModDownloadManager>.Instance));

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
