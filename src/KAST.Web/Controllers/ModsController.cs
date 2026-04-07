using KAST.Core.Interfaces;
using KAST.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace KAST.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ModsController(IModService modService) : ControllerBase
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
        await modService.DownloadModAsync(id, ct);
        return Ok();
    }

    [HttpPost("{id:int}/update")]
    public async Task<ActionResult> Update(int id, CancellationToken ct)
    {
        await modService.UpdateModFilesAsync(id, ct);
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
