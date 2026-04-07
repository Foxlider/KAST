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

public class ModServiceTests : IDisposable
{
    private readonly Infrastructure.Data.KastDbContext _db;
    private readonly ISteamService _steamService;
    private readonly IAppEventBroadcaster _broadcaster;
    private readonly ILogger<ModService> _logger;
    private readonly ModService _sut;

    public ModServiceTests()
    {
        _db = DbHelper.CreateInMemoryDb();
        _steamService = Substitute.For<ISteamService>();
        _broadcaster = Substitute.For<IAppEventBroadcaster>();
        _logger = Substitute.For<ILogger<ModService>>();
        _sut = new ModService(_db, _steamService, _broadcaster, _logger);
    }

    public void Dispose() => _db.Dispose();

    // ── AddWorkshopModAsync ──

    [Fact]
    public async Task AddWorkshopMod_NewMod_CreatesEntry()
    {
        _steamService.GetWorkshopItemInfoAsync(123456, Arg.Any<CancellationToken>())
            .Returns(new WorkshopItemInfo
            {
                WorkshopId = 123456,
                Name = "Test Mod",
                Description = "A test mod",
                Author = "Tester",
                SizeBytes = 1024,
                LastUpdated = DateTime.UtcNow
            });

        var mod = await _sut.AddWorkshopModAsync(123456);

        Assert.Equal(123456, mod.WorkshopId);
        Assert.Equal("Test Mod", mod.Name);
        Assert.Equal("Tester", mod.Author);
        Assert.Equal(ModSource.SteamWorkshop, mod.Source);
        Assert.Equal(ModStatus.NotInstalled, mod.Status);
        Assert.Equal(1024, mod.ExpectedSizeBytes);
    }

    [Fact]
    public async Task AddWorkshopMod_DuplicateId_ReturnsExisting()
    {
        _steamService.GetWorkshopItemInfoAsync(123456, Arg.Any<CancellationToken>())
            .Returns(new WorkshopItemInfo { WorkshopId = 123456, Name = "Mod" });

        var first = await _sut.AddWorkshopModAsync(123456);
        var second = await _sut.AddWorkshopModAsync(123456);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_db.Mods);
    }

    [Fact]
    public async Task AddWorkshopMod_SteamInfoNull_UsesFallbackName()
    {
        _steamService.GetWorkshopItemInfoAsync(999, Arg.Any<CancellationToken>())
            .Returns((WorkshopItemInfo?)null);

        var mod = await _sut.AddWorkshopModAsync(999);

        Assert.Equal("Workshop Item 999", mod.Name);
    }

    // ── ImportLocalModAsync ──

    [Fact]
    public async Task ImportLocalMod_Directory_SetsLocalFolderSource()
    {
        var mod = await _sut.ImportLocalModAsync("/some/path/mod", "Local Mod");

        Assert.Equal("Local Mod", mod.Name);
        Assert.Equal(ModSource.LocalFolder, mod.Source);
        Assert.Equal(ModStatus.Installed, mod.Status);
        Assert.Equal("/some/path/mod", mod.LocalPath);
    }

    [Fact]
    public async Task ImportLocalMod_ZipFile_SetsLocalZipSource()
    {
        var mod = await _sut.ImportLocalModAsync("/some/path/mod.zip", "Zipped Mod");

        Assert.Equal(ModSource.LocalZip, mod.Source);
    }

    // ── GetAllModsAsync ──

    [Fact]
    public async Task GetAllMods_ReturnsOrderedByName()
    {
        _db.Mods.Add(new SteamMod { Name = "Bravo Mod", WorkshopId = 2 });
        _db.Mods.Add(new SteamMod { Name = "Alpha Mod", WorkshopId = 1 });
        _db.Mods.Add(new SteamMod { Name = "Charlie Mod", WorkshopId = 3 });
        await _db.SaveChangesAsync();

        var mods = await _sut.GetAllModsAsync();

        Assert.Equal("Alpha Mod", mods[0].Name);
        Assert.Equal("Bravo Mod", mods[1].Name);
        Assert.Equal("Charlie Mod", mods[2].Name);
    }

    // ── GetModByIdAsync / GetModByWorkshopIdAsync ──

    [Fact]
    public async Task GetModById_ExistingId_ReturnsMod()
    {
        var mod = new SteamMod { Name = "Find Me", WorkshopId = 42 };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        var found = await _sut.GetModByIdAsync(mod.Id);

        Assert.NotNull(found);
        Assert.Equal("Find Me", found!.Name);
    }

    [Fact]
    public async Task GetModById_NonExistent_ReturnsNull()
    {
        var found = await _sut.GetModByIdAsync(999);
        Assert.Null(found);
    }

    [Fact]
    public async Task GetModByWorkshopId_ExistingId_ReturnsMod()
    {
        _db.Mods.Add(new SteamMod { Name = "WS Mod", WorkshopId = 12345 });
        await _db.SaveChangesAsync();

        var found = await _sut.GetModByWorkshopIdAsync(12345);

        Assert.NotNull(found);
        Assert.Equal(12345, found!.WorkshopId);
    }

    // ── DeleteModAsync ──

    [Fact]
    public async Task DeleteMod_RemovesFromDb()
    {
        var mod = new SteamMod { Name = "Delete Me", WorkshopId = 99 };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        await _sut.DeleteModAsync(mod.Id);

        Assert.Empty(_db.Mods);
    }

    [Fact]
    public async Task DeleteMod_NonExistentId_DoesNotThrow()
    {
        await _sut.DeleteModAsync(999);
    }

    // ── UpdateModAsync ──

    [Fact]
    public async Task UpdateMod_PersistsChanges()
    {
        var mod = new SteamMod { Name = "Original", WorkshopId = 1 };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        mod.Name = "Updated";
        await _sut.UpdateModAsync(mod);

        var updated = await _db.Mods.FindAsync(mod.Id);
        Assert.Equal("Updated", updated!.Name);
    }

    // ── DownloadModAsync ──

    [Fact]
    public async Task DownloadMod_NonExistentId_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DownloadModAsync(999));
    }

    [Fact]
    public async Task DownloadMod_SetsStatusToDownloading_ThenInstalled()
    {
        var mod = new SteamMod { Name = "DL Mod", WorkshopId = 555, ExpectedSizeBytes = 1000 };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        _steamService.DownloadWorkshopItemAsync(555, Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.DownloadModAsync(mod.Id);

        var updated = await _db.Mods.FindAsync(mod.Id);
        Assert.Equal(ModStatus.Installed, updated!.Status);
        Assert.NotNull(updated.LastUpdatedLocal);
    }

    [Fact]
    public async Task DownloadMod_BroadcastsStatusEvents()
    {
        var mod = new SteamMod { Name = "Broadcast Mod", WorkshopId = 777 };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        _steamService.DownloadWorkshopItemAsync(777, Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.DownloadModAsync(mod.Id);

        // Should broadcast at least twice: Downloading + Installed
        await _broadcaster.Received(2).BroadcastModStatusChangedAsync(Arg.Any<ModStatusChangedEvent>());
    }

    [Fact]
    public async Task DownloadMod_SteamFails_SetsErrorStatus()
    {
        var mod = new SteamMod { Name = "Fail Mod", WorkshopId = 888 };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        _steamService.DownloadWorkshopItemAsync(888, Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Steam CDN error"));

        await Assert.ThrowsAsync<Exception>(() => _sut.DownloadModAsync(mod.Id));

        var updated = await _db.Mods.FindAsync(mod.Id);
        Assert.Equal(ModStatus.Error, updated!.Status);
    }

    // ── CheckForUpdatesAsync ──

    [Fact]
    public async Task CheckForUpdates_NewerSteamVersion_SetsUpdateAvailable()
    {
        var mod = new SteamMod
        {
            Name = "Outdated Mod",
            WorkshopId = 111,
            Source = ModSource.SteamWorkshop,
            Status = ModStatus.Installed,
            LastUpdatedLocal = new DateTime(2025, 1, 1)
        };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        _steamService.GetWorkshopItemInfoAsync(111, Arg.Any<CancellationToken>())
            .Returns(new WorkshopItemInfo
            {
                WorkshopId = 111,
                LastUpdated = new DateTime(2025, 6, 1) // newer
            });

        await _sut.CheckForUpdatesAsync();

        var updated = await _db.Mods.FindAsync(mod.Id);
        Assert.Equal(ModStatus.UpdateAvailable, updated!.Status);
    }

    [Fact]
    public async Task CheckForUpdates_UpToDate_StatusUnchanged()
    {
        var mod = new SteamMod
        {
            Name = "Current Mod",
            WorkshopId = 222,
            Source = ModSource.SteamWorkshop,
            Status = ModStatus.Installed,
            LastUpdatedLocal = new DateTime(2025, 6, 1)
        };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();

        _steamService.GetWorkshopItemInfoAsync(222, Arg.Any<CancellationToken>())
            .Returns(new WorkshopItemInfo
            {
                WorkshopId = 222,
                LastUpdated = new DateTime(2025, 1, 1) // older
            });

        await _sut.CheckForUpdatesAsync();

        var updated = await _db.Mods.FindAsync(mod.Id);
        Assert.Equal(ModStatus.Installed, updated!.Status);
    }
}
