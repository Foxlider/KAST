using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services;
using KAST.Tests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace KAST.Tests;

public class ServerInstanceServiceTests : IDisposable
{
    private readonly Infrastructure.Data.KastDbContext _db;
    private readonly IProcessManagerService _processManager;
    private readonly IAppEventBroadcaster _broadcaster;
    private readonly ILogger<ServerInstanceService> _logger;
    private readonly ServerInstanceService _sut;
    private bool _disposed = false;

    public ServerInstanceServiceTests()
    {
        _db = DbHelper.CreateInMemoryDb();
        _processManager = Substitute.For<IProcessManagerService>();
        _broadcaster = Substitute.For<IAppEventBroadcaster>();
        _logger = Substitute.For<ILogger<ServerInstanceService>>();
        _sut = new ServerInstanceService(_db, _processManager, _broadcaster, _logger);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _db?.Dispose();
            }
            _disposed = true;
        }
    }

    ~ServerInstanceServiceTests()
    {
        Dispose(false);
    }

    private async Task<ServerInstance> SeedInstanceAsync(
        string name = "Test Server",
        ServerInstanceStatus status = ServerInstanceStatus.Stopped,
        int? processId = null)
    {
        var instance = new ServerInstance
        {
            Name = name,
            InstallPath = "/tmp/arma3",
            Port = 2302,
            Status = status,
            ProcessId = processId
        };
        _db.ServerInstances.Add(instance);
        await _db.SaveChangesAsync();
        return instance;
    }

    // ── CRUD ──

    [Fact]
    public async Task CreateInstance_PersistsToDb()
    {
        var instance = new ServerInstance { Name = "New Server", InstallPath = "/tmp/a3" };

        var created = await _sut.CreateInstanceAsync(instance);

        Assert.True(created.Id > 0);
        Assert.Single(_db.ServerInstances);
        Assert.NotEqual(default, created.CreatedAt);
    }

    [Fact]
    public async Task GetAllInstances_ReturnsOrderedByName()
    {
        await SeedInstanceAsync("Charlie");
        await SeedInstanceAsync("Alpha");
        await SeedInstanceAsync("Bravo");

        var instances = await _sut.GetAllInstancesAsync();

        Assert.Equal(3, instances.Count);
        Assert.Equal("Alpha", instances[0].Name);
        Assert.Equal("Bravo", instances[1].Name);
        Assert.Equal("Charlie", instances[2].Name);
    }

    [Fact]
    public async Task GetInstanceById_ReturnsInstanceWithMods()
    {
        var instance = await SeedInstanceAsync();
        var mod = new SteamMod { Name = "Mod", WorkshopId = 1 };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        _db.ServerInstanceMods.Add(new ServerInstanceMod
        {
            ServerInstanceId = instance.Id,
            SteamModId = mod.Id
        });
        await _db.SaveChangesAsync();

        var found = await _sut.GetInstanceByIdAsync(instance.Id);

        Assert.NotNull(found);
        Assert.Single(found!.Mods);
    }

    [Fact]
    public async Task GetInstanceById_NonExistent_ReturnsNull()
    {
        var result = await _sut.GetInstanceByIdAsync(999);
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateInstance_SetsLastModified()
    {
        var instance = await SeedInstanceAsync();
        Assert.Null(instance.LastModified);

        instance.Name = "Updated";
        await _sut.UpdateInstanceAsync(instance);

        var updated = await _db.ServerInstances.FindAsync(instance.Id);
        Assert.Equal("Updated", updated!.Name);
        Assert.NotNull(updated.LastModified);
    }

    [Fact]
    public async Task DeleteInstance_RemovesFromDb()
    {
        var instance = await SeedInstanceAsync();
        await _sut.DeleteInstanceAsync(instance.Id);
        Assert.Empty(_db.ServerInstances);
    }

    [Fact]
    public async Task DeleteInstance_RunningServer_StopsFirst()
    {
        var instance = await SeedInstanceAsync(status: ServerInstanceStatus.Running, processId: 1234);

        _processManager.StopProcessAsync(1234, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.DeleteInstanceAsync(instance.Id);

        await _processManager.Received().StopProcessAsync(1234, Arg.Any<CancellationToken>());
        Assert.Empty(_db.ServerInstances);
    }

    // ── Start / Stop / Restart ──

    [Fact]
    public async Task StartInstance_NonExistentId_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.StartInstanceAsync(999));
    }

    [Fact]
    public async Task StartInstance_AlreadyRunning_NoOp()
    {
        var instance = await SeedInstanceAsync(status: ServerInstanceStatus.Running);

        await _sut.StartInstanceAsync(instance.Id);

        await _processManager.DidNotReceive()
            .StartServerProcessAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action<int, string>?>(), Arg.Any<Action<int, int>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartInstance_SetsRunningStatus()
    {
        var instance = await SeedInstanceAsync();

        _processManager.StartServerProcessAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action<int, string>?>(), Arg.Any<Action<int, int>?>(), Arg.Any<CancellationToken>())
            .Returns(42);

        await _sut.StartInstanceAsync(instance.Id);

        var updated = await _db.ServerInstances.FindAsync(instance.Id);
        Assert.Equal(ServerInstanceStatus.Running, updated!.Status);
        Assert.Equal(42, updated.ProcessId);
        Assert.NotNull(updated.StartedAt);
    }

    [Fact]
    public async Task StartInstance_BroadcastsStatusEvents()
    {
        var instance = await SeedInstanceAsync();

        _processManager.StartServerProcessAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action<int, string>?>(), Arg.Any<Action<int, int>?>(), Arg.Any<CancellationToken>())
            .Returns(42);

        await _sut.StartInstanceAsync(instance.Id);

        // Starting + Running
        await _broadcaster.Received(2)
            .BroadcastServerStatusChangedAsync(Arg.Any<ServerStatusChangedEvent>());
    }

    [Fact]
    public async Task StartInstance_ProcessFails_SetsCrashedStatus()
    {
        var instance = await SeedInstanceAsync();

        _processManager.StartServerProcessAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action<int, string>?>(), Arg.Any<Action<int, int>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Failed to start"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.StartInstanceAsync(instance.Id));

        var updated = await _db.ServerInstances.FindAsync(instance.Id);
        Assert.Equal(ServerInstanceStatus.Stopped, updated!.Status);
    }

    [Fact]
    public async Task StopInstance_ClearsProcessInfo()
    {
        var instance = await SeedInstanceAsync(status: ServerInstanceStatus.Running, processId: 42);

        _processManager.StopProcessAsync(42, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.StopInstanceAsync(instance.Id);

        var updated = await _db.ServerInstances.FindAsync(instance.Id);
        Assert.Equal(ServerInstanceStatus.Stopped, updated!.Status);
        Assert.Null(updated.ProcessId);
        Assert.Null(updated.StartedAt);
    }

    [Fact]
    public async Task StopInstance_NonExistentId_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.StopInstanceAsync(999));
    }

    [Fact]
    public async Task StopInstance_StopsHeadlessClientsFirst()
    {
        var instance = await SeedInstanceAsync(status: ServerInstanceStatus.Running, processId: 100);

        var hc = new HeadlessClient
        {
            ServerInstanceId = instance.Id,
            ProcessId = 200,
            Status = ServerInstanceStatus.Running
        };
        _db.HeadlessClients.Add(hc);
        await _db.SaveChangesAsync();

        _processManager.StopProcessAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.StopInstanceAsync(instance.Id);

        // HC stopped before server
        Received.InOrder(() =>
        {
            _processManager.StopProcessAsync(200, Arg.Any<CancellationToken>());
            _processManager.StopProcessAsync(100, Arg.Any<CancellationToken>());
        });
    }

    // ── Mod Management ──

    [Fact]
    public async Task AddModToInstance_CreatesLink()
    {
        var instance = await SeedInstanceAsync();
        var mod = new SteamMod { Name = "Mod", WorkshopId = 1 };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        await _sut.AddModToInstanceAsync(instance.Id, mod.Id, 5);

        var link = Assert.Single(_db.ServerInstanceMods);
        Assert.Equal(instance.Id, link.ServerInstanceId);
        Assert.Equal(mod.Id, link.SteamModId);
        Assert.Equal(5, link.LoadOrder);
    }

    [Fact]
    public async Task AddModToInstance_Duplicate_NoOp()
    {
        var instance = await SeedInstanceAsync();
        var mod = new SteamMod { Name = "Mod", WorkshopId = 1 };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        await _sut.AddModToInstanceAsync(instance.Id, mod.Id);
        await _sut.AddModToInstanceAsync(instance.Id, mod.Id);

        Assert.Single(_db.ServerInstanceMods);
    }

    [Fact]
    public async Task RemoveModFromInstance_DeletesLink()
    {
        var instance = await SeedInstanceAsync();
        var mod = new SteamMod { Name = "Mod", WorkshopId = 1 };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        _db.ServerInstanceMods.Add(new ServerInstanceMod
        {
            ServerInstanceId = instance.Id,
            SteamModId = mod.Id
        });
        await _db.SaveChangesAsync();

        await _sut.RemoveModFromInstanceAsync(instance.Id, mod.Id);

        Assert.Empty(_db.ServerInstanceMods);
    }

    [Fact]
    public async Task UpdateModLoadOrder_ChangesOrder()
    {
        var instance = await SeedInstanceAsync();
        var mod = new SteamMod { Name = "Mod", WorkshopId = 1 };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        _db.ServerInstanceMods.Add(new ServerInstanceMod
        {
            ServerInstanceId = instance.Id,
            SteamModId = mod.Id,
            LoadOrder = 0
        });
        await _db.SaveChangesAsync();

        await _sut.UpdateModLoadOrderAsync(instance.Id, mod.Id, 10);

        var link = _db.ServerInstanceMods.First();
        Assert.Equal(10, link.LoadOrder);
    }
}
