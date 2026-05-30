using System.Net;
using System.Net.Http.Json;
using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Services.Content;
using KAST.UI.Api;
using KAST.UI.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KAST.Tests;

public class KastApiModsEndToEndTests
{
    private const long EnhancedMovementWorkshopId = 333310405;
    private const string EnhancedMovementModName = "Enhanced Movement Mod";

    [Fact]
    public async Task WorkshopEndpoint_CreatesAndPersistsMod_ThenGetByIdReturnsIt()
    {
        await using var app = await EndToEndApiApp.CreateAsync();

        var createResponse = await app.Client.PostAsync($"/api/mods/workshop/{EnhancedMovementWorkshopId}", content: null);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<SteamMod>();
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);
        Assert.Equal(EnhancedMovementWorkshopId, created.WorkshopId);
        Assert.Equal(EnhancedMovementModName, created.Name);

        var getResponse = await app.Client.GetAsync($"/api/mods/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var fetched = await getResponse.Content.ReadFromJsonAsync<SteamMod>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var fromDb = await db.Mods.FindAsync(created.Id);
        Assert.NotNull(fromDb);
        Assert.Equal(EnhancedMovementWorkshopId, fromDb!.WorkshopId);
    }

    [Fact]
    public async Task WorkshopEndpoint_DuplicateWorkshopId_ReturnsSameEntity()
    {
        await using var app = await EndToEndApiApp.CreateAsync();

        var firstResponse = await app.Client.PostAsync($"/api/mods/workshop/{EnhancedMovementWorkshopId}", content: null);
        var secondResponse = await app.Client.PostAsync($"/api/mods/workshop/{EnhancedMovementWorkshopId}", content: null);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

        var first = await firstResponse.Content.ReadFromJsonAsync<SteamMod>();
        var second = await secondResponse.Content.ReadFromJsonAsync<SteamMod>();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        Assert.Equal(1, await db.Mods.CountAsync());
    }

    [Fact]
    public async Task LocalModEndpoints_CreateListDelete_WorkEndToEnd()
    {
        await using var app = await EndToEndApiApp.CreateAsync();

        var createResponse = await app.Client.PostAsJsonAsync("/api/mods/local", new
        {
            Path = "/tmp/local-mod-folder",
            Name = EnhancedMovementModName
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<SteamMod>();
        Assert.NotNull(created);

        var listResponse = await app.Client.GetAsync("/api/mods/");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var listed = await listResponse.Content.ReadFromJsonAsync<List<SteamMod>>();
        Assert.NotNull(listed);
        Assert.Contains(listed!, m => m.Id == created!.Id && m.Source == ModSource.LocalFolder);

        var deleteResponse = await app.Client.DeleteAsync($"/api/mods/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDelete = await app.Client.GetAsync($"/api/mods/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task GetMods_ReturnsAllStatuses_FromRealSqliteDb()
    {
        await using var app = await EndToEndApiApp.CreateAsync();

        await app.SeedAsync(async db =>
        {
            db.Mods.AddRange(
                new SteamMod { Name = "NotInstalled", WorkshopId = EnhancedMovementWorkshopId + 1, Status = ModStatus.NotInstalled },
                new SteamMod { Name = "Downloading", WorkshopId = EnhancedMovementWorkshopId + 2, Status = ModStatus.Downloading },
                new SteamMod { Name = "Installed", WorkshopId = EnhancedMovementWorkshopId + 3, Status = ModStatus.Installed },
                new SteamMod { Name = "UpdateAvailable", WorkshopId = EnhancedMovementWorkshopId + 4, Status = ModStatus.UpdateAvailable },
                new SteamMod { Name = "Updating", WorkshopId = EnhancedMovementWorkshopId + 5, Status = ModStatus.Updating },
                new SteamMod { Name = "Error", WorkshopId = EnhancedMovementWorkshopId + 6, Status = ModStatus.Error });
            await db.SaveChangesAsync();
        });

        var response = await app.Client.GetAsync("/api/mods/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mods = await response.Content.ReadFromJsonAsync<List<SteamMod>>();
        Assert.NotNull(mods);
        Assert.Contains(mods!, m => m.Status == ModStatus.NotInstalled);
        Assert.Contains(mods!, m => m.Status == ModStatus.Downloading);
        Assert.Contains(mods!, m => m.Status == ModStatus.Installed);
        Assert.Contains(mods!, m => m.Status == ModStatus.UpdateAvailable);
        Assert.Contains(mods!, m => m.Status == ModStatus.Updating);
        Assert.Contains(mods!, m => m.Status == ModStatus.Error);
    }

    private sealed class EndToEndApiApp : IAsyncDisposable
    {
        private EndToEndApiApp(WebApplication app, HttpClient client, string tempRoot)
        {
            App = app;
            Client = client;
            TempRoot = tempRoot;
        }

        public WebApplication App { get; }
        public HttpClient Client { get; }
        public string TempRoot { get; }
        public IServiceProvider Services => App.Services;

        public static async Task<EndToEndApiApp> CreateAsync()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"kast-api-e2e-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            var modsDir = Path.Combine(tempRoot, "mods");
            var serversDir = Path.Combine(tempRoot, "servers");
            Directory.CreateDirectory(modsDir);
            Directory.CreateDirectory(serversDir);

            var dbPath = Path.Combine(tempRoot, "kast-e2e.db");

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing",
                ContentRootPath = tempRoot
            });

            builder.WebHost.UseTestServer();

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kast:ModsDirectory"] = modsDir,
                ["Kast:ServersDirectory"] = serversDir,
                ["Kast:Arma3AppId"] = "233780"
            });

            builder.Services.AddKastInfrastructure($"Data Source={dbPath}");
            builder.Services.AddSingleton<ISteamService>(new FakeSteamService());
            builder.Services.AddSingleton<IAppEventBroadcaster, NoopAppEventBroadcaster>();
            builder.Services.AddSingleton(sp => new ModDownloadManager(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IContentOrchestrator>(),
                sp.GetRequiredService<ContentProgressTracker>(),
                NullLogger<ModDownloadManager>.Instance));

            var app = builder.Build();
            app.MapGroup("/api").MapKastApi();
            await app.StartAsync();

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            return new EndToEndApiApp(app, app.GetTestClient(), tempRoot);
        }

        public async Task SeedAsync(Func<KastDbContext, Task> seed)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
            await seed(db);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();

            if (Directory.Exists(TempRoot))
            {
                try
                {
                    Directory.Delete(TempRoot, recursive: true);
                }
                catch
                {
                    // Best effort cleanup for temp integration files.
                }
            }
        }
    }

    private sealed class NoopAppEventBroadcaster : IAppEventBroadcaster
    {
        public event Action<ModDownloadProgressEvent>? OnModDownloadProgress;
        public event Action<ModStatusChangedEvent>? OnModStatusChanged;
        public event Action<ServerStatusChangedEvent>? OnServerStatusChanged;
        public Task BroadcastDownloadProgressAsync(ModDownloadProgressEvent progress) => Task.CompletedTask;
        public Task BroadcastModStatusChangedAsync(ModStatusChangedEvent status) => Task.CompletedTask;
        public Task BroadcastServerStatusChangedAsync(ServerStatusChangedEvent status) => Task.CompletedTask;
        public Task BroadcastHostMetricsAsync(HostMetricsUpdatedEvent metrics) => Task.CompletedTask;
        public Task BroadcastInstanceMetricsAsync(InstanceMetricsUpdatedEvent metrics) => Task.CompletedTask;
        public Task BroadcastLogEntryAsync(LogEntryEvent logEntry) => Task.CompletedTask;
    }

    private sealed class FakeSteamService : ISteamService
    {
        public bool IsAuthenticated => false;
        public bool IsConnected => false;
        public string? CurrentUsername => null;
        public SteamUserProfile? Profile => null;
        public event Action? AuthStateChanged
        {
            add
            {
                // Test double does not publish auth-state changes.
            }
            remove
            {
                // Test double does not publish auth-state changes.
            }
        }

        public Task<bool> LoginAnonymousAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> LoginWithTokenAsync(string username, string refreshToken, CancellationToken ct = default) => Task.FromResult(false);
        public Task<SteamQrAuthSession> BeginQrLoginAsync(CancellationToken ct = default) => Task.FromResult(new SteamQrAuthSession());
        public Task<bool> PollQrLoginAsync(SteamQrAuthSession session, CancellationToken ct = default) => Task.FromResult(false);
        public Task<SteamCredentialAuthSession> BeginCredentialLoginAsync(string username, string password, CancellationToken ct = default) => Task.FromResult(new SteamCredentialAuthSession());
        public Task<bool> SubmitCredentialGuardCodeAsync(SteamCredentialAuthSession session, string code, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> PollCredentialLoginAsync(SteamCredentialAuthSession session, CancellationToken ct = default) => Task.FromResult(false);
        public Task LogoutAsync() => Task.CompletedTask;

        public Task<ulong> DownloadWorkshopItemAsync(long workshopId, string destinationPath, IProgress<double>? progress = null, int maxParallelDownloads = 4, CancellationToken ct = default)
            => throw new InvalidOperationException("Downloads are intentionally disabled in end-to-end API tests.");

        public Task DownloadAppAsync(uint appId, string destinationPath, IProgress<double>? progress = null, IProgress<string>? logProgress = null,
            bool ignorePlatformFilter = false, string branch = "public", uint[]? depotFilter = null, int maxParallelDownloads = 4,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Server downloads are intentionally disabled in end-to-end API tests.");

        public Task<WorkshopItemInfo?> GetWorkshopItemInfoAsync(long workshopId, CancellationToken ct = default)
            => Task.FromResult<WorkshopItemInfo?>(new WorkshopItemInfo
            {
                WorkshopId = workshopId,
                Name = EnhancedMovementModName,
                Description = "Fake workshop metadata for integration tests",
                Author = "test",
                SizeBytes = 1024,
                LastUpdated = DateTime.UtcNow
            });

        public Task<IReadOnlyList<WorkshopItemInfo>> SearchWorkshopAsync(string query, int count = 20, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WorkshopItemInfo>>(Array.Empty<WorkshopItemInfo>());

        public Task<IReadOnlyList<BenchmarkResult>> BenchmarkDownloadAsync(IProgress<string>? log = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BenchmarkResult>>(Array.Empty<BenchmarkResult>());
    }
}
