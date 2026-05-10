using System.Globalization;
using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KAST.UI.Services;

/// <summary>
/// Evaluates server instance schedules every minute and starts/stops
/// instances based on their AutoStartTime and AutoStopTime (HH:mm format).
/// </summary>
public class SchedulingBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<SchedulingBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Scheduling background service started");

        using var timer = new PeriodicTimer(CheckInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            { await EvaluateSchedulesAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            { break; }
            catch (Exception ex)
            { logger.LogWarning(ex, "Error evaluating schedules"); }
        }

        logger.LogInformation("Scheduling background service stopped");
    }

    private async Task EvaluateSchedulesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var serverService = scope.ServiceProvider.GetRequiredService<IServerInstanceService>();

        var scheduledInstances = await db.ServerInstances
            .Where(s => s.ScheduleEnabled)
            .ToListAsync(ct);

        var now = DateTime.Now;
        var currentTime = now.ToString("HH:mm");

        foreach (var instance in scheduledInstances)
        {
            if (!string.IsNullOrEmpty(instance.AutoStartTime)
                && IsTimeMatch(currentTime, instance.AutoStartTime)
                && instance.Status == ServerInstanceStatus.Stopped)
            {
                logger.LogInformation(
                    "Schedule: starting server {Name} (scheduled at {Time})",
                    instance.Name, instance.AutoStartTime);
                try
                { await serverService.StartInstanceAsync(instance.Id, ct); }
                catch (Exception ex)
                { logger.LogError(ex, "Schedule: failed to start server {Name}", instance.Name); }
            }

            if (string.IsNullOrEmpty(instance.AutoStopTime)
                || !IsTimeMatch(currentTime, instance.AutoStopTime)
                || instance.Status != ServerInstanceStatus.Running) continue;
            
            logger.LogInformation(
                "Schedule: stopping server {Name} (scheduled at {Time})",
                instance.Name, instance.AutoStopTime);
            try { await serverService.StopInstanceAsync(instance.Id, ct);}
            catch (Exception ex)
            { logger.LogError(ex, "Schedule: failed to stop server {Name}", instance.Name); }
        }
    }

    private static bool IsTimeMatch(string currentTime, string scheduledTime)
    {
        if (!TimeOnly.TryParseExact(scheduledTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var scheduled))
            return false;
        if (!TimeOnly.TryParseExact(currentTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var current))
            return false;

        return current == scheduled;
    }
}
