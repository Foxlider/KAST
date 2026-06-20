using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KAST.UI.Services;

/// <summary>
/// Monitors running server instances for crashed processes and restarts them per RestartPolicy.
/// Also detects and cleans up orphaned headless client processes.
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

        // ── 1. Running instances with dead processes ──

        var runningInstances = await db.ServerInstances
            .Where(s => s.Status == ServerInstanceStatus.Running && s.ProcessId != null)
            .ToListAsync(ct);

        foreach (var instance in runningInstances.Where(instance => !processManager.IsProcessRunning(instance.ProcessId!.Value)))
        {
            var deadPid = instance.ProcessId!.Value;
            logger.LogWarning("Server {Name} (PID {Pid}) has crashed", instance.Name, deadPid);
            await consoleLogTailer.StopFollowingAsync(instance.Id);

            CloseHistoryEntry(db, instance.Id, deadPid, "Crashed");

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

        // ── 2. Re-attach log tailers for alive instances ──

        foreach (var instance in runningInstances.Where(instance => processManager.IsProcessRunning(instance.ProcessId!.Value)))
        {
            consoleLogTailer.StartFollowing(
                instance,
                instance.StartedAt ?? DateTime.UtcNow,
                replayExistingContent: false);
        }

        // ── 3. Orphaned headless clients ──

        var orphanedHCs = await db.HeadlessClients
            .Where(h => h.Status == ServerInstanceStatus.Running && h.ProcessId != null)
            .ToListAsync(ct);

        foreach (var hc in orphanedHCs.Where(h => !processManager.IsProcessRunning(h.ProcessId!.Value)))
        {
            logger.LogWarning("Headless client {Id} (PID {Pid}) was orphaned", hc.Id, hc.ProcessId);
            hc.ProcessId = null;
            hc.Status = ServerInstanceStatus.Stopped;
        }

        if (orphanedHCs.Any(h => h.ProcessId == null))
            await db.SaveChangesAsync(ct);

        // ── 4. Retry Crashed instances with pending restart attempts ──

        var retryIds = _restartAttempts.Keys.ToList();
        if (retryIds.Count > 0)
        {
            var crashedInstances = await db.ServerInstances
                .Where(s => retryIds.Contains(s.Id) && s.Status == ServerInstanceStatus.Crashed)
                .ToListAsync(ct);

            foreach (var instance in crashedInstances)
                await TryRestartAsync(instance, scope.ServiceProvider, ct);
        }

        // ── 5. Restarting instances stuck from a previous crash ──

        var stuckRestarting = await db.ServerInstances
            .Where(s => s.Status == ServerInstanceStatus.Restarting)
            .ToListAsync(ct);

        foreach (var instance in stuckRestarting)
        {
            logger.LogWarning("Server {Name} was stuck in Restarting state, resetting to Crashed", instance.Name);
            instance.Status = ServerInstanceStatus.Crashed;
        }

        if (stuckRestarting.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private async Task TryRestartAsync(
        ServerInstance instance,
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

    private static void CloseHistoryEntry(KastDbContext db, int instanceId, int processId, string reason)
    {
        var history = db.ServerInstanceProcessHistories
            .Where(h => h.ServerInstanceId == instanceId && h.ProcessId == processId && h.EndedAt == null)
            .OrderByDescending(h => h.StartedAt)
            .FirstOrDefault();
        if (history is not null)
        {
            history.EndedAt = DateTime.UtcNow;
            history.TerminationReason = reason;
        }
    }
}
