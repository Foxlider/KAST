using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services;
using KAST.Tests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace KAST.Tests.Services;

public class MissionHttpDownloadServiceTests : IDisposable
{
    private readonly Infrastructure.Data.KastDbContext _db;
    private readonly IMissionHashService _hashService;
    private readonly ILogger<MissionHttpDownloadService> _logger;
    private readonly MissionHttpDownloadService _sut;
    private bool _disposed;

    public MissionHttpDownloadServiceTests()
    {
        _db = DbHelper.CreateInMemoryDb();
        _hashService = Substitute.For<IMissionHashService>();
        _logger = Substitute.For<ILogger<MissionHttpDownloadService>>();
        _sut = new MissionHttpDownloadService(_db, _hashService, _logger);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _db.Dispose();
            _disposed = true;
        }
    }

    private async Task<Mission> SeedMission(int instanceId, string fileName, uint hash, string? physicalPath = null, bool writeFile = true)
    {
        var path = physicalPath ?? Path.GetTempFileName();
        if (writeFile)
            await File.WriteAllBytesAsync(path, new byte[] { 0x00, 0x50, 0x42, 0x4F });
        var mission = new Mission
        {
            ServerInstanceId = instanceId,
            FileName = fileName,
            DisplayName = "Test Mission",
            PhysicalPath = path,
            SizeBytes = 4,
            Hash = hash,
            UploadedAt = DateTime.UtcNow
        };
        _db.Missions.Add(mission);
        await _db.SaveChangesAsync();
        return mission;
    }

    [Fact]
    public async Task GetDownload_FoundMission_ReturnsResult()
    {
        var mission = await SeedMission(1, "test.Altis.pbo", 12345u);

        var result = await _sut.GetDownloadAsync(1, "test.Altis.pbo");

        Assert.NotNull(result);
        Assert.Equal(mission.PhysicalPath, result!.PhysicalPath);
        Assert.Equal(12345u, result.Hash);
        Assert.Equal(4, result.SizeBytes);
        Assert.Contains("1-test.Altis.pbo-12345-4", result.ETag);

        File.Delete(mission.PhysicalPath);
    }

    [Fact]
    public async Task GetDownload_WrongInstance_ReturnsNull()
    {
        await SeedMission(1, "test.pbo", 0u);

        var result = await _sut.GetDownloadAsync(2, "test.pbo");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDownload_FileMissing_ReturnsNull()
    {
        await SeedMission(1, "missing.pbo", 0u, "C:\\nonexistent\\missing.pbo", writeFile: false);

        var result = await _sut.GetDownloadAsync(1, "missing.pbo");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDownload_NullHash_LazyComputesAndStores()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, new byte[] { 0x00, 0x50, 0x42, 0x4F });
        var mission = new Mission
        {
            ServerInstanceId = 1,
            FileName = "lazy.pbo",
            DisplayName = "Lazy",
            PhysicalPath = path,
            SizeBytes = 4,
            Hash = null,
            UploadedAt = DateTime.UtcNow
        };
        _db.Missions.Add(mission);
        await _db.SaveChangesAsync();

        _hashService.ComputeHashAsync(path, Arg.Any<CancellationToken>()).Returns(54321u);

        var result = await _sut.GetDownloadAsync(1, "lazy.pbo");

        Assert.NotNull(result);
        Assert.Equal(54321u, result!.Hash);
        await _hashService.Received(1).ComputeHashAsync(path, Arg.Any<CancellationToken>());

        // Verify hash was persisted
        var updated = await _db.Missions.FindAsync(mission.Id);
        Assert.Equal(54321u, updated!.Hash);

        File.Delete(path);
    }

    [Fact]
    public async Task GetDownload_UnknownMission_ReturnsNull()
    {
        var result = await _sut.GetDownloadAsync(999, "nonexistent.pbo");
        Assert.Null(result);
    }
}
