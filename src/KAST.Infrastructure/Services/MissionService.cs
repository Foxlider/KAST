using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace KAST.Infrastructure.Services;

public class MissionService(
    KastDbContext db,
    IServerInstanceService serverInstanceService,
    ILogger<MissionService> logger) : IMissionService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> InstanceLocks = new();

    public async Task<IReadOnlyList<Mission>> GetMissionsForInstanceAsync(int instanceId, CancellationToken ct = default)
    {
        await ReconcileMissionFilesAsync(instanceId, ct);

        return await db.Missions
            .Include(m => m.TagAssignments).ThenInclude(a => a.Tag)
            .Include(m => m.ModPreset)
            .AsNoTracking()
            .Where(m => m.ServerInstanceId == instanceId)
            .OrderByDescending(m => m.UploadedAt)
            .ToListAsync(ct);
    }

    public async Task<Mission?> GetMissionByIdAsync(int id, CancellationToken ct = default)
        => await db.Missions
            .Include(m => m.TagAssignments).ThenInclude(a => a.Tag)
            .Include(m => m.ModPreset)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<Mission> UploadMissionAsync(int instanceId, string fileName, Stream pboStream, CancellationToken ct = default)
    {
        var instance = await serverInstanceService.GetInstanceByIdAsync(instanceId, ct)
            ?? throw new InvalidOperationException($"Server instance {instanceId} not found");

        var gate = InstanceLocks.GetOrAdd(instanceId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var mpmissionsDir = GetCanonicalMissionsDirectory(instance.InstallPath);
            Directory.CreateDirectory(mpmissionsDir);

            var safeName = SanitizeMissionFileName(fileName);
            var targetPath = Path.GetFullPath(Path.Join(mpmissionsDir, safeName));
            EnsurePathInsideDirectory(mpmissionsDir, targetPath);
            var tempPath = Path.Join(mpmissionsDir, $".{safeName}.{Guid.NewGuid():N}.upload");

            try
            {
                await using (var fileStream = new FileStream(
                                 tempPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 1024 * 1024,
                                 FileOptions.SequentialScan | FileOptions.Asynchronous))
                {
                    await pboStream.CopyToAsync(fileStream, ct);
                    await fileStream.FlushAsync(ct);
                }

                ReplaceFile(tempPath, targetPath);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }

            var fileInfo = new FileInfo(targetPath);
            var mission = await db.Missions
                .Include(m => m.TagAssignments)
                .FirstOrDefaultAsync(
                    m => m.ServerInstanceId == instanceId && m.FileName.ToLower() == safeName.ToLower(),
                    ct);

            if (mission == null)
            {
                var (displayName, mapName) = ParseMissionName(safeName);
                mission = new Mission
                {
                    ServerInstanceId = instanceId,
                    FileName = safeName,
                    DisplayName = displayName,
                    MapName = mapName,
                    UploadedAt = DateTime.UtcNow
                };
                db.Missions.Add(mission);
            }

            mission.FileName = safeName;
            mission.SizeBytes = fileInfo.Length;
            mission.PhysicalPath = targetPath;
            mission.UploadedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Mission uploaded or replaced: {FileName} ({SizeBytes} bytes) on instance {InstanceId}",
                safeName,
                fileInfo.Length,
                instanceId);
            return mission;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Mission> UpdateMissionAsync(Mission mission, CancellationToken ct = default)
    {
        var existing = await db.Missions.FindAsync([mission.Id], ct)
            ?? throw new InvalidOperationException($"Mission {mission.Id} not found");

        existing.DisplayName = mission.DisplayName;
        existing.MapName = mission.MapName;
        existing.ModPresetId = mission.ModPresetId;

        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task DeleteMissionAsync(int id, CancellationToken ct = default)
    {
        var mission = await db.Missions.FindAsync([id], ct);
        if (mission == null) return;

        // Delete physical file
        if (!string.IsNullOrEmpty(mission.PhysicalPath) && File.Exists(mission.PhysicalPath))
        {
            try
            {
                File.Delete(mission.PhysicalPath);
                logger.LogInformation("Deleted mission file: {Path}", mission.PhysicalPath);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Failed to delete mission file: {Path}", mission.PhysicalPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Failed to delete mission file: {Path}", mission.PhysicalPath);
            }
        }

        db.Missions.Remove(mission);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Stream?> GetMissionFileStreamAsync(int id, CancellationToken ct = default)
    {
        var mission = await db.Missions.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mission == null || string.IsNullOrEmpty(mission.PhysicalPath) || !File.Exists(mission.PhysicalPath))
            return null;

        return File.OpenRead(mission.PhysicalPath);
    }

    // ── Tags ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<MissionTag>> GetTagsForInstanceAsync(int instanceId, CancellationToken ct = default)
        => await db.MissionTags
            .AsNoTracking()
            .Where(t => t.ServerInstanceId == instanceId)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<MissionTag> CreateTagAsync(int instanceId, string name, CancellationToken ct = default)
    {
        var cleanName = name.TrimStart('#').Trim();

        var existing = await db.MissionTags
            .FirstOrDefaultAsync(t => t.ServerInstanceId == instanceId && t.Name == cleanName, ct);

        if (existing != null)
            return existing;

        var tag = new MissionTag
        {
            ServerInstanceId = instanceId,
            Name = cleanName
        };

        db.MissionTags.Add(tag);
        await db.SaveChangesAsync(ct);
        return tag;
    }

    public async Task DeleteTagAsync(int id, CancellationToken ct = default)
    {
        var tag = await db.MissionTags.FindAsync([id], ct);
        if (tag == null) return;

        db.MissionTags.Remove(tag);
        await db.SaveChangesAsync(ct);
    }

    public async Task AssignTagAsync(int missionId, int tagId, CancellationToken ct = default)
    {
        var exists = await db.MissionTagAssignments
            .AnyAsync(a => a.MissionId == missionId && a.MissionTagId == tagId, ct);

        if (exists) return;

        db.MissionTagAssignments.Add(new MissionTagAssignment
        {
            MissionId = missionId,
            MissionTagId = tagId
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveTagAsync(int missionId, int tagId, CancellationToken ct = default)
    {
        var assignment = await db.MissionTagAssignments
            .FirstOrDefaultAsync(a => a.MissionId == missionId && a.MissionTagId == tagId, ct);

        if (assignment == null) return;

        db.MissionTagAssignments.Remove(assignment);
        await db.SaveChangesAsync(ct);
    }

    // ── Campaigns ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Campaign>> GetCampaignsForInstanceAsync(int instanceId, CancellationToken ct = default)
    {
        var campaigns = await db.Campaigns
            .Include(c => c.CampaignMissions.OrderBy(cm => cm.OrderIndex)).ThenInclude(cm => cm.Mission)
            .AsNoTracking()
            .Where(c => c.ServerInstanceId == instanceId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        foreach (var campaign in campaigns)
            campaign.CampaignMissions = campaign.CampaignMissions.OrderBy(cm => cm.OrderIndex).ToList();

        return campaigns;
    }

    public async Task<Campaign?> GetCampaignByIdAsync(int id, CancellationToken ct = default)
        => await db.Campaigns
            .Include(c => c.CampaignMissions.OrderBy(cm => cm.OrderIndex)).ThenInclude(cm => cm.Mission)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<Campaign> CreateCampaignAsync(Campaign campaign, CancellationToken ct = default)
    {
        campaign.Title = NormalizeFolderTitle(campaign.Title, "New Campaign");
        campaign.Description = campaign.Description?.Trim();
        campaign.CreatedAt = campaign.CreatedAt == default ? DateTime.UtcNow : campaign.CreatedAt;
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);
        return campaign;
    }

    public async Task<Campaign> UpdateCampaignAsync(Campaign campaign, CancellationToken ct = default)
    {
        var existing = await db.Campaigns.FindAsync([campaign.Id], ct)
            ?? throw new InvalidOperationException($"Campaign {campaign.Id} not found");

        existing.Title = NormalizeFolderTitle(campaign.Title, "New Campaign");
        existing.Description = campaign.Description?.Trim();
        existing.ImagePath = campaign.ImagePath;

        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task DeleteCampaignAsync(int id, CancellationToken ct = default)
    {
        var campaign = await db.Campaigns.FindAsync([id], ct);
        if (campaign == null) return;

        db.Campaigns.Remove(campaign);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddMissionToCampaignAsync(int campaignId, int missionId, int orderIndex, CancellationToken ct = default)
    {
        var campaign = await db.Campaigns.FindAsync([campaignId], ct)
            ?? throw new InvalidOperationException($"Campaign {campaignId} not found");
        var mission = await db.Missions.FindAsync([missionId], ct)
            ?? throw new InvalidOperationException($"Mission {missionId} not found");

        if (campaign.ServerInstanceId != mission.ServerInstanceId)
            throw new InvalidOperationException("Campaigns can only contain missions from the same server instance.");

        var exists = await db.CampaignMissions
            .AnyAsync(cm => cm.CampaignId == campaignId && cm.MissionId == missionId, ct);

        if (exists) return;

        if (orderIndex < 0)
        {
            orderIndex = await db.CampaignMissions
                .Where(cm => cm.CampaignId == campaignId)
                .CountAsync(ct);
        }

        db.CampaignMissions.Add(new CampaignMission
        {
            CampaignId = campaignId,
            MissionId = missionId,
            OrderIndex = orderIndex
        });

        await db.SaveChangesAsync(ct);
        await NormalizeCampaignOrderAsync(campaignId, ct);
    }

    public async Task RemoveMissionFromCampaignAsync(int campaignId, int missionId, CancellationToken ct = default)
    {
        var cm = await db.CampaignMissions
            .FirstOrDefaultAsync(c => c.CampaignId == campaignId && c.MissionId == missionId, ct);

        if (cm == null) return;

        db.CampaignMissions.Remove(cm);
        await db.SaveChangesAsync(ct);
        await NormalizeCampaignOrderAsync(campaignId, ct);
    }

    public async Task ReorderCampaignMissionsAsync(int campaignId, List<int> orderedMissionIds, CancellationToken ct = default)
    {
        var entries = await db.CampaignMissions
            .Where(cm => cm.CampaignId == campaignId)
            .ToListAsync(ct);

        var nextOrder = 0;
        foreach (var entry in orderedMissionIds
                     .Distinct()
                     .Select(missionId => entries.FirstOrDefault(e => e.MissionId == missionId))
                     .Where(entry => entry is not null))
        {
            entry!.OrderIndex = nextOrder++;
        }

        foreach (var entry in entries.Where(e => !orderedMissionIds.Contains(e.MissionId)).OrderBy(e => e.OrderIndex))
        {
            entry.OrderIndex = nextOrder++;
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Sets ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Set>> GetSetsForInstanceAsync(int instanceId, CancellationToken ct = default)
    {
        var sets = await db.Sets
            .Include(s => s.SetMissions).ThenInclude(sm => sm.Mission)
            .AsNoTracking()
            .Where(s => s.ServerInstanceId == instanceId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        foreach (var set in sets)
            set.SetMissions = set.SetMissions
                .OrderBy(sm => sm.Mission!.DisplayName)
                .ThenBy(sm => sm.Mission!.FileName)
                .ToList();

        return sets;
    }

    public async Task<Set?> GetSetByIdAsync(int id, CancellationToken ct = default)
    {
        var set = await db.Sets
            .Include(s => s.SetMissions).ThenInclude(sm => sm.Mission)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (set != null)
        {
            set.SetMissions = set.SetMissions
                .OrderBy(sm => sm.Mission!.DisplayName)
                .ThenBy(sm => sm.Mission!.FileName)
                .ToList();
        }

        return set;
    }

    public async Task<Set> CreateSetAsync(Set set, CancellationToken ct = default)
    {
        set.Title = NormalizeFolderTitle(set.Title, "New Set");
        set.Description = set.Description?.Trim();
        set.CreatedAt = set.CreatedAt == default ? DateTime.UtcNow : set.CreatedAt;
        db.Sets.Add(set);
        await db.SaveChangesAsync(ct);
        return set;
    }

    public async Task<Set> UpdateSetAsync(Set set, CancellationToken ct = default)
    {
        var existing = await db.Sets.FindAsync([set.Id], ct)
            ?? throw new InvalidOperationException($"Set {set.Id} not found");

        existing.Title = NormalizeFolderTitle(set.Title, "New Set");
        existing.Description = set.Description?.Trim();
        existing.ImagePath = set.ImagePath;

        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task DeleteSetAsync(int id, CancellationToken ct = default)
    {
        var set = await db.Sets.FindAsync([id], ct);
        if (set == null) return;

        db.Sets.Remove(set);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddMissionToSetAsync(int setId, int missionId, CancellationToken ct = default)
    {
        var set = await db.Sets.FindAsync([setId], ct)
            ?? throw new InvalidOperationException($"Set {setId} not found");
        var mission = await db.Missions.FindAsync([missionId], ct)
            ?? throw new InvalidOperationException($"Mission {missionId} not found");

        if (set.ServerInstanceId != mission.ServerInstanceId)
            throw new InvalidOperationException("Sets can only contain missions from the same server instance.");

        var exists = await db.SetMissions
            .AnyAsync(sm => sm.SetId == setId && sm.MissionId == missionId, ct);

        if (exists) return;

        db.SetMissions.Add(new SetMission
        {
            SetId = setId,
            MissionId = missionId
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveMissionFromSetAsync(int setId, int missionId, CancellationToken ct = default)
    {
        var sm = await db.SetMissions
            .FirstOrDefaultAsync(s => s.SetId == setId && s.MissionId == missionId, ct);

        if (sm == null) return;

        db.SetMissions.Remove(sm);
        await db.SaveChangesAsync(ct);
    }

    // ── Search ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Mission>> SearchMissionsAsync(int instanceId, string? query, List<int>? tagIds, string? mapName, CancellationToken ct = default)
    {
        await ReconcileMissionFilesAsync(instanceId, ct);

        var missionsQuery = db.Missions
            .Include(m => m.TagAssignments).ThenInclude(a => a.Tag)
            .Include(m => m.ModPreset)
            .AsNoTracking()
            .Where(m => m.ServerInstanceId == instanceId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            missionsQuery = missionsQuery.Where(m =>
                m.DisplayName.Contains(q) || m.FileName.Contains(q) || m.MapName.Contains(q));
        }

        if (tagIds is { Count: > 0 })
        {
            missionsQuery = missionsQuery.Where(m =>
                m.TagAssignments.Any(a => tagIds.Contains(a.MissionTagId)));
        }

        if (!string.IsNullOrWhiteSpace(mapName))
        {
            missionsQuery = missionsQuery.Where(m => m.MapName == mapName);
        }

        return await missionsQuery
            .OrderByDescending(m => m.UploadedAt)
            .ToListAsync(ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task ReconcileMissionFilesAsync(int instanceId, CancellationToken ct)
    {
        var instance = await serverInstanceService.GetInstanceByIdAsync(instanceId, ct);
        if (instance == null)
            return;

        var gate = InstanceLocks.GetOrAdd(instanceId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            CollapseAutoRenamedMissionDuplicates(instance.InstallPath);

            var missionFiles = EnumerateMissionFiles(instance.InstallPath)
                .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(path => IsCanonicalMissionDirectory(instance.InstallPath, path) ? 0 : 1).First())
                .ToList();

            var existing = await db.Missions
                .Where(m => m.ServerInstanceId == instanceId)
                .ToListAsync(ct);

            var existingByName = existing.ToDictionary(m => m.FileName, StringComparer.OrdinalIgnoreCase);
            var diskNames = missionFiles
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var path in missionFiles)
            {
                var name = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var info = new FileInfo(path);
                if (!existingByName.TryGetValue(name, out var mission))
                {
                    var (displayName, mapName) = ParseMissionName(name);
                    mission = new Mission
                    {
                        ServerInstanceId = instanceId,
                        FileName = name,
                        DisplayName = displayName,
                        MapName = mapName,
                        UploadedAt = info.LastWriteTimeUtc
                    };
                    db.Missions.Add(mission);
                }

                mission.FileName = name;
                mission.PhysicalPath = info.FullName;
                mission.SizeBytes = info.Length;
            }

            var stale = existing.Where(m => !diskNames.Contains(m.FileName)).ToList();
            if (stale.Count > 0)
                db.Missions.RemoveRange(stale);

            if (missionFiles.Count > 0 || stale.Count > 0)
                await db.SaveChangesAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task NormalizeCampaignOrderAsync(int campaignId, CancellationToken ct)
    {
        var entries = await db.CampaignMissions
            .Where(cm => cm.CampaignId == campaignId)
            .OrderBy(cm => cm.OrderIndex)
            .ThenBy(cm => cm.MissionId)
            .ToListAsync(ct);

        for (var i = 0; i < entries.Count; i++)
            entries[i].OrderIndex = i;

        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<string> EnumerateMissionFiles(string installPath)
    {
        foreach (var directory in GetMissionDirectories(installPath).Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.pbo", SearchOption.TopDirectoryOnly))
                yield return Path.GetFullPath(file);
        }
    }

    private void CollapseAutoRenamedMissionDuplicates(string installPath)
    {
        foreach (var directory in GetMissionDirectories(installPath).Where(Directory.Exists))
        {
            var files = Directory.EnumerateFiles(directory, "*.pbo", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .ToList();
            var byName = files.ToDictionary(file => file.Name, StringComparer.OrdinalIgnoreCase);
            var duplicateGroups = files
                .Select(file => new { File = file, OriginalName = TryGetAutoRenamedOriginalName(file.Name) })
                .Where(item => item.OriginalName != null && byName.ContainsKey(item.OriginalName))
                .GroupBy(item => item.OriginalName!, StringComparer.OrdinalIgnoreCase);

            foreach (var group in duplicateGroups)
            {
                var original = byName[group.Key];
                var duplicates = group.Select(item => item.File).ToList();
                var newest = duplicates
                    .Append(original)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .First();

                if (!string.Equals(newest.FullName, original.FullName, StringComparison.OrdinalIgnoreCase))
                    ReplaceFile(newest.FullName, original.FullName);

                foreach (var duplicate in duplicates)
                    TryDeleteFile(duplicate.FullName);

                logger.LogWarning(
                    "Collapsed auto-renamed duplicate mission files for {FileName} in {Directory}",
                    original.Name,
                    directory);
            }
        }
    }

    private static IEnumerable<string> GetMissionDirectories(string installPath)
    {
        yield return GetCanonicalMissionsDirectory(installPath);

        var legacyUnderscorePath = Path.Join(installPath, "mp_missions");
        if (!Path.GetFullPath(legacyUnderscorePath).Equals(
                Path.GetFullPath(GetCanonicalMissionsDirectory(installPath)),
                StringComparison.OrdinalIgnoreCase))
        {
            yield return legacyUnderscorePath;
        }
    }

    private static string GetCanonicalMissionsDirectory(string installPath)
        => Path.Join(installPath, "mpmissions");

    private static bool IsCanonicalMissionDirectory(string installPath, string missionPath)
    {
        var canonical = Path.GetFullPath(GetCanonicalMissionsDirectory(installPath));
        var directory = Path.GetDirectoryName(Path.GetFullPath(missionPath));
        return string.Equals(canonical, directory, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeMissionFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(Path.GetFileName(fileName).Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        sanitized = string.IsNullOrWhiteSpace(sanitized) ? "mission.pbo" : sanitized;

        if (!Path.GetExtension(sanitized).Equals(".pbo", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Mission uploads must be .pbo files.");

        return sanitized;
    }

    private static (string DisplayName, string MapName) ParseMissionName(string fileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var dotIndex = nameWithoutExt.LastIndexOf('.');
        if (dotIndex <= 0 || dotIndex == nameWithoutExt.Length - 1)
            return (nameWithoutExt, "");

        return (nameWithoutExt[..dotIndex], nameWithoutExt[(dotIndex + 1)..]);
    }

    private static string? TryGetAutoRenamedOriginalName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".pbo", StringComparison.OrdinalIgnoreCase))
            return null;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        if (!nameWithoutExt.EndsWith(')'))
            return null;

        var markerIndex = nameWithoutExt.LastIndexOf(" (", StringComparison.Ordinal);
        if (markerIndex <= 0)
            return null;

        var counterText = nameWithoutExt[(markerIndex + 2)..^1];
        if (!counterText.All(char.IsDigit))
            return null;

        return nameWithoutExt[..markerIndex] + extension;
    }

    private static string NormalizeFolderTitle(string? title, string fallback)
    {
        var clean = title?.Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    private static void EnsurePathInsideDirectory(string directory, string path)
    {
        var root = Path.GetFullPath(directory);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!Path.GetFullPath(path).StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Mission path escapes the mpmissions directory.");
    }

    private static void ReplaceFile(string sourcePath, string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            File.Move(sourcePath, targetPath);
            return;
        }

        var backupPath = $"{targetPath}.{Guid.NewGuid():N}.bak";
        try
        {
            File.Replace(sourcePath, targetPath, backupPath, ignoreMetadataErrors: true);
        }
        finally
        {
            TryDeleteFile(backupPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort cleanup only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup only.
        }
    }
}
