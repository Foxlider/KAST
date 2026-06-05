using System.Collections.Concurrent;
using System.Diagnostics;
using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services.Content;

/// <summary>
/// Singleton orchestrator that queues content installs, manages cancellation,
/// and delegates to the appropriate <see cref="IContentInstaller"/>.
/// The UI layer only signals this service — all download logic runs here in Infrastructure.
/// </summary>
public class ContentOrchestrator(
    IServiceScopeFactory scopeFactory,
    ContentProgressTracker tracker,
    IEnumerable<IContentInstaller> installers,
    ILogger<ContentOrchestrator> logger,
    IOutputSanitizer sanitizer) : IContentOrchestrator
{
    private readonly ConcurrentDictionary<string, ActiveInstall> _active = new();
    private readonly object _steamQueueLock = new();
    private readonly Queue<SteamQueueEntry> _steamQueue = new();
    private int _activeSteamModInstalls;
    private bool _steamServerInstallActive;
    private readonly Dictionary<ContentType, IContentInstaller> _installers =
        installers.ToDictionary(i => i.Type);

    public bool IsRunning(string key) => _active.ContainsKey(key);

    public ContentInstallState? GetState(string key)
        => _active.TryGetValue(key, out var active) ? active.State : tracker.Get(key);

    // ── Server install ───────────────────────────────────────────────────────

    public ContentInstallState StartServerInstall(int instanceId, string installPath, ServerInstance instance, int maxParallelDownloads)
    {
        var key = ContentProgressTracker.ServerKey(instanceId);

        using var activity = KastActivitySources.Content.StartActivity(
            "kast.content.queued", ActivityKind.Internal);
        activity?.SetTag("content.key", key);
        activity?.SetTag("content.type", ContentType.Server.ToString());
        activity?.SetTag("content.instance_id", instanceId);
        activity?.SetTag("content.instance_name", instance.Name);
        activity?.SetTag("content.parallel_workers", maxParallelDownloads);

        if (_active.TryGetValue(key, out var existing))
        {
            activity?.SetTag("content.queued", false);
            activity?.AddEvent(new ActivityEvent("install.already_running"));
            return existing.State;
        }

        var installer = _installers[ContentType.Server];
        var request = new ContentInstallRequest
        {
            Type = ContentType.Server,
            DestinationPath = installPath,
            AppId = ServerInstaller.Arma3ServerAppId,
            ServerInstanceId = instanceId,
            Instance = instance,
            MaxParallelDownloads = maxParallelDownloads
        };

        var steps = installer.PlanSteps(request);
        var state = new ContentInstallState
        {
            Key = key,
            Type = ContentType.Server,
            Label = instance.Name,
            Steps = steps.ToList()
        };
        state.IsDownloading = true;
        state.AddLog($"Queued download for instance {instanceId}.");

        var cts = new CancellationTokenSource();
        var operation = new ActiveInstall(cts, state);
        try
        {
            if (!_active.TryAdd(key, operation))
            {
                cts.Dispose();
                activity?.SetTag("content.queued", false);
                activity?.AddEvent(new ActivityEvent("install.already_running"));
                return _active.TryGetValue(key, out existing) ? existing.State : tracker.Get(key) ?? state;
            }
        }
        catch
        {
            cts.Dispose();
            throw;
        }

        tracker.Set(state);

        activity?.SetTag("content.queued", true);
        activity?.SetTag("content.steps", steps.Count);

        _ = Task.Run(() => RunAsync(key, request, state, cts.Token));
        return state;
    }

    // ── Mod install ──────────────────────────────────────────────────────────

    public ContentInstallState StartModInstall(int modId, ContentType type, string destinationPath,
        long workshopId = 0, string? sourcePath = null,
        long expectedSizeBytes = 0,
        Func<IServiceProvider, ContentInstallState, Task>? onStarted = null,
        Func<IServiceProvider, ContentInstallState, Task>? onComplete = null,
        Func<IServiceProvider, ContentInstallState, Exception, Task>? onError = null,
        int maxParallelDownloads = 4,
        int maxParallelModDownloads = 1)
    {
        var key = ContentProgressTracker.ModKey(modId);

        using var activity = KastActivitySources.Content.StartActivity(
            "kast.content.queued", ActivityKind.Internal);
        activity?.SetTag("content.key", key);
        activity?.SetTag("content.type", type.ToString());
        activity?.SetTag("content.mod_id", modId);
        if (workshopId != 0)
            activity?.SetTag("content.workshop_id", workshopId);

        if (_active.TryGetValue(key, out var existing))
        {
            activity?.SetTag("content.queued", false);
            activity?.AddEvent(new ActivityEvent("install.already_running"));
            return existing.State;
        }

        var installer = _installers[type];
        var request = new ContentInstallRequest
        {
            Type = type,
            DestinationPath = destinationPath,
            ModId = modId,
            WorkshopId = workshopId,
            ExpectedSizeBytes = expectedSizeBytes,
            SourcePath = sourcePath,
            MaxParallelDownloads = maxParallelDownloads,
            MaxParallelModDownloads = maxParallelModDownloads
        };

        var steps = installer.PlanSteps(request);
        var state = new ContentInstallState
        {
            Key = key,
            Type = type,
            Label = $"Mod {modId}",
            Steps = steps.ToList()
        };
        state.IsDownloading = true;
        state.AddLog($"Queued install for mod {modId}.");

        var cts = new CancellationTokenSource();
        var operation = new ActiveInstall(cts, state);
        try
        {
            if (!_active.TryAdd(key, operation))
            {
                cts.Dispose();
                activity?.SetTag("content.queued", false);
                activity?.AddEvent(new ActivityEvent("install.already_running"));
                return _active.TryGetValue(key, out existing) ? existing.State : tracker.Get(key) ?? state;
            }
        }
        catch
        {
            cts.Dispose();
            throw;
        }

        tracker.Set(state);

        activity?.SetTag("content.queued", true);
        activity?.SetTag("content.steps", steps.Count);

        _ = Task.Run(() => RunAsync(key, request, state, cts.Token, onStarted, onComplete, onError));
        return state;
    }

    // ── Cancel ───────────────────────────────────────────────────────────────

    public void Cancel(string key)
    {
        if (_active.TryGetValue(key, out var active))
            active.Cancellation.Cancel();
    }

    // ── Validation ───────────────────────────────────────────────────────────

    public IReadOnlyList<ContentValidationResult> Validate(ContentType type, ContentInstallRequest request)
    {
        return !_installers.TryGetValue(type, out var installer) ? [] : installer.Validate(request);
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private async Task RunAsync(string key, ContentInstallRequest request, ContentInstallState state,
        CancellationToken ct,
        Func<IServiceProvider, ContentInstallState, Task>? onStarted = null,
        Func<IServiceProvider, ContentInstallState, Task>? onComplete = null,
        Func<IServiceProvider, ContentInstallState, Exception, Task>? onError = null)
    {
        // Detach from any ambient HTTP/SignalR span so this long-running background
        // task becomes a clean root trace rather than a child of a short-lived request.
        Activity.Current = null;

        using var activity = KastActivitySources.Content.StartActivity(
            "kast.content.install", ActivityKind.Internal);
        activity?.SetTag("content.key", key);
        activity?.SetTag("content.type", request.Type.ToString());
        activity?.SetTag("content.steps", state.Steps.Count);
        if (request.Type == ContentType.Server)
            activity?.SetTag("content.instance_id", request.ServerInstanceId);

        try
        {
            if (request.Type == ContentType.Server)
                await UpdateServerStatusAsync(request.ServerInstanceId, ServerInstanceStatus.Downloading, CancellationToken.None);

            var installer = _installers[request.Type];
            await RunInstallerAsync(installer, request, state, ct, onStarted);
            activity?.AddEvent(new ActivityEvent("install.completed"));

            await HandleServerInstallCompleteAsync(request, state);
            await HandleCompleteCallbackAsync(onComplete, state);

            state.AddLog("All steps complete.");
            state.IsDownloading = false;
            state.IsComplete = true;
            state.NotifyChanged();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The user explicitly cancelled via Cancel(key) — expected path
            activity?.SetStatus(ActivityStatusCode.Error, "Cancelled by user");
            activity?.AddEvent(new ActivityEvent("install.cancelled"));
            await HandleCancellationAsync(key, request, state, onError);
        }
        catch (OperationCanceledException oce)
        {
            // Spurious TaskCanceledException from an HTTP timeout or SteamKit2 internals.
            // Treat it as a real error so the user sees a meaningful message.
            var safeMessage = sanitizer.Sanitize(oce.Message);
            activity?.SetStatus(ActivityStatusCode.Error, safeMessage);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                ["exception.type"]    = oce.GetType().FullName ?? oce.GetType().Name,
                ["exception.message"] = safeMessage
            }));
            await HandleErrorAsync(key, request, state,
                new IOException($"Network timeout or transient failure during download. Details: {safeMessage}", oce),
                onError);
        }
        catch (Exception ex)
        {
            var safeMessage = sanitizer.Sanitize(ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, safeMessage);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                ["exception.type"]    = ex.GetType().FullName ?? ex.GetType().Name,
                ["exception.message"] = safeMessage
            }));
            await HandleErrorAsync(key, request, state, ex, onError);
        }
        finally
        {
            if (_active.TryRemove(key, out var active))
                active.Cancellation.Dispose();
        }
    }

    private async Task RunInstallerAsync(IContentInstaller installer, ContentInstallRequest request,
        ContentInstallState state, CancellationToken ct,
        Func<IServiceProvider, ContentInstallState, Task>? onStarted)
    {
        if (request.Type is not (ContentType.SteamMod or ContentType.Server))
        {
            await HandleStartedCallbackAsync(onStarted, state);
            await installer.InstallAsync(request, state, ct);
            return;
        }

        var slotName = request.Type == ContentType.SteamMod
            ? "Steam mod download slot"
            : "Steam content slot";
        state.AddLog($"Queued for {slotName}...");
        using var lease = await WaitForSteamContentTurnAsync(request, state.Key, ct);
        try
        {
            state.AddLog($"{slotName} acquired.");
            await HandleStartedCallbackAsync(onStarted, state);
            await installer.InstallAsync(request, state, ct);
        }
        finally
        {
        }
    }

    private async Task<IDisposable> WaitForSteamContentTurnAsync(ContentInstallRequest request, string key, CancellationToken ct)
    {
        var entry = new SteamQueueEntry(
            key,
            request.Type,
            Math.Clamp(request.MaxParallelModDownloads, 1, 16));
        entry.Cancellation = ct.Register(() => CancelSteamQueueEntry(entry));

        lock (_steamQueueLock)
        {
            _steamQueue.Enqueue(entry);
            TryStartNextSteamQueueEntryLocked();
        }

        await entry.Ready.Task.WaitAsync(ct);
        return new SteamQueueLease(this, entry);
    }

    private void CancelSteamQueueEntry(SteamQueueEntry entry)
    {
        lock (_steamQueueLock)
        {
            if (entry.Started)
                return;

            entry.Cancelled = true;
            entry.Ready.TrySetCanceled();
            TryStartNextSteamQueueEntryLocked();
        }
    }

    private void CompleteSteamQueueEntry(SteamQueueEntry entry)
    {
        lock (_steamQueueLock)
        {
            if (entry.Type == ContentType.SteamMod)
                _activeSteamModInstalls = Math.Max(0, _activeSteamModInstalls - 1);
            else if (entry.Type == ContentType.Server)
                _steamServerInstallActive = false;

            entry.Cancellation.Dispose();
            TryStartNextSteamQueueEntryLocked();
        }
    }

    private void TryStartNextSteamQueueEntryLocked()
    {
        while (_steamQueue.Count > 0)
        {
            var next = _steamQueue.Peek();
            if (next.Cancelled)
            {
                _steamQueue.Dequeue();
                next.Cancellation.Dispose();
                continue;
            }

            if (!CanStartSteamQueueEntry(next))
                return;

            _steamQueue.Dequeue();
            next.Started = true;
            if (next.Type == ContentType.SteamMod)
                _activeSteamModInstalls++;
            else if (next.Type == ContentType.Server)
                _steamServerInstallActive = true;
            next.Ready.TrySetResult();
        }
    }

    private bool CanStartSteamQueueEntry(SteamQueueEntry entry)
    {
        return entry.Type switch
        {
            ContentType.SteamMod => !_steamServerInstallActive &&
                                    _activeSteamModInstalls < entry.MaxParallelModDownloads,
            ContentType.Server => !_steamServerInstallActive && _activeSteamModInstalls == 0,
            _ => true
        };
    }

    private async Task HandleServerInstallCompleteAsync(ContentInstallRequest request, ContentInstallState state)
    {
        if (request.Type == ContentType.Server)
        {
            var buildId = DateTime.UtcNow.ToString("yyyyMMddHHmm");
            state.AddLog($"Build stamp: {buildId}");
            await UpdateServerInstallAsync(request.ServerInstanceId, DateTime.UtcNow, buildId, CancellationToken.None);
        }
    }

    private async Task HandleCompleteCallbackAsync(Func<IServiceProvider, ContentInstallState, Task>? onComplete, ContentInstallState state)
    {
        if (onComplete is not null)
        {
            using var scope = scopeFactory.CreateScope();
            await onComplete(scope.ServiceProvider, state);
        }
    }

    private async Task HandleStartedCallbackAsync(Func<IServiceProvider, ContentInstallState, Task>? onStarted, ContentInstallState state)
    {
        if (onStarted is not null)
        {
            using var scope = scopeFactory.CreateScope();
            await onStarted(scope.ServiceProvider, state);
        }
    }

    private async Task HandleCancellationAsync(string key, ContentInstallRequest request, ContentInstallState state,
        Func<IServiceProvider, ContentInstallState, Exception, Task>? onError)
    {
        logger.LogInformation("Content install cancelled by user: {Key}", key);
        state.AddLog("Cancelled.");
        state.IsDownloading = false;
        state.ErrorMessage = "Cancelled";

        if (state.CurrentStep is { Status: ContentStepStatus.InProgress })
            state.FailStep(state.CurrentStepIndex, "Cancelled");
        else
        {
            var firstPending = state.Steps.FindIndex(s => s.Status == ContentStepStatus.Pending);
            if (firstPending >= 0)
                state.FailStep(firstPending, "Cancelled");
        }

        await HandleErrorCallbackAsync(onError, state, new OperationCanceledException());
        state.NotifyChanged();

        if (request.Type == ContentType.Server)
            await UpdateServerStatusAsync(request.ServerInstanceId, ServerInstanceStatus.Stopped, CancellationToken.None);
    }

    private async Task HandleErrorAsync(string key, ContentInstallRequest request, ContentInstallState state, Exception ex,
        Func<IServiceProvider, ContentInstallState, Exception, Task>? onError)
    {
        var safeMessage = sanitizer.Sanitize(ex.Message);
        logger.LogError(ex, "Content install failed: {Key}", key);
        state.AddLog($"ERROR: {safeMessage}");
        state.IsDownloading = false;
        state.ErrorMessage = safeMessage;

        if (state.CurrentStep is { Status: ContentStepStatus.InProgress })
            state.FailStep(state.CurrentStepIndex, safeMessage);
        else
        {
            // Error occurred before any step was started (e.g. Steam connection failure)
            var firstPending = state.Steps.FindIndex(s => s.Status == ContentStepStatus.Pending);
            if (firstPending >= 0)
                state.FailStep(firstPending, safeMessage);
        }

        await HandleErrorCallbackAsync(onError, state, ex);
        state.NotifyChanged();

        if (request.Type == ContentType.Server)
            await UpdateServerStatusAsync(request.ServerInstanceId, ServerInstanceStatus.Stopped, CancellationToken.None);
    }

    private async Task HandleErrorCallbackAsync(Func<IServiceProvider, ContentInstallState, Exception, Task>? onError, ContentInstallState state, Exception ex)
    {
        if (onError is not null)
        {
            using var scope = scopeFactory.CreateScope();
            await onError(scope.ServiceProvider, state, ex);
        }
    }

    private async Task UpdateServerStatusAsync(int instanceId, ServerInstanceStatus status, CancellationToken ct)
    {
        using var activity = KastActivitySources.Content.StartActivity(
            "kast.content.server_status_update", ActivityKind.Internal);
        activity?.SetTag("instance.id", instanceId);
        activity?.SetTag("server.status", status.ToString());

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var instance = await db.ServerInstances.FindAsync([instanceId], ct);
        if (instance is null)
        {
            activity?.SetTag("instance.found", false);
            return;
        }
        instance.Status = status;
        await db.SaveChangesAsync(ct);
        activity?.SetTag("instance.found", true);
    }

    private async Task UpdateServerInstallAsync(int instanceId, DateTime installedAt, string buildId, CancellationToken ct)
    {
        using var activity = KastActivitySources.Content.StartActivity(
            "kast.content.server_install_record", ActivityKind.Internal);
        activity?.SetTag("instance.id", instanceId);
        activity?.SetTag("instance.build", buildId);
        activity?.SetTag("instance.installed_at", installedAt.ToString("O"));

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var instance = await db.ServerInstances.FindAsync([instanceId], ct);
        if (instance is null)
        {
            activity?.SetTag("instance.found", false);
            return;
        }
        instance.InstalledAt = installedAt;
        instance.InstalledBuildId = buildId;
        instance.Status = ServerInstanceStatus.Stopped;
        await db.SaveChangesAsync(ct);
        activity?.SetTag("instance.found", true);
    }

    private sealed record ActiveInstall(CancellationTokenSource Cancellation, ContentInstallState State);

    private sealed class SteamQueueEntry(string key, ContentType type, int maxParallelModDownloads)
    {
        public string Key { get; } = key;
        public ContentType Type { get; } = type;
        public int MaxParallelModDownloads { get; } = maxParallelModDownloads;
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Cancellation { get; set; }
        public bool Started { get; set; }
        public bool Cancelled { get; set; }
    }

    private sealed class SteamQueueLease(ContentOrchestrator owner, SteamQueueEntry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.CompleteSteamQueueEntry(entry);
        }
    }
}
