using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services;
using KAST.Tests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace KAST.Tests;

public class ModPresetServiceTests : IDisposable
{
    private readonly Infrastructure.Data.KastDbContext _db;
    private readonly IModService _modService;
    private readonly IServerInstanceService _serverInstanceService;
    private readonly ILogger<ModPresetService> _logger;
    private readonly ModPresetService _sut;
    private bool _disposed;

    public ModPresetServiceTests()
    {
        _db = DbHelper.CreateInMemoryDb();
        _modService = Substitute.For<IModService>();
        _serverInstanceService = Substitute.For<IServerInstanceService>();
        _logger = Substitute.For<ILogger<ModPresetService>>();
        _sut = new ModPresetService(_db, _modService, _serverInstanceService, _logger);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _db.Dispose();
            _disposed = true;
        }
    }

    // ── Helpers ──

    private async Task<ServerInstance> SeedInstance(string name = "Test Server")
    {
        var instance = new ServerInstance { Name = name, InstallPath = $"/servers/{name}" };
        _db.ServerInstances.Add(instance);
        await _db.SaveChangesAsync();
        return instance;
    }

    private async Task<SteamMod> SeedMod(long workshopId = 12345, string name = "Test Mod")
    {
        var mod = new SteamMod { WorkshopId = workshopId, Name = name };
        _db.Mods.Add(mod);
        await _db.SaveChangesAsync();
        return mod;
    }

    // ── GetPresetsForInstanceAsync ──

    [Fact]
    public async Task GetPresets_ReturnsEmptyWhenNone()
    {
        var instance = await SeedInstance();
        var result = await _sut.GetPresetsForInstanceAsync(instance.Id);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPresets_ReturnsOrderedByCreatedAtDescending()
    {
        var instance = await SeedInstance();
        _db.ModPresets.Add(new ModPreset { ServerInstanceId = instance.Id, Name = "Older", CreatedAt = DateTime.UtcNow.AddDays(-2) });
        _db.ModPresets.Add(new ModPreset { ServerInstanceId = instance.Id, Name = "Newer", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _sut.GetPresetsForInstanceAsync(instance.Id);

        Assert.Equal(2, result.Count);
        Assert.Equal("Newer", result[0].Name);
        Assert.Equal("Older", result[1].Name);
    }

    [Fact]
    public async Task GetPresets_OnlyReturnsForSpecifiedInstance()
    {
        var inst1 = await SeedInstance("Server 1");
        var inst2 = await SeedInstance("Server 2");
        _db.ModPresets.Add(new ModPreset { ServerInstanceId = inst1.Id, Name = "P1" });
        _db.ModPresets.Add(new ModPreset { ServerInstanceId = inst2.Id, Name = "P2" });
        await _db.SaveChangesAsync();

        var result = await _sut.GetPresetsForInstanceAsync(inst1.Id);

        Assert.Single(result);
        Assert.Equal("P1", result[0].Name);
    }

    // ── GetPresetByIdAsync ──

    [Fact]
    public async Task GetPresetById_IncludesEntriesAndMods()
    {
        var instance = await SeedInstance();
        var mod = await SeedMod();
        var preset = new ModPreset { ServerInstanceId = instance.Id, Name = "Test" };
        _db.ModPresets.Add(preset);
        await _db.SaveChangesAsync();

        _db.ModPresetEntries.Add(new ModPresetEntry
        {
            ModPresetId = preset.Id,
            SteamModId = mod.Id,
            IsClientSide = true,
            LoadOrder = 0
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetPresetByIdAsync(preset.Id);

        Assert.NotNull(result);
        Assert.Equal("Test", result!.Name);
        Assert.Single(result.Entries);
        Assert.True(result.Entries.First().IsClientSide);
    }

    [Fact]
    public async Task GetPresetById_NotFound_ReturnsNull()
    {
        var result = await _sut.GetPresetByIdAsync(999);
        Assert.Null(result);
    }

    // ── CreatePresetAsync ──

    [Fact]
    public async Task CreatePreset_PersistsToDb()
    {
        var instance = await SeedInstance();
        var preset = new ModPreset { ServerInstanceId = instance.Id, Name = "My Preset", Type = ModPresetType.Kast };

        var created = await _sut.CreatePresetAsync(preset);

        Assert.NotEqual(0, created.Id);
        var fromDb = await _db.ModPresets.FindAsync(created.Id);
        Assert.Equal("My Preset", fromDb!.Name);
        Assert.Equal(ModPresetType.Kast, fromDb.Type);
    }

    // ── UpdatePresetAsync ──

    [Fact]
    public async Task UpdatePreset_ChangesName()
    {
        var instance = await SeedInstance();
        var preset = new ModPreset { ServerInstanceId = instance.Id, Name = "Original" };
        _db.ModPresets.Add(preset);
        await _db.SaveChangesAsync();

        preset.Name = "Renamed";
        var updated = await _sut.UpdatePresetAsync(preset);

        Assert.Equal("Renamed", updated.Name);
        var fromDb = await _db.ModPresets.FindAsync(preset.Id);
        Assert.Equal("Renamed", fromDb!.Name);
    }

    [Fact]
    public async Task UpdatePreset_NotFound_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdatePresetAsync(new ModPreset { Id = 999, Name = "Ghost" }));
    }

    // ── DeletePresetAsync ──

    [Fact]
    public async Task DeletePreset_RemovesFromDb()
    {
        var instance = await SeedInstance();
        var preset = new ModPreset { ServerInstanceId = instance.Id, Name = "Delete Me" };
        _db.ModPresets.Add(preset);
        await _db.SaveChangesAsync();

        await _sut.DeletePresetAsync(preset.Id);

        Assert.Empty(_db.ModPresets);
    }

    [Fact]
    public async Task DeletePreset_CascadesToEntries()
    {
        var instance = await SeedInstance();
        var mod = await SeedMod();
        var preset = new ModPreset { ServerInstanceId = instance.Id, Name = "Cascade Test" };
        _db.ModPresets.Add(preset);
        await _db.SaveChangesAsync();

        _db.ModPresetEntries.Add(new ModPresetEntry { ModPresetId = preset.Id, SteamModId = mod.Id });
        await _db.SaveChangesAsync();

        await _sut.DeletePresetAsync(preset.Id);

        Assert.Empty(_db.ModPresetEntries);
    }

    [Fact]
    public async Task DeletePreset_NotFound_DoesNotThrow()
    {
        var exception = await Record.ExceptionAsync(() => _sut.DeletePresetAsync(999));
        Assert.Null(exception);
    }

    // ── SaveInstanceAsKastPresetAsync ──

    [Fact]
    public async Task SaveInstanceAsKastPreset_SnapshotsAllMods()
    {
        var instance = await SeedInstance();
        var mod1 = await SeedMod(111, "Mod A");
        var mod2 = await SeedMod(222, "Mod B");
        var mod3 = await SeedMod(333, "Mod C");

        _db.ServerInstanceMods.Add(new ServerInstanceMod
        {
            ServerInstanceId = instance.Id, SteamModId = mod1.Id, IsClientSide = true, LoadOrder = 0
        });
        _db.ServerInstanceMods.Add(new ServerInstanceMod
        {
            ServerInstanceId = instance.Id, SteamModId = mod2.Id, IsServerSide = true, LoadOrder = 0
        });
        _db.ServerInstanceMods.Add(new ServerInstanceMod
        {
            ServerInstanceId = instance.Id, SteamModId = mod3.Id, IsClientSide = false, IsServerSide = false, LoadOrder = 0
        });
        await _db.SaveChangesAsync();

        var preset = await _sut.SaveInstanceAsKastPresetAsync(instance.Id, "Full Layout");

        Assert.Equal("Full Layout", preset.Name);
        Assert.Equal(ModPresetType.Kast, preset.Type);
        Assert.Equal(3, preset.Entries.Count);

        var entries = preset.Entries.ToList();
        Assert.True(entries[0].IsClientSide);
        Assert.True(entries[1].IsServerSide);
        Assert.False(entries[2].IsClientSide);
        Assert.False(entries[2].IsServerSide); // whitelisted
    }

    [Fact]
    public async Task SaveInstanceAsKastPreset_EmptyInstance_ReturnsEmptyPreset()
    {
        var instance = await SeedInstance();
        var preset = await _sut.SaveInstanceAsKastPresetAsync(instance.Id, "Empty");
        Assert.Empty(preset.Entries);
    }

    // ── ImportFromArmaHtmlAsync ──

    [Fact]
    public async Task ImportFromArmaHtml_CreatesArmaPreset()
    {
        var instance = await SeedInstance();
        var mod = await SeedMod(123456, "ACE");

        _modService.GetModByWorkshopIdAsync(123456, Arg.Any<CancellationToken>())
            .Returns(mod);

        var html = @"<html><body><table>
            <tr data-type=""ModContainer"">
                <td data-type=""DisplayName"">@ACE</td>
                <td><a href=""?id=123456"">link</a></td>
            </tr>
        </table></body></html>";

        var preset = await _sut.ImportFromArmaHtmlAsync(instance.Id, "My Arma Preset", html);

        Assert.Equal("My Arma Preset", preset.Name);
        Assert.Equal(ModPresetType.Arma, preset.Type);
        Assert.Equal(html, preset.RawHtmlContent);
        Assert.Single(preset.Entries);
        Assert.True(preset.Entries.First().IsClientSide);
    }

    [Fact]
    public async Task ImportFromArmaHtml_UnresolvableMod_Skips()
    {
        var instance = await SeedInstance();

        _modService.GetModByWorkshopIdAsync(999999, Arg.Any<CancellationToken>())
            .Returns((SteamMod?)null);
        _modService.AddWorkshopModAsync(999999, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SteamMod>(new Exception("Not found")));

        var html = @"<tr data-type=""ModContainer""><td><a href=""?id=999999"">link</a></td></tr>";
        var preset = await _sut.ImportFromArmaHtmlAsync(instance.Id, "Bad Import", html);

        Assert.Empty(preset.Entries);
    }

    [Fact]
    public async Task ImportFromArmaHtml_AutoResolvesNewMod()
    {
        var instance = await SeedInstance();
        var autoMod = await SeedMod(555, "Auto Resolved");

        _modService.GetModByWorkshopIdAsync(555, Arg.Any<CancellationToken>())
            .Returns(autoMod);

        var html = @"<tr data-type=""ModContainer""><td><a href=""?id=555"">link</a></td></tr>";
        var preset = await _sut.ImportFromArmaHtmlAsync(instance.Id, "Auto", html);

        Assert.Single(preset.Entries);
        await _modService.Received(1).GetModByWorkshopIdAsync(555, Arg.Any<CancellationToken>());
    }

    // ── ApplyPresetAsync ──

    [Fact]
    public async Task ApplyPreset_ReplacesAllModAssignments()
    {
        var instance = await SeedInstance();
        var oldMod = await SeedMod(111, "Old Mod");
        var newMod1 = await SeedMod(222, "New Mod A");
        var newMod2 = await SeedMod(333, "New Mod B");

        // Existing assignment
        _db.ServerInstanceMods.Add(new ServerInstanceMod
        {
            ServerInstanceId = instance.Id, SteamModId = oldMod.Id, IsClientSide = true, LoadOrder = 0
        });
        await _db.SaveChangesAsync();

        // Create preset with new config
        var preset = new ModPreset { ServerInstanceId = instance.Id, Name = "Replacement", Type = ModPresetType.Kast };
        _db.ModPresets.Add(preset);
        await _db.SaveChangesAsync();

        _db.ModPresetEntries.Add(new ModPresetEntry { ModPresetId = preset.Id, SteamModId = newMod1.Id, IsClientSide = true, LoadOrder = 0 });
        _db.ModPresetEntries.Add(new ModPresetEntry { ModPresetId = preset.Id, SteamModId = newMod2.Id, IsServerSide = true, LoadOrder = 0 });
        await _db.SaveChangesAsync();

        await _sut.ApplyPresetAsync(preset.Id, instance.Id);

        var assignments = _db.ServerInstanceMods.Where(m => m.ServerInstanceId == instance.Id).ToList();
        Assert.Equal(2, assignments.Count);
        Assert.Contains(assignments, a => a.SteamModId == newMod1.Id && a.IsClientSide);
        Assert.Contains(assignments, a => a.SteamModId == newMod2.Id && a.IsServerSide);
        Assert.DoesNotContain(assignments, a => a.SteamModId == oldMod.Id);
    }

    [Fact]
    public async Task ApplyPreset_UpdatesLastAppliedAt()
    {
        var instance = await SeedInstance();
        var preset = new ModPreset { ServerInstanceId = instance.Id, Name = "Time Test" };
        _db.ModPresets.Add(preset);
        await _db.SaveChangesAsync();

        Assert.Null(preset.LastAppliedAt);

        await _sut.ApplyPresetAsync(preset.Id, instance.Id);

        Assert.NotNull(preset.LastAppliedAt);
    }

    [Fact]
    public async Task ApplyPreset_SkipsDeletedMods()
    {
        var instance = await SeedInstance();
        var preset = new ModPreset { ServerInstanceId = instance.Id, Name = "Ghost Preset" };
        _db.ModPresets.Add(preset);
        await _db.SaveChangesAsync();

        // Entry references a mod that doesn't exist
        _db.ModPresetEntries.Add(new ModPresetEntry { ModPresetId = preset.Id, SteamModId = 99999, IsClientSide = true });
        await _db.SaveChangesAsync();

        // Should not throw
        await _sut.ApplyPresetAsync(preset.Id, instance.Id);

        // No assignments created because mod didn't exist
        var assignments = _db.ServerInstanceMods.Where(m => m.ServerInstanceId == instance.Id).ToList();
        Assert.Empty(assignments);
    }

    [Fact]
    public async Task ApplyPreset_NotFound_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ApplyPresetAsync(999, 1));
    }

    [Fact]
    public async Task ApplyPreset_PreservesFourColumnLayout()
    {
        var instance = await SeedInstance();
        var mod1 = await SeedMod(1, "Client");
        var mod2 = await SeedMod(2, "Server");
        var mod3 = await SeedMod(3, "Whitelisted");
        var mod4 = await SeedMod(4, "Client2");

        var preset = new ModPreset { ServerInstanceId = instance.Id, Name = "Full Layout" };
        _db.ModPresets.Add(preset);
        await _db.SaveChangesAsync();

        _db.ModPresetEntries.Add(new ModPresetEntry { ModPresetId = preset.Id, SteamModId = mod1.Id, IsClientSide = true, LoadOrder = 0 });
        _db.ModPresetEntries.Add(new ModPresetEntry { ModPresetId = preset.Id, SteamModId = mod4.Id, IsClientSide = true, LoadOrder = 1 });
        _db.ModPresetEntries.Add(new ModPresetEntry { ModPresetId = preset.Id, SteamModId = mod2.Id, IsServerSide = true, LoadOrder = 0 });
        _db.ModPresetEntries.Add(new ModPresetEntry { ModPresetId = preset.Id, SteamModId = mod3.Id, LoadOrder = 0 }); // whitelisted
        await _db.SaveChangesAsync();

        await _sut.ApplyPresetAsync(preset.Id, instance.Id);

        var mods = _db.ServerInstanceMods.Where(m => m.ServerInstanceId == instance.Id).ToList();
        Assert.Equal(4, mods.Count);
        Assert.Equal(2, mods.Count(m => m.IsClientSide));
        Assert.Single(mods.Where(m => m.IsServerSide));
        Assert.Single(mods.Where(m => !m.IsClientSide && !m.IsServerSide));
    }
}
