using KAST.Core.Interfaces;
using KAST.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace KAST.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServersController(IServerInstanceService serverService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServerInstance>>> GetAll(CancellationToken ct)
        => Ok(await serverService.GetAllInstancesAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServerInstance>> GetById(int id, CancellationToken ct)
    {
        var instance = await serverService.GetInstanceByIdAsync(id, ct);
        return instance is null ? NotFound() : Ok(instance);
    }

    [HttpPost]
    public async Task<ActionResult<ServerInstance>> Create([FromBody] ServerInstance instance, CancellationToken ct)
    {
        var created = await serverService.CreateInstanceAsync(instance, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ServerInstance>> Update(int id, [FromBody] ServerInstance instance, CancellationToken ct)
    {
        instance.Id = id;
        var updated = await serverService.UpdateInstanceAsync(instance, ct);
        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        await serverService.DeleteInstanceAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/start")]
    public async Task<ActionResult> Start(int id, CancellationToken ct)
    {
        await serverService.StartInstanceAsync(id, ct);
        return Ok();
    }

    [HttpPost("{id:int}/stop")]
    public async Task<ActionResult> Stop(int id, CancellationToken ct)
    {
        await serverService.StopInstanceAsync(id, ct);
        return Ok();
    }

    [HttpPost("{id:int}/restart")]
    public async Task<ActionResult> Restart(int id, CancellationToken ct)
    {
        await serverService.RestartInstanceAsync(id, ct);
        return Ok();
    }

    [HttpPost("{id:int}/mods/{modId:int}")]
    public async Task<ActionResult> AddMod(int id, int modId, [FromQuery] int loadOrder = 0, CancellationToken ct = default)
    {
        await serverService.AddModToInstanceAsync(id, modId, loadOrder, ct);
        return Ok();
    }

    [HttpDelete("{id:int}/mods/{modId:int}")]
    public async Task<ActionResult> RemoveMod(int id, int modId, CancellationToken ct)
    {
        await serverService.RemoveModFromInstanceAsync(id, modId, ct);
        return NoContent();
    }
}
