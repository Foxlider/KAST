using System.Collections.Concurrent;
using System.Threading.Channels;
using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public sealed class ModDownloadQueueService(
    IServiceScopeFactory scopeFactory,
    IContentOrchestrator orchestrator,
    ContentProgressTracker tracker,
    ILogger<ModDownloadQueueService> logger) : BackgroundService, IModDownloadQueueService
{
    private const int DefaultMaxRetries = 3;
    private static readonly TimeSpan DispatchInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProgressPersistInterval = TimeSpan.FromSeconds(1);

    private readonly Channel<bool> _wakeups = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeByModId = new();

    public int ActiveCount => _activeByModId.Count;

    public bool IsActive(int modId)
        => _activeByModId.ContainsKey(modId) ||
           orchestrator.IsRunning(ContentProgressTracker.ModKey(modId));

    public async Task<DownloadTask?> QueueDownloadAsync(int modId, bool isUpdate, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        var mod = await db.Mods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modId, ct);
        if (mod is null || mod.Source != ModSource.SteamWorkshop)
            return null;

        var existing = await db.DownloadTasks
            .Where(t => t.ModId == modId &&
                        (t.Status == DownloadStatus.Queued ||
                         t.Status == DownloadStatus.Downloading ||
                         t.Status == DownloadStatus.Validating))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return existing;

        var settings = await settingsService.GetSettingsAsync(ct);
        var destinationPath = ResolveDestinationPath(mod, settings.ModsDirectory, isUpdate);
        var task = new DownloadTask
        {
            ModId = mod.Id,
            WorkshopId = mod.WorkshopId,
            Name = mod.Name,
            IsUpdate = isUpdate,
            DestinationPath = destinationPath,
            TotalBytes = mod.ExpectedSizeBytes,
            Status = DownloadStatus.Queued,
            MaxRetries = DefaultMaxRetries,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.DownloadTasks.Add(task);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Queued mod download {TaskId} for mod {ModId} ({WorkshopId})", task.Id, mod.Id, mod.WorkshopId);
        SignalDispatcher();
        return task;
    }

    public async Task<int> QueueAllOutdatedAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var candidates = await db.Mods.AsNoTracking()
            .Where(m => m.Source == ModSource.SteamWorkshop &&
                        (m.Status == ModStatus.NotInstalled ||
                         m.Status == ModStatus.UpdateAvailable ||
                         m.Status == ModStatus.Error))
            .OrderBy(m => m.ExpectedSizeBytes)
            .Select(m => new { m.Id, m.Status })
            .ToListAsync(ct);

        var queued = 0;
        foreach (var mod in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var isUpdate = mod.Status != ModStatus.NotInstalled;
            if (await QueueDownloadAsync(mod.Id, isUpdate, ct) is not null)
                queued++;
        }

        return queued;
    }

    public async Task<bool> CancelAsync(int modId, CancellationToken ct = default)
    {
        var cancelled = false;
        if (_activeByModId.TryGetValue(modId, out var cts))
        {
            orchestrator.Cancel(ContentProgressTracker.ModKey(modId));
            cts.Cancel();
            cancelled = true;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var queuedTasks = await db.DownloadTasks
            .Where(t => t.ModId == modId && t.Status == DownloadStatus.Queued)
            .ToListAsync(ct);
        foreach (var task in queuedTasks)
        {
            task.Status = DownloadStatus.Cancelled;
            task.ErrorMessage = "Cancelled";
            task.CompletedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
            cancelled = true;
        }

        if (queuedTasks.Count > 0)
            await db.SaveChangesAsync(ct);

        return cancelled;
    }

    public async Task<int> CancelAllAsync(CancellationToken ct = default)
    {
        var cancelled = 0;
        foreach (var modId in _activeByModId.Keys.ToList())
        {
            if (await CancelAsync(modId, ct))
                cancelled++;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var queuedTasks = await db.DownloadTasks
            .Where(t => t.Status == DownloadStatus.Queued)
            .ToListAsync(ct);
        foreach (var task in queuedTasks)
        {
            task.Status = DownloadStatus.Cancelled;
            task.ErrorMessage = "Cancelled";
            task.CompletedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
        }

        if (queuedTasks.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            cancelled += queuedTasks.Count;
        }

        return cancelled;
    }

    public async Task<DownloadQueueSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var tasks = await db.DownloadTasks
            .AsNoTracking()
            .OrderByDescending(t => t.Status == DownloadStatus.Downloading)
            .ThenByDescending(t => t.Status == DownloadStatus.Queued)
            .ThenByDescending(t => t.UpdatedAt)
            .Take(250)
            .ToListAsync(ct);

        foreach (var task in tasks)
            ApplyLiveState(task);

        return new DownloadQueueSnapshot(
            ActiveCount,
            tasks.Count(t => t.Status == DownloadStatus.Queued),
            tasks.Count(t => t.Status == DownloadStatus.Completed),
            tasks.Count(t => t.Status == DownloadStatus.Failed),
            tasks.Count(t => t.Status == DownloadStatus.Cancelled),
            tasks);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Mod download queue service started");
        await ReconcileStartupStateAsync(stoppingToken);
        SignalDispatcher();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await DispatchQueuedJobsAsync(stoppingToken);

                var timerTask = Task.Delay(DispatchInterval, stoppingToken);
                var wakeTask = _wakeups.Reader.WaitToReadAsync(stoppingToken).AsTask();
                var completed = await Task.WhenAny(timerTask, wakeTask);
                if (completed == wakeTask && await wakeTask)
                {
                    while (_wakeups.Reader.TryRead(out _))
                    {
                    }
                }
                else
                {
                    await timerTask;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var active in _activeByModId.Values)
                active.Cancel();
            logger.LogInformation("Mod download queue service stopped");
        }
    }

    private async Task ReconcileStartupStateAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();

        var staleTasks = await db.DownloadTasks
            .Where(t => t.Status == DownloadStatus.Downloading || t.Status == DownloadStatus.Validating)
            .ToListAsync(ct);

        foreach (var task in staleTasks)
        {
            task.Status = task.RetryCount < task.MaxRetries
                ? DownloadStatus.Queued
                : DownloadStatus.Failed;
            task.ErrorMessage = "Recovered after KAST restart.";
            task.UpdatedAt = DateTime.UtcNow;
            if (task.Status == DownloadStatus.Failed)
                task.CompletedAt = DateTime.UtcNow;
        }

        var staleMods = await db.Mods
            .Where(m => m.Status == ModStatus.Downloading || m.Status == ModStatus.Updating)
            .ToListAsync(ct);
        foreach (var mod in staleMods)
        {
            mod.Status = !string.IsNullOrWhiteSpace(mod.LocalPath) && Directory.Exists(mod.LocalPath)
                ? ModStatus.UpdateAvailable
                : ModStatus.NotInstalled;
        }

        if (staleTasks.Count > 0 || staleMods.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogWarning(
                "Recovered {TaskCount} interrupted download task(s) and {ModCount} interrupted mod state(s)",
                staleTasks.Count,
                staleMods.Count);
        }
    }

    private async Task DispatchQueuedJobsAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var maxActive = Math.Clamp((await settings.GetSettingsAsync(stoppingToken)).ParallelModDownloads, 1, 16);
        var capacity = maxActive - _activeByModId.Count;
        if (capacity <= 0)
            return;

        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var jobs = await db.DownloadTasks
            .AsNoTracking()
            .Where(t => t.Status == DownloadStatus.Queued)
            .OrderBy(t => t.CreatedAt)
            .Take(capacity)
            .ToListAsync(stoppingToken);

        foreach (var job in jobs)
        {
            if (_activeByModId.ContainsKey(job.ModId))
                continue;

            var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            if (!_activeByModId.TryAdd(job.ModId, linked))
            {
                linked.Dispose();
                continue;
            }

            _ = RunJobGuardedAsync(job.Id, job.ModId, linked);
        }
    }

    private async Task RunJobGuardedAsync(int taskId, int modId, CancellationTokenSource cts)
    {
        try
        {
            await ProcessJobAsync(taskId, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            await MarkTaskCancelledAsync(taskId, "Cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled mod download queue failure for task {TaskId}", taskId);
            await MarkTaskFailedAsync(taskId, ex);
        }
        finally
        {
            _activeByModId.TryRemove(modId, out _);
            cts.Dispose();
            SignalDispatcher();
        }
    }

    private async Task ProcessJobAsync(int taskId, CancellationToken ct)
    {
        DownloadTask task;
        SteamMod mod;
        KastSettings settings;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
            task = await db.DownloadTasks.FirstAsync(t => t.Id == taskId, ct);
            mod = await db.Mods.FirstAsync(m => m.Id == task.ModId, ct);
            settings = await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetSettingsAsync(ct);

            task.Status = DownloadStatus.Downloading;
            task.StartedAt ??= DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
            task.ErrorMessage = null;
            task.DestinationPath = string.IsNullOrWhiteSpace(task.DestinationPath)
                ? ResolveDestinationPath(mod, settings.ModsDirectory, task.IsUpdate)
                : task.DestinationPath;
            task.TotalBytes = mod.ExpectedSizeBytes;
            mod.Status = task.IsUpdate ? ModStatus.Updating : ModStatus.Downloading;
            await db.SaveChangesAsync(ct);
            await scope.ServiceProvider.GetRequiredService<IAppEventBroadcaster>()
                .BroadcastModStatusChangedAsync(new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));
        }

        Exception? observedError = null;
        var installTask = orchestrator.RunModInstallAsync(
            mod.Id,
            ContentType.SteamMod,
            task.DestinationPath,
            mod.WorkshopId,
            expectedSizeBytes: mod.ExpectedSizeBytes,
            onComplete: (sp, state) => CompleteModInstallAsync(sp, mod.Id, task.DestinationPath, state),
            onError: (sp, _, ex) =>
            {
                observedError = ex;
                return FailModInstallAsync(sp, mod.Id, ex);
            },
            maxParallelDownloads: Math.Clamp(settings.ParallelDownloads, 1, 64),
            maxParallelModDownloads: 1,
            ct: ct);

        var persistTask = PersistProgressUntilCompleteAsync(taskId, mod.Id, installTask, ct);
        var state = await installTask;
        await persistTask;

        if (state.IsComplete)
        {
            await MarkTaskCompletedAsync(taskId, state);
            return;
        }

        if (ct.IsCancellationRequested || observedError is OperationCanceledException)
        {
            await MarkTaskCancelledAsync(taskId, "Cancelled");
            return;
        }

        if (observedError is not null && await TryQueueRetryAsync(taskId, observedError))
            return;

        await MarkTaskFailedAsync(taskId, observedError ?? new IOException(state.ErrorMessage ?? "Download failed."));
    }

    private async Task PersistProgressUntilCompleteAsync(
        int taskId,
        int modId,
        Task<ContentInstallState> installTask,
        CancellationToken ct)
    {
        while (!installTask.IsCompleted)
        {
            var completed = await Task.WhenAny(installTask, Task.Delay(ProgressPersistInterval, ct));
            if (completed == installTask)
                break;

            ct.ThrowIfCancellationRequested();
            await PersistProgressAsync(taskId, modId, ct);
        }

        await PersistProgressAsync(taskId, modId, CancellationToken.None);
    }

    private async Task PersistProgressAsync(int taskId, int modId, CancellationToken ct)
    {
        var state = tracker.Get(ContentProgressTracker.ModKey(modId));
        if (state is null)
            return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var task = await db.DownloadTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return;

        ApplyStateToTask(task, state);
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkTaskCompletedAsync(int taskId, ContentInstallState state)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var task = await db.DownloadTasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null)
            return;

        ApplyStateToTask(task, state);
        task.Status = DownloadStatus.Completed;
        task.ProgressPercent = 100;
        task.CompletedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        task.ErrorMessage = null;
        task.InstalledManifestId = state.InstalledManifestId;
        await db.SaveChangesAsync();
    }

    private async Task<bool> TryQueueRetryAsync(int taskId, Exception ex)
    {
        if (!IsRetryable(ex))
            return false;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var task = await db.DownloadTasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null || task.RetryCount >= task.MaxRetries)
            return false;

        task.RetryCount++;
        task.Status = DownloadStatus.Queued;
        task.ErrorMessage = $"Retry {task.RetryCount}/{task.MaxRetries}: {ex.Message}";
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogWarning(ex, "Retrying mod download task {TaskId} ({Retry}/{Max})", task.Id, task.RetryCount, task.MaxRetries);
        SignalDispatcher();
        return true;
    }

    private async Task MarkTaskFailedAsync(int taskId, Exception ex)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var task = await db.DownloadTasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null)
            return;

        task.Status = DownloadStatus.Failed;
        task.ErrorMessage = ex.Message;
        task.CompletedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task MarkTaskCancelledAsync(int taskId, string message)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var task = await db.DownloadTasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null)
            return;

        task.Status = DownloadStatus.Cancelled;
        task.ErrorMessage = message;
        task.CompletedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task CompleteModInstallAsync(
        IServiceProvider sp,
        int modId,
        string destinationPath,
        ContentInstallState state)
    {
        var modService = sp.GetRequiredService<IModService>();
        var fs = sp.GetRequiredService<IFileSystemService>();
        var broadcaster = sp.GetRequiredService<IAppEventBroadcaster>();

        var mod = await modService.GetModByIdAsync(modId, CancellationToken.None);
        if (mod is null)
            return;

        mod.Status = ModStatus.Installed;
        mod.LocalPath = Path.GetFullPath(destinationPath);
        mod.SizeBytes = fs.GetDirectorySize(destinationPath);
        mod.LastUpdatedLocal = DateTime.UtcNow;
        if (state.InstalledManifestId > 0)
        {
            mod.InstalledManifestId = state.InstalledManifestId;
            mod.SteamManifestId = state.InstalledManifestId;
        }

        await modService.UpdateModAsync(mod, CancellationToken.None);
        await broadcaster.BroadcastModStatusChangedAsync(new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));
    }

    private static async Task FailModInstallAsync(IServiceProvider sp, int modId, Exception ex)
    {
        var modService = sp.GetRequiredService<IModService>();
        var broadcaster = sp.GetRequiredService<IAppEventBroadcaster>();

        var mod = await modService.GetModByIdAsync(modId, CancellationToken.None);
        if (mod is null)
            return;

        mod.Status = ex is OperationCanceledException
            ? RecoverableStatus(mod)
            : ModStatus.Error;

        await modService.UpdateModAsync(mod, CancellationToken.None);
        await broadcaster.BroadcastModStatusChangedAsync(new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));
    }

    private static void ApplyStateToTask(DownloadTask task, ContentInstallState state)
    {
        task.ProgressPercent = Math.Clamp(state.OverallProgress, 0, 100);
        var files = state.FileProgress;
        if (files.Count > 0)
        {
            task.BytesDownloaded = files.Sum(f => Math.Max(0, f.BytesDownloaded));
            task.TotalBytes = Math.Max(task.TotalBytes, files.Sum(f => Math.Max(0, f.TotalBytes)));
        }

        task.InstalledManifestId = state.InstalledManifestId;
        task.ErrorMessage = state.ErrorMessage;
        task.UpdatedAt = DateTime.UtcNow;
    }

    private void ApplyLiveState(DownloadTask task)
    {
        var state = tracker.Get(ContentProgressTracker.ModKey(task.ModId));
        if (state is null)
            return;

        ApplyStateToTask(task, state);
        if (state.IsDownloading)
            task.Status = DownloadStatus.Downloading;
        if (state.IsComplete)
            task.Status = DownloadStatus.Completed;
    }

    private static string ResolveDestinationPath(SteamMod mod, string modsDirectory, bool isUpdate)
    {
        if (isUpdate && !string.IsNullOrWhiteSpace(mod.LocalPath))
            return mod.LocalPath;

        return Path.Join(modsDirectory, mod.WorkshopId.ToString());
    }

    private static ModStatus RecoverableStatus(SteamMod mod)
        => mod.InstalledManifestId > 0 || !string.IsNullOrWhiteSpace(mod.LocalPath)
            ? ModStatus.UpdateAvailable
            : ModStatus.NotInstalled;

    private static bool IsRetryable(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is IOException or HttpRequestException or TimeoutException)
                return true;
        }

        return false;
    }

    private void SignalDispatcher()
        => _wakeups.Writer.TryWrite(true);
}
