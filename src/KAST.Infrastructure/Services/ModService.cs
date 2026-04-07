using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KAST.Infrastructure.Services;

public class ModService(KastDbContext db, ISteamService steamService) : IModService
{
    public async Task<IReadOnlyList<SteamMod>> GetAllModsAsync(CancellationToken ct = default)
        => await db.Mods.AsNoTracking().OrderBy(m => m.Name).ToListAsync(ct);

    public async Task<SteamMod?> GetModByIdAsync(int id, CancellationToken ct = default)
        => await db.Mods.FindAsync([id], ct);

    public async Task<SteamMod?> GetModByWorkshopIdAsync(long workshopId, CancellationToken ct = default)
        => await db.Mods.FirstOrDefaultAsync(m => m.WorkshopId == workshopId, ct);

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
            SizeBytes = info?.SizeBytes ?? 0,
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

    public async Task DownloadModAsync(int id, CancellationToken ct = default)
    {
        var mod = await db.Mods.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Mod {id} not found");

        mod.Status = ModStatus.Downloading;
        await db.SaveChangesAsync(ct);

        try
        {
            var destPath = Path.Combine("mods", mod.WorkshopId.ToString());
            var progress = new Progress<double>(p =>
            {
                // Progress will be reported via SignalR in the hub
            });

            await steamService.DownloadWorkshopItemAsync(mod.WorkshopId, destPath, progress, ct);

            mod.Status = ModStatus.Installed;
            mod.LocalPath = Path.GetFullPath(destPath);
            mod.LastUpdatedLocal = DateTime.UtcNow;
        }
        catch
        {
            mod.Status = ModStatus.Error;
            throw;
        }
        finally
        {
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UpdateModFilesAsync(int id, CancellationToken ct = default)
    {
        var mod = await db.Mods.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Mod {id} not found");

        mod.Status = ModStatus.Updating;
        await db.SaveChangesAsync(ct);

        try
        {
            await steamService.DownloadWorkshopItemAsync(mod.WorkshopId, mod.LocalPath, null, ct);
            mod.Status = ModStatus.Installed;
            mod.LastUpdatedLocal = DateTime.UtcNow;
        }
        catch
        {
            mod.Status = ModStatus.Error;
            throw;
        }
        finally
        {
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var mods = await db.Mods
            .Where(m => m.Source == ModSource.SteamWorkshop && m.Status == ModStatus.Installed)
            .ToListAsync(ct);

        foreach (var mod in mods)
        {
            var info = await steamService.GetWorkshopItemInfoAsync(mod.WorkshopId, ct);
            if (info != null && info.LastUpdated > mod.LastUpdatedLocal)
            {
                mod.Status = ModStatus.UpdateAvailable;
                mod.LastUpdatedSteam = info.LastUpdated;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
