using KAST.Core.Interfaces;
using KAST.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace KAST.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MonitoringController(IMonitoringService monitoringService) : ControllerBase
{
    [HttpGet("host")]
    public async Task<ActionResult<HostMetrics>> GetHostMetrics(CancellationToken ct)
        => Ok(await monitoringService.GetHostMetricsAsync(ct));

    [HttpGet("instances")]
    public async Task<ActionResult<IReadOnlyList<InstanceMetrics>>> GetAllInstanceMetrics(CancellationToken ct)
        => Ok(await monitoringService.GetAllInstanceMetricsAsync(ct));

    [HttpGet("instances/{id:int}")]
    public async Task<ActionResult<InstanceMetrics>> GetInstanceMetrics(int id, CancellationToken ct)
    {
        var metrics = await monitoringService.GetInstanceMetricsAsync(id, ct);
        return metrics is null ? NotFound() : Ok(metrics);
    }
}
