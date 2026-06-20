using KAST.Core.Interfaces;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public class MissionHttpDownloadService(
    KastDbContext db,
    IMissionHashService hashService,
    ILogger<MissionHttpDownloadService> logger) : IMissionHttpDownloadService
{
    public async Task<MissionDownloadResult?> GetDownloadAsync(int instanceId, string fileName, CancellationToken ct = default)
    {
        var mission = await db.Missions
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerInstanceId == instanceId && m.FileName == fileName, ct);

        if (mission == null || string.IsNullOrEmpty(mission.PhysicalPath) || !File.Exists(mission.PhysicalPath))
            return null;

        if (mission.Hash == null)
        {
            mission.Hash = await hashService.ComputeHashAsync(mission.PhysicalPath, ct);
            var tracked = await db.Missions.FindAsync([mission.Id], ct);
            if (tracked != null)
            {
                tracked.Hash = mission.Hash;
                await db.SaveChangesAsync(ct);
            }
            logger.LogInformation("Lazy-computed hash for mission {FileName} on instance {InstanceId}: {Hash}",
                mission.FileName, instanceId, mission.Hash);
        }

        var lastModified = File.GetLastWriteTimeUtc(mission.PhysicalPath);
        var etag = $"\"{instanceId}-{mission.FileName}-{mission.Hash}-{mission.SizeBytes}\"";

        return new MissionDownloadResult
        {
            PhysicalPath = mission.PhysicalPath,
            FileName = mission.FileName,
            SizeBytes = mission.SizeBytes,
            Hash = mission.Hash.Value,
            LastModified = lastModified,
            ETag = etag
        };
    }
}
