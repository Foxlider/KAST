using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KAST.UI.Services;

/// <summary>
/// Monitors running server instances for crashed processes and restarts them per RestartPolicy.
/// </summary>
public class ProcessWatchdogService(
    IServiceScopeFactory scopeFactory,
    IProcessManagerService processManager,
    IAppEventBroadcaster broadcaster,
    IServerConsoleLogTailer consoleLogTailer,
    ILogger<ProcessWatchdogService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
    private readonly Dictionary<int, int> _restartAttempts = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Process watchdog service started");

        using var timer = new PeriodicTimer(CheckInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            { await CheckRunningInstancesAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            { break; }
            catch (Exception ex)
            { logger.LogWarning(ex, "Error in process watchdog check"); }
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

        foreach (var instance in runningInstances.Where(instance => !processManager.IsProcessRunning(instance.ProcessId!.Value)))
        {
            logger.LogWarning("Server {Name} (PID {Pid}) has crashed", instance.Name, instance.ProcessId);
            await consoleLogTailer.StopFollowingAsync(instance.Id);

            instance.ProcessId = null;
            instance.Status = ServerInstanceStatus.Crashed;
            instance.StartedAt = null;
            await db.SaveChangesAsync(ct);
            await broadcaster.BroadcastServerStatusChangedAsync(
                new ServerStatusChangedEvent(instance.Id, instance.Status.ToString()));

            switch (instance.RestartPolicy)
            {
                case RestartPolicy.None:
                    logger.LogInformation("Server {Name}: RestartPolicy=None, leaving crashed", instance.Name);
                    _restartAttempts.Remove(instance.Id);
                    break;

                case RestartPolicy.OnCrash:
                case RestartPolicy.Always:
                    await TryRestartAsync(instance, scope.ServiceProvider, ct);
                    break;
                default:
                    logger.LogWarning("Server {Name}: Unknown restart policy {Policy}", instance.Name, instance.RestartPolicy);
                    break;
            }
        }

        foreach (var instance in runningInstances.Where(instance => processManager.IsProcessRunning(instance.ProcessId!.Value)))
        {
            consoleLogTailer.StartFollowing(
                instance,
                instance.StartedAt ?? DateTime.UtcNow,
                replayExistingContent: false);
        }
    }

    private async Task TryRestartAsync(
        Core.Models.ServerInstance instance,
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
            _restartAttempts.Remove(instance.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Server {Name}: restart attempt {Attempt} failed", instance.Name, attempts + 1);
        }
    }
}
