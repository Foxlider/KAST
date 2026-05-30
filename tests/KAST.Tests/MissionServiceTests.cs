using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services;
using KAST.Tests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace KAST.Tests;

public class MissionServiceTests : IDisposable
{
    private readonly Infrastructure.Data.KastDbContext _db;
    private readonly IServerInstanceService _serverInstanceService;
    private readonly ILogger<MissionService> _logger;
    private readonly MissionService _sut;
    private bool _disposed;

    public MissionServiceTests()
    {
        _db = DbHelper.CreateInMemoryDb();
        _serverInstanceService = Substitute.For<IServerInstanceService>();
        _logger = Substitute.For<ILogger<MissionService>>();
        _sut = new MissionService(_db, _serverInstanceService, _logger);
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

    private async Task<ServerInstance> SeedInstance(string name = "Test Server", string? installPath = null)
    {
        installPath ??= Path.Combine(Path.GetTempPath(), "kast-mission-tests", Guid.NewGuid().ToString("N"));
        var instance = new ServerInstance { Name = name, InstallPath = installPath };
        _db.ServerInstances.Add(instance);
        await _db.SaveChangesAsync();

        // Setup mpmissions directory for upload tests
        var mpmissionsDir = Path.Combine(installPath, "mpmissions");
        if (Directory.Exists(installPath))
            Directory.Delete(installPath, recursive: true);
        Directory.CreateDirectory(mpmissionsDir);

        _serverInstanceService.GetInstanceByIdAsync(instance.Id, Arg.Any<CancellationToken>())
            .Returns(instance);

        return instance;
    }

    private async Task<Mission> SeedMission(int instanceId, string fileName = "test.pbo")
    {
        var instance = await _db.ServerInstances.FindAsync(instanceId)
            ?? throw new InvalidOperationException($"Missing test instance {instanceId}");
        var missionPath = Path.Combine(instance.InstallPath, "mpmissions", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(missionPath)!);
        await File.WriteAllBytesAsync(missionPath, [0x00, 0x50, 0x42, 0x4F]);

        var mission = new Mission
        {
            ServerInstanceId = instanceId,
            FileName = fileName,
            DisplayName = Path.GetFileNameWithoutExtension(fileName),
            PhysicalPath = missionPath,
            SizeBytes = 4,
            UploadedAt = DateTime.UtcNow
        };
        _db.Missions.Add(mission);
        await _db.SaveChangesAsync();
        return mission;
    }

    private async Task<MissionTag> SeedTag(int instanceId, string name = "cool-mission")
    {
        var tag = new MissionTag { ServerInstanceId = instanceId, Name = name };
        _db.MissionTags.Add(tag);
        await _db.SaveChangesAsync();
        return tag;
    }

    // ── GetMissionsForInstanceAsync ──

    [Fact]
    public async Task GetMissions_ReturnsEmptyWhenNone()
    {
        var instance = await SeedInstance();
        var result = await _sut.GetMissionsForInstanceAsync(instance.Id);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMissions_IncludesTagsAndPreset()
    {
        var instance = await SeedInstance();
        var mission = await SeedMission(instance.Id);
        var tag = await SeedTag(instance.Id, "cool");

        _db.MissionTagAssignments.Add(new MissionTagAssignment { MissionId = mission.Id, MissionTagId = tag.Id });
        await _db.SaveChangesAsync();

        var result = await _sut.GetMissionsForInstanceAsync(instance.Id);

        Assert.Single(result);
        Assert.Single(result[0].TagAssignments);
        Assert.Equal("cool", result[0].TagAssignments.First().Tag!.Name);
    }

    [Fact]
    public async Task GetMissions_OnlyReturnsForSpecifiedInstance()
    {
        var inst1 = await SeedInstance("Server 1");
        var inst2 = await SeedInstance("Server 2");
        await SeedMission(inst1.Id, "m1.pbo");
        await SeedMission(inst2.Id, "m2.pbo");

        var result = await _sut.GetMissionsForInstanceAsync(inst1.Id);

        Assert.Single(result);
        Assert.Equal("m1.pbo", result[0].FileName);
    }

    // ── UploadMissionAsync ──

    [Fact]
    public async Task UploadMission_WritesFileAndCreatesRecord()
    {
        var instance = await SeedInstance("UploadServer", "/tmp/servers/upload-test");

        using var stream = new MemoryStream(new byte[] { 0x00, 0x50, 0x42, 0x4F }); // mock PBO
        var mission = await _sut.UploadMissionAsync(instance.Id, "co40_escape.tanoa.pbo", stream);

        Assert.Equal("co40_escape.tanoa.pbo", mission.FileName);
        Assert.Equal("co40_escape", mission.DisplayName);
        Assert.Equal("tanoa", mission.MapName);
        Assert.Equal(4, mission.SizeBytes);
        Assert.True(File.Exists(mission.PhysicalPath));

        // Cleanup
        File.Delete(mission.PhysicalPath);
    }

    [Fact]
    public async Task UploadMission_NoTerrainInFilename_UsesEmptyMapName()
    {
        var instance = await SeedInstance("PlainServer", "/tmp/servers/plain-test");

        using var stream = new MemoryStream(new byte[] { 0x00, 0x50, 0x42, 0x4F });
        var mission = await _sut.UploadMissionAsync(instance.Id, "simple_mission.pbo", stream);

        Assert.Equal("simple_mission", mission.DisplayName);
        Assert.Equal("", mission.MapName);

        File.Delete(mission.PhysicalPath);
    }

    [Fact]
    public async Task UploadMission_DuplicateFilename_ReplacesExistingFileAndRecord()
    {
        var instance = await SeedInstance("DupServer", "/tmp/servers/dup-test");
        var mpmissionsDir = Path.Combine(instance.InstallPath, "mpmissions");
        Directory.CreateDirectory(mpmissionsDir);

        // Pre-create a file with the same name
        var existingPath = Path.Combine(mpmissionsDir, "mission.pbo");
        File.WriteAllText(existingPath, "existing");

        var existingMission = new Mission
        {
            ServerInstanceId = instance.Id,
            FileName = "mission.pbo",
            DisplayName = "Existing Mission",
            MapName = "",
            PhysicalPath = existingPath,
            SizeBytes = 8,
            UploadedAt = DateTime.UtcNow.AddDays(-1)
        };
        _db.Missions.Add(existingMission);
        await _db.SaveChangesAsync();

        using var stream = new MemoryStream(new byte[] { 0x00, 0x01 });
        var mission = await _sut.UploadMissionAsync(instance.Id, "mission.pbo", stream);

        Assert.Equal(existingMission.Id, mission.Id);
        Assert.Equal("mission.pbo", mission.FileName);
        Assert.Equal("Existing Mission", mission.DisplayName);
        Assert.Equal(2, mission.SizeBytes);
        Assert.Single(_db.Missions.Where(m => m.ServerInstanceId == instance.Id));
        Assert.Equal(new byte[] { 0x00, 0x01 }, await File.ReadAllBytesAsync(existingPath));

        // Cleanup
        File.Delete(existingPath);
    }

    [Fact]
    public async Task UploadMission_InstanceNotFound_Throws()
    {
        using var stream = new MemoryStream(new byte[] { 0x00 });
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UploadMissionAsync(999, "test.pbo", stream));
    }

    // ── UpdateMissionAsync ──

    [Fact]
    public async Task UpdateMission_ChangesMetadata()
    {
        var instance = await SeedInstance();
        var mission = await SeedMission(instance.Id);

        mission.DisplayName = "Operation Arrowhead";
        mission.MapName = "Tanoa";
        mission.ModPresetId = null;

        var updated = await _sut.UpdateMissionAsync(mission);

        Assert.Equal("Operation Arrowhead", updated.DisplayName);
        Assert.Equal("Tanoa", updated.MapName);
    }

    [Fact]
    public async Task UpdateMission_NotFound_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateMissionAsync(new Mission { Id = 999 }));
    }

    // ── DeleteMissionAsync ──

    [Fact]
    public async Task DeleteMission_RemovesFromDb()
    {
        var instance = await SeedInstance();
        var mission = await SeedMission(instance.Id);

        await _sut.DeleteMissionAsync(mission.Id);

        Assert.Empty(_db.Missions);
    }

    [Fact]
    public async Task DeleteMission_NotFound_DoesNotThrow()
    {
        var exception = await Record.ExceptionAsync(() => _sut.DeleteMissionAsync(999));
        Assert.Null(exception);
    }

    // ── Tags: CreateTagAsync ──

    [Fact]
    public async Task CreateTag_StripsHashPrefix()
    {
        var instance = await SeedInstance();
        var tag = await _sut.CreateTagAsync(instance.Id, "#zeus-missions");

        Assert.Equal("zeus-missions", tag.Name);
    }

    [Fact]
    public async Task CreateTag_DuplicateName_ReturnsExisting()
    {
        var instance = await SeedInstance();
        var tag1 = await _sut.CreateTagAsync(instance.Id, "pvp");
        var tag2 = await _sut.CreateTagAsync(instance.Id, "pvp");

        Assert.Equal(tag1.Id, tag2.Id);
        Assert.Single(_db.MissionTags);
    }

    [Fact]
    public async Task CreateTag_SameNameDifferentInstances_AreSeparate()
    {
        var inst1 = await SeedInstance("S1");
        var inst2 = await SeedInstance("S2");

        var tag1 = await _sut.CreateTagAsync(inst1.Id, "shared");
        var tag2 = await _sut.CreateTagAsync(inst2.Id, "shared");

        Assert.NotEqual(tag1.Id, tag2.Id);
        Assert.Equal(2, _db.MissionTags.Count());
    }

    // ── Tags: AssignTagAsync / RemoveTagAsync ──

    [Fact]
    public async Task AssignTag_CreatesAssignment()
    {
        var instance = await SeedInstance();
        var mission = await SeedMission(instance.Id);
        var tag = await SeedTag(instance.Id, "pve");

        await _sut.AssignTagAsync(mission.Id, tag.Id);

        Assert.Single(_db.MissionTagAssignments);
    }

    [Fact]
    public async Task AssignTag_Duplicate_DoesNotThrow()
    {
        var instance = await SeedInstance();
        var mission = await SeedMission(instance.Id);
        var tag = await SeedTag(instance.Id);

        await _sut.AssignTagAsync(mission.Id, tag.Id);
        await _sut.AssignTagAsync(mission.Id, tag.Id);

        Assert.Single(_db.MissionTagAssignments);
    }

    [Fact]
    public async Task RemoveTag_RemovesAssignment()
    {
        var instance = await SeedInstance();
        var mission = await SeedMission(instance.Id);
        var tag = await SeedTag(instance.Id);

        await _sut.AssignTagAsync(mission.Id, tag.Id);
        await _sut.RemoveTagAsync(mission.Id, tag.Id);

        Assert.Empty(_db.MissionTagAssignments);
    }

    // ── DeleteTagAsync ──

    [Fact]
    public async Task DeleteTag_CascadesToAssignments()
    {
        var instance = await SeedInstance();
        var mission = await SeedMission(instance.Id);
        var tag = await SeedTag(instance.Id);

        _db.MissionTagAssignments.Add(new MissionTagAssignment { MissionId = mission.Id, MissionTagId = tag.Id });
        await _db.SaveChangesAsync();

        await _sut.DeleteTagAsync(tag.Id);

        Assert.Empty(_db.MissionTags);
        Assert.Empty(_db.MissionTagAssignments);
    }

    // ── Campaigns ──

    [Fact]
    public async Task CreateCampaign_PersistsToDb()
    {
        var instance = await SeedInstance();
        var campaign = new Campaign
        {
            ServerInstanceId = instance.Id,
            Title = "Eastern Front",
            Description = "A series of missions on the eastern front"
        };

        var created = await _sut.CreateCampaignAsync(campaign);

        Assert.NotEqual(0, created.Id);
        Assert.Equal("Eastern Front", created.Title);
        Assert.Contains("eastern front", created.Description);
    }

    [Fact]
    public async Task GetCampaignsForInstance_ReturnsOrdered()
    {
        var instance = await SeedInstance();
        _db.Campaigns.Add(new Campaign { ServerInstanceId = instance.Id, Title = "Old", CreatedAt = DateTime.UtcNow.AddDays(-10) });
        _db.Campaigns.Add(new Campaign { ServerInstanceId = instance.Id, Title = "New", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _sut.GetCampaignsForInstanceAsync(instance.Id);
        Assert.Equal("New", result[0].Title);
        Assert.Equal("Old", result[1].Title);
    }

    [Fact]
    public async Task GetCampaignById_IncludesOrderedMissions()
    {
        var instance = await SeedInstance();
        var mission1 = await SeedMission(instance.Id, "m1.pbo");
        var mission2 = await SeedMission(instance.Id, "m2.pbo");

        var campaign = new Campaign { ServerInstanceId = instance.Id, Title = "Ordered Campaign" };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        _db.CampaignMissions.Add(new CampaignMission { CampaignId = campaign.Id, MissionId = mission1.Id, OrderIndex = 1 });
        _db.CampaignMissions.Add(new CampaignMission { CampaignId = campaign.Id, MissionId = mission2.Id, OrderIndex = 0 });
        await _db.SaveChangesAsync();

        var result = await _sut.GetCampaignByIdAsync(campaign.Id);

        Assert.NotNull(result);
        Assert.Equal(2, result!.CampaignMissions.Count);
        Assert.Equal(mission2.Id, result.CampaignMissions.First().MissionId); // order 0 first
        Assert.Equal(mission1.Id, result.CampaignMissions.Last().MissionId);  // order 1 second
    }

    [Fact]
    public async Task UpdateCampaign_ChangesTitleAndDescription()
    {
        var instance = await SeedInstance();
        var campaign = new Campaign { ServerInstanceId = instance.Id, Title = "Original" };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        campaign.Title = "Renamed Campaign";
        campaign.Description = "Updated desc";
        var updated = await _sut.UpdateCampaignAsync(campaign);

        Assert.Equal("Renamed Campaign", updated.Title);
        Assert.Equal("Updated desc", updated.Description);
    }

    [Fact]
    public async Task AddMissionToCampaign_Duplicate_DoesNotThrow()
    {
        var instance = await SeedInstance();
        var mission = await SeedMission(instance.Id);
        var campaign = new Campaign { ServerInstanceId = instance.Id, Title = "Dup Test" };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        await _sut.AddMissionToCampaignAsync(campaign.Id, mission.Id, 0);
        await _sut.AddMissionToCampaignAsync(campaign.Id, mission.Id, 1);

        Assert.Single(_db.CampaignMissions);
    }

    [Fact]
    public async Task ReorderCampaignMissions_UpdatesOrderIndexes()
    {
        var instance = await SeedInstance();
        var m1 = await SeedMission(instance.Id, "a.pbo");
        var m2 = await SeedMission(instance.Id, "b.pbo");
        var m3 = await SeedMission(instance.Id, "c.pbo");

        var campaign = new Campaign { ServerInstanceId = instance.Id, Title = "Reorder" };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        _db.CampaignMissions.Add(new CampaignMission { CampaignId = campaign.Id, MissionId = m1.Id, OrderIndex = 0 });
        _db.CampaignMissions.Add(new CampaignMission { CampaignId = campaign.Id, MissionId = m2.Id, OrderIndex = 1 });
        _db.CampaignMissions.Add(new CampaignMission { CampaignId = campaign.Id, MissionId = m3.Id, OrderIndex = 2 });
        await _db.SaveChangesAsync();

        // Reverse order: m3, m2, m1
        await _sut.ReorderCampaignMissionsAsync(campaign.Id, new List<int> { m3.Id, m2.Id, m1.Id });

        var missions = _db.CampaignMissions.Where(cm => cm.CampaignId == campaign.Id).OrderBy(cm => cm.OrderIndex).ToList();
        Assert.Equal(m3.Id, missions[0].MissionId);
        Assert.Equal(0, missions[0].OrderIndex);
        Assert.Equal(m2.Id, missions[1].MissionId);
        Assert.Equal(1, missions[1].OrderIndex);
        Assert.Equal(m1.Id, missions[2].MissionId);
        Assert.Equal(2, missions[2].OrderIndex);
    }

    [Fact]
    public async Task DeleteCampaign_CascadesToMissionLinks()
    {
        var instance = await SeedInstance();
        var mission = await SeedMission(instance.Id);
        var campaign = new Campaign { ServerInstanceId = instance.Id, Title = "Cascade" };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        _db.CampaignMissions.Add(new CampaignMission { CampaignId = campaign.Id, MissionId = mission.Id, OrderIndex = 0 });
        await _db.SaveChangesAsync();

        await _sut.DeleteCampaignAsync(campaign.Id);

        Assert.Empty(_db.Campaigns);
        Assert.Empty(_db.CampaignMissions);
    }

    // ── Sets ──

    [Fact]
    public async Task CreateSet_PersistsToDb()
    {
        var instance = await SeedInstance();
        var set = new Set
        {
            ServerInstanceId = instance.Id,
            Title = "Weekend Ops",
            Description = "Collection of weekend operations"
        };

        var created = await _sut.CreateSetAsync(set);

        Assert.NotEqual(0, created.Id);
        Assert.Equal("Weekend Ops", created.Title);
    }

    [Fact]
    public async Task GetSetsForInstance_ReturnsOrderedByCreatedDescending()
    {
        var instance = await SeedInstance();
        _db.Sets.Add(new Set { ServerInstanceId = instance.Id, Title = "Old Set", CreatedAt = DateTime.UtcNow.AddDays(-5) });
        _db.Sets.Add(new Set { ServerInstanceId = instance.Id, Title = "New Set", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _sut.GetSetsForInstanceAsync(instance.Id);

        Assert.Equal("New Set", result[0].Title);
        Assert.Equal("Old Set", result[1].Title);
    }

    [Fact]
    public async Task AddMissionToSet_Duplicate_DoesNotThrow()
    {
        var instance = await SeedInstance();
        var mission = await SeedMission(instance.Id);
        var set = new Set { ServerInstanceId = instance.Id, Title = "Dup Set" };
        _db.Sets.Add(set);
        await _db.SaveChangesAsync();

        await _sut.AddMissionToSetAsync(set.Id, mission.Id);
        await _sut.AddMissionToSetAsync(set.Id, mission.Id);

        Assert.Single(_db.SetMissions);
    }

    [Fact]
    public async Task DeleteSet_CascadesToMissionLinks()
    {
        var instance = await SeedInstance();
        var mission = await SeedMission(instance.Id);
        var set = new Set { ServerInstanceId = instance.Id, Title = "Cascade Set" };
        _db.Sets.Add(set);
        await _db.SaveChangesAsync();

        _db.SetMissions.Add(new SetMission { SetId = set.Id, MissionId = mission.Id });
        await _db.SaveChangesAsync();

        await _sut.DeleteSetAsync(set.Id);

        Assert.Empty(_db.SetMissions);
    }

    // ── Search ──

    [Fact]
    public async Task SearchMissions_ByName_FindsMatch()
    {
        var instance = await SeedInstance();
        var m1 = await SeedMission(instance.Id, "co40_tanoa.pbo");
        var m2 = await SeedMission(instance.Id, "co10_stratis.pbo");
        m1.DisplayName = "Operation Tanoa";
        m2.DisplayName = "Stratis Patrol";
        await _db.SaveChangesAsync();

        var result = await _sut.SearchMissionsAsync(instance.Id, "tanoa", null, null, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Operation Tanoa", result[0].DisplayName);
    }

    [Fact]
    public async Task SearchMissions_ByTag_FindsMatch()
    {
        var instance = await SeedInstance();
        var mission = await SeedMission(instance.Id, "zeus_mission.pbo");
        var tag = await SeedTag(instance.Id, "zeus");

        _db.MissionTagAssignments.Add(new MissionTagAssignment { MissionId = mission.Id, MissionTagId = tag.Id });
        await _db.SaveChangesAsync();

        var result = await _sut.SearchMissionsAsync(instance.Id, null, new List<int> { tag.Id }, null);

        Assert.Single(result);
        Assert.Equal("zeus_mission.pbo", result[0].FileName);
    }

    [Fact]
    public async Task SearchMissions_ByMapName_FindsExactMatch()
    {
        var instance = await SeedInstance();
        var m1 = await SeedMission(instance.Id, "a.pbo");
        var m2 = await SeedMission(instance.Id, "b.pbo");
        m1.MapName = "Altis";
        m2.MapName = "Tanoa";
        await _db.SaveChangesAsync();

        var result = await _sut.SearchMissionsAsync(instance.Id, null, null, "Altis");

        Assert.Single(result);
        Assert.Equal("Altis", result[0].MapName);
    }

    [Fact]
    public async Task SearchMissions_CombinedFilters_FindsIntersection()
    {
        var instance = await SeedInstance();
        var tag = await SeedTag(instance.Id, "night");

        var m1 = await SeedMission(instance.Id, "match.pbo");
        var m2 = await SeedMission(instance.Id, "nomatch.pbo");
        m1.DisplayName = "Night Patrol";
        m1.MapName = "Altis";
        m2.DisplayName = "Night Raid";
        m2.MapName = "Tanoa";
        await _db.SaveChangesAsync();

        _db.MissionTagAssignments.Add(new MissionTagAssignment { MissionId = m1.Id, MissionTagId = tag.Id });
        await _db.SaveChangesAsync();

        // Search by tag + map
        var result = await _sut.SearchMissionsAsync(instance.Id, null, new List<int> { tag.Id }, "Altis");

        Assert.Single(result);
        Assert.Equal("Night Patrol", result[0].DisplayName);
    }
}
