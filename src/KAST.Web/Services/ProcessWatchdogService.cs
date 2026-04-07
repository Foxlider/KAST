using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KAST.Web.Services;

/// <summary>
/// Monitors running server instances for crashed processes and
/// restarts them according to their RestartPolicy.
/// </summary>
public class ProcessWatchdogService(
    IServiceScopeFactory scopeFactory,
    IProcessManagerService processManager,
    IAppEventBroadcaster broadcaster,
    ILogger<ProcessWatchdogService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);

    // Track restart attempts per instance to enforce MaxRestartAttempts
    private readonly Dictionary<int, int> _restartAttempts = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Process watchdog service started");

        using var timer = new PeriodicTimer(CheckInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckRunningInstancesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error in process watchdog check");
            }
        }

        logger.LogInformation("Process watchdog service stopped");
    }

    private async Task CheckRunningInstancesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();

        var runningInstances = await db.ServerInstances
            .Where(s => s.Status == ServerInstanceStatus.Running && s.ProcessId != null)
            .ToListAsync(ct);

        foreach (var instance in runningInstances)
        {
            if (processManager.IsProcessRunning(instance.ProcessId!.Value))
                continue;

            // Process has crashed/exited unexpectedly
            logger.LogWarning("Server {Name} (PID {Pid}) has crashed", instance.Name, instance.ProcessId);

            instance.ProcessId = null;
            instance.Status = ServerInstanceStatus.Crashed;
            instance.StartedAt = null;
            await db.SaveChangesAsync(ct);
            await broadcaster.BroadcastServerStatusChangedAsync(
                new ServerStatusChangedEvent(instance.Id, instance.Status.ToString()));

            // Check restart policy
            switch (instance.RestartPolicy)
            {
                case RestartPolicy.None:
                    logger.LogInformation("Server {Name}: RestartPolicy=None, leaving crashed", instance.Name);
                    _restartAttempts.Remove(instance.Id);
                    break;

                case RestartPolicy.OnCrash:
                case RestartPolicy.Always:
                    await TryRestartAsync(instance, db, scope.ServiceProvider, ct);
                    break;
            }
        }
    }

    private async Task TryRestartAsync(
        Core.Models.ServerInstance instance,
        KastDbContext db,
        IServiceProvider sp,
        CancellationToken ct)
    {
        _restartAttempts.TryGetValue(instance.Id, out var attempts);

        if (attempts >= instance.MaxRestartAttempts)
        {
            logger.LogError(
                "Server {Name}: exceeded max restart attempts ({Max}), leaving crashed",
                instance.Name, instance.MaxRestartAttempts);
            _restartAttempts.Remove(instance.Id);
            return;
        }

        _restartAttempts[instance.Id] = attempts + 1;

        logger.LogInformation(
            "Server {Name}: restarting (attempt {Attempt}/{Max}, policy={Policy})",
            instance.Name, attempts + 1, instance.MaxRestartAttempts, instance.RestartPolicy);

        try
        {
            var serverService = sp.GetRequiredService<IServerInstanceService>();
            await serverService.StartInstanceAsync(instance.Id, ct);

            // Successful restart — reset counter
            _restartAttempts.Remove(instance.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Server {Name}: restart attempt {Attempt} failed", instance.Name, attempts + 1);
        }
    }
}
