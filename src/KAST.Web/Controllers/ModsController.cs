using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using KAST.Web.Services.Content;
using Microsoft.AspNetCore.Mvc;

namespace KAST.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ModsController(IModService modService, ContentOrchestrator orchestrator,
    ISettingsService settingsService, IFileSystemService fs, IAppEventBroadcaster broadcaster) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SteamMod>>> GetAll(CancellationToken ct)
        => Ok(await modService.GetAllModsAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SteamMod>> GetById(int id, CancellationToken ct)
    {
        var mod = await modService.GetModByIdAsync(id, ct);
        return mod is null ? NotFound() : Ok(mod);
    }

    [HttpPost("workshop/{workshopId:long}")]
    public async Task<ActionResult<SteamMod>> AddWorkshopMod(long workshopId, CancellationToken ct)
    {
        var mod = await modService.AddWorkshopModAsync(workshopId, ct);
        return CreatedAtAction(nameof(GetById), new { id = mod.Id }, mod);
    }

    [HttpPost("local")]
    public async Task<ActionResult<SteamMod>> ImportLocal([FromBody] ImportLocalModRequest request, CancellationToken ct)
    {
        var mod = await modService.ImportLocalModAsync(request.Path, request.Name, ct);
        return CreatedAtAction(nameof(GetById), new { id = mod.Id }, mod);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        await modService.DeleteModAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/download")]
    public async Task<ActionResult> Download(int id, CancellationToken ct)
    {
        var mod = await modService.GetModByIdAsync(id, ct);
        if (mod is null) return NotFound();

        // Set status to downloading and persist
        mod.Status = ModStatus.Downloading;
        await modService.UpdateModAsync(mod, ct);
        await broadcaster.BroadcastModStatusChangedAsync(new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));

        var settings = await settingsService.GetSettingsAsync(ct);
        var destPath = Path.Combine(settings.ModsDirectory, mod.WorkshopId.ToString());

        orchestrator.StartModInstall(mod.Id, ContentType.SteamMod, destPath, mod.WorkshopId,
            onComplete: async (sp, _) =>
            {
                var db = sp.GetRequiredService<KastDbContext>();
                var m = await db.Mods.FindAsync(id);
                if (m is null) return;
                m.Status = ModStatus.Installed;
                m.LocalPath = Path.GetFullPath(destPath);
                m.SizeBytes = fs.GetDirectorySize(destPath);
                m.LastUpdatedLocal = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await sp.GetRequiredService<IAppEventBroadcaster>()
                    .BroadcastModStatusChangedAsync(new ModStatusChangedEvent(m.Id, m.Status.ToString()));
            },
            onError: async (sp, _, _) =>
            {
                var db = sp.GetRequiredService<KastDbContext>();
                var m = await db.Mods.FindAsync(id);
                if (m is null) return;
                m.Status = ModStatus.Error;
                await db.SaveChangesAsync();
                await sp.GetRequiredService<IAppEventBroadcaster>()
                    .BroadcastModStatusChangedAsync(new ModStatusChangedEvent(m.Id, m.Status.ToString()));
            });

        return Ok();
    }

    [HttpPost("{id:int}/update")]
    public async Task<ActionResult> Update(int id, CancellationToken ct)
    {
        var mod = await modService.GetModByIdAsync(id, ct);
        if (mod is null) return NotFound();

        mod.Status = ModStatus.Updating;
        await modService.UpdateModAsync(mod, ct);
        await broadcaster.BroadcastModStatusChangedAsync(new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));

        var destPath = mod.LocalPath;

        orchestrator.StartModInstall(mod.Id, ContentType.SteamMod, destPath, mod.WorkshopId,
            onComplete: async (sp, _) =>
            {
                var db = sp.GetRequiredService<KastDbContext>();
                var m = await db.Mods.FindAsync(id);
                if (m is null) return;
                m.Status = ModStatus.Installed;
                m.SizeBytes = fs.GetDirectorySize(destPath);
                m.LastUpdatedLocal = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await sp.GetRequiredService<IAppEventBroadcaster>()
                    .BroadcastModStatusChangedAsync(new ModStatusChangedEvent(m.Id, m.Status.ToString()));
            },
            onError: async (sp, _, _) =>
            {
                var db = sp.GetRequiredService<KastDbContext>();
                var m = await db.Mods.FindAsync(id);
                if (m is null) return;
                m.Status = ModStatus.Error;
                await db.SaveChangesAsync();
                await sp.GetRequiredService<IAppEventBroadcaster>()
                    .BroadcastModStatusChangedAsync(new ModStatusChangedEvent(m.Id, m.Status.ToString()));
            });

        return Ok();
    }

    [HttpPost("check-updates")]
    public async Task<ActionResult> CheckUpdates(CancellationToken ct)
    {
        await modService.CheckForUpdatesAsync(ct);
        return Ok();
    }
}

public record ImportLocalModRequest(string Path, string Name);
