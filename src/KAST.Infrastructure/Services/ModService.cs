using System.Diagnostics;
using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public class ModService(KastDbContext db, ISteamService steamService, ISettingsService settingsService, IAppEventBroadcaster broadcaster, ILogger<ModService> logger) : IModService
{
    public async Task<IReadOnlyList<SteamMod>> GetAllModsAsync(CancellationToken ct = default)
        => await db.Mods.AsNoTracking().OrderBy(m => m.Name).ToListAsync(ct);

    public async Task<SteamMod?> GetModByIdAsync(int id, CancellationToken ct = default)
        => await db.Mods.FindAsync([id], ct);

    public async Task<SteamMod?> GetModByWorkshopIdAsync(long workshopId, CancellationToken ct = default)
        => await db.Mods.OrderBy(m => m.Id).FirstOrDefaultAsync(m => m.WorkshopId == workshopId, ct);

    public async Task<SteamMod> AddWorkshopModAsync(long workshopId, CancellationToken ct = default)
    {
        var existing = await GetModByWorkshopIdAsync(workshopId, ct);
        if (existing != null)
            return existing;

        var info = await steamService.GetWorkshopItemInfoAsync(workshopId, ct);

        var mod = new SteamMod
        {
            WorkshopId = workshopId,
            Name = info?.Name ?? $"Workshop Item {workshopId}",
            Description = info?.Description,
            ThumbnailUrl = info?.ThumbnailUrl,
            Author = info?.Author,
            SizeBytes = 0,
            ExpectedSizeBytes = info?.SizeBytes ?? 0,
            Source = ModSource.SteamWorkshop,
            Status = ModStatus.NotInstalled,
            LastUpdatedSteam = info?.LastUpdated
        };

        db.Mods.Add(mod);
        await db.SaveChangesAsync(ct);
        return mod;
    }

    public async Task<SteamMod> ImportLocalModAsync(string path, string name, CancellationToken ct = default)
    {
        var isZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var mod = new SteamMod
        {
            Name = name,
            Source = isZip ? ModSource.LocalZip : ModSource.LocalFolder,
            Status = ModStatus.Installed,
            LocalPath = path,
            LastUpdatedLocal = DateTime.UtcNow
        };

        db.Mods.Add(mod);
        await db.SaveChangesAsync(ct);
        return mod;
    }

    public async Task DeleteModAsync(int id, CancellationToken ct = default)
    {
        var mod = await db.Mods.FindAsync([id], ct);
        if (mod != null)
        {
            // Delete mod files from disk
            if (!string.IsNullOrEmpty(mod.LocalPath) && Directory.Exists(mod.LocalPath))
            {
                try
                {
                    Directory.Delete(mod.LocalPath, recursive: true);
                    logger.LogInformation("Deleted mod files at {Path}", mod.LocalPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete mod files at {Path}", mod.LocalPath);
                }
            }

            // DB cascade delete automatically removes ServerInstanceMod join rows
            db.Mods.Remove(mod);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<SteamMod> UpdateModAsync(SteamMod mod, CancellationToken ct = default)
    {
        db.Mods.Update(mod);
        await db.SaveChangesAsync(ct);
        return mod;
    }

    public async Task DownloadModAsync(int id, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var mod = await db.Mods.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Mod {id} not found");

        using var activity = KastActivitySources.Mods.StartActivity(
            "kast.mod.download", ActivityKind.Internal);
        activity?.SetTag("mod.id",          id);
        activity?.SetTag("mod.workshop_id", mod.WorkshopId);
        activity?.SetTag("mod.name",        mod.Name);

        mod.Status = ModStatus.Downloading;
        await db.SaveChangesAsync(ct);
        await broadcaster.BroadcastModStatusChangedAsync(new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));

        // Wrap the user's progress to also broadcast over SignalR
        var broadcastProgress = new Progress<double>(async pct =>
        {
            progress?.Report(pct);
            await broadcaster.BroadcastDownloadProgressAsync(
                new ModDownloadProgressEvent(mod.Id, mod.WorkshopId, pct, (long)(pct / 100.0 * mod.ExpectedSizeBytes), mod.ExpectedSizeBytes));
        });

        try
        {
            var settings = await settingsService.GetSettingsAsync(ct);
            var destPath = Path.Combine(settings.ModsDirectory, mod.WorkshopId.ToString());

            await steamService.DownloadWorkshopItemAsync(mod.WorkshopId, destPath, broadcastProgress, ct);

            mod.Status = ModStatus.Installed;
            mod.LocalPath = Path.GetFullPath(destPath);
            mod.SizeBytes = GetSizeOnDisk(mod.LocalPath);
            mod.LastUpdatedLocal = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Download failed for mod {Id} (WorkshopId={WorkshopId})", id, mod.WorkshopId);
            mod.Status = ModStatus.Error;
            mod.LocalPath = string.Empty;
            mod.SizeBytes = 0;
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            await db.SaveChangesAsync(ct);
            await broadcaster.BroadcastModStatusChangedAsync(new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));
        }
    }

    public async Task UpdateModFilesAsync(int id, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var mod = await db.Mods.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Mod {id} not found");

        using var activity = KastActivitySources.Mods.StartActivity(
            "kast.mod.update", ActivityKind.Internal);
        activity?.SetTag("mod.id",          id);
        activity?.SetTag("mod.workshop_id", mod.WorkshopId);
        activity?.SetTag("mod.name",        mod.Name);

        mod.Status = ModStatus.Updating;
        await db.SaveChangesAsync(ct);
        await broadcaster.BroadcastModStatusChangedAsync(new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));

        var broadcastProgress = new Progress<double>(async pct =>
        {
            progress?.Report(pct);
            await broadcaster.BroadcastDownloadProgressAsync(
                new ModDownloadProgressEvent(mod.Id, mod.WorkshopId, pct, (long)(pct / 100.0 * mod.ExpectedSizeBytes), mod.ExpectedSizeBytes));
        });

        try
        {
            var settings = await settingsService.GetSettingsAsync(ct);
            var destPath = string.IsNullOrEmpty(mod.LocalPath)
                ? Path.Combine(settings.ModsDirectory, mod.WorkshopId.ToString())
                : mod.LocalPath;

            await steamService.DownloadWorkshopItemAsync(mod.WorkshopId, destPath, broadcastProgress, ct);
            mod.Status = ModStatus.Installed;
            mod.LocalPath = Path.GetFullPath(destPath);
            mod.SizeBytes = GetSizeOnDisk(mod.LocalPath);
            mod.LastUpdatedLocal = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Update failed for mod {Id} (WorkshopId={WorkshopId})", id, mod.WorkshopId);
            mod.Status = ModStatus.Error;
            mod.SizeBytes = 0;
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            await db.SaveChangesAsync(ct);
            await broadcaster.BroadcastModStatusChangedAsync(new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));
        }
    }

    public async Task CheckForUpdatesAsync(CancellationToken ct = default)
    {
        using var activity = KastActivitySources.Mods.StartActivity(
            "kast.mod.update_check", ActivityKind.Internal);

        var mods = await db.Mods
            .Where(m => m.Source == ModSource.SteamWorkshop && m.Status == ModStatus.Installed)
            .ToListAsync(ct);

        activity?.SetTag("mods.checked", mods.Count);
        int updatesFound = 0;

        foreach (var mod in mods)
        {
            var info = await steamService.GetWorkshopItemInfoAsync(mod.WorkshopId, ct);
            if (info != null && info.LastUpdated > mod.LastUpdatedLocal)
            {
                mod.Status = ModStatus.UpdateAvailable;
                mod.LastUpdatedSteam = info.LastUpdated;
                updatesFound++;
            }
        }

        activity?.SetTag("mods.updates_found", updatesFound);
        await db.SaveChangesAsync(ct);
    }

    private static long GetSizeOnDisk(string path)
    {
        try
        {
            if (File.Exists(path))
                return new FileInfo(path).Length;

            if (Directory.Exists(path))
                return new DirectoryInfo(path)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
        }
        catch { /* best effort */ }
        return 0;
    }
}
