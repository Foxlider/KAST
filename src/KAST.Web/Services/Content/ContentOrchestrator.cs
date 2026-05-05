using System.Collections.Concurrent;
using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KAST.Web.Services.Content;

/// <summary>
/// Singleton orchestrator that queues content installs, manages cancellation,
/// and delegates to the appropriate <see cref="IContentInstaller"/>.
/// Replaces the old ServerInstallService + the download portions of ModService.
/// </summary>
public class ContentOrchestrator(
    IServiceScopeFactory scopeFactory,
    ContentProgressTracker tracker,
    IEnumerable<IContentInstaller> installers,
    ILogger<ContentOrchestrator> logger)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    private readonly Dictionary<ContentType, IContentInstaller> _installers =
        installers.ToDictionary(i => i.Type);

    public bool IsRunning(string key) => _active.ContainsKey(key);

    // ── Server install ───────────────────────────────────────────────────────

    /// <summary>Starts a background server install. Returns the state for UI binding.</summary>
    public ContentInstallState StartServerInstall(int instanceId, string installPath, ServerInstance instance, int maxParallelDownloads)
    {
        var key = ContentProgressTracker.ServerKey(instanceId);
        if (_active.ContainsKey(key))
            return tracker.Get(key)!;

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
        var state = tracker.Create(key, ContentType.Server, instance.Name, steps);
        state.IsDownloading = true;
        state.AddLog($"Queued download for instance {instanceId}.");

        var cts = new CancellationTokenSource();
        _active[key] = cts;

        _ = Task.Run(() => RunAsync(key, request, state, cts.Token));
        return state;
    }

    // ── Mod install ──────────────────────────────────────────────────────────

    /// <summary>Starts a background mod install (Steam or local). Returns the state for UI binding.</summary>
    /// <param name="onComplete">Called with a scoped IServiceProvider after a successful install.</param>
    /// <param name="onError">Called with a scoped IServiceProvider and the exception on failure.</param>
    public ContentInstallState StartModInstall(int modId, ContentType type, string destinationPath,
        long workshopId = 0, string? sourcePath = null,
        Func<IServiceProvider, ContentInstallState, Task>? onComplete = null,
        Func<IServiceProvider, ContentInstallState, Exception, Task>? onError = null)
    {
        var key = ContentProgressTracker.ModKey(modId);
        if (_active.ContainsKey(key))
            return tracker.Get(key)!;

        var installer = _installers[type];
        var request = new ContentInstallRequest
        {
            Type = type,
            DestinationPath = destinationPath,
            WorkshopId = workshopId,
            SourcePath = sourcePath,
        };

        var steps = installer.PlanSteps(request);
        var state = tracker.Create(key, type, $"Mod {modId}", steps);
        state.IsDownloading = true;
        state.AddLog($"Queued install for mod {modId}.");

        var cts = new CancellationTokenSource();
        _active[key] = cts;

        _ = Task.Run(() => RunAsync(key, request, state, cts.Token, onComplete, onError));
        return state;
    }

    // ── Cancel ───────────────────────────────────────────────────────────────

    public void Cancel(string key)
    {
        if (_active.TryRemove(key, out var cts))
            cts.Cancel();
    }

    // ── Validation ───────────────────────────────────────────────────────────

    public IReadOnlyList<ContentValidationResult> Validate(ContentType type, ContentInstallRequest request)
    {
        if (!_installers.TryGetValue(type, out var installer))
            return [];
        return installer.Validate(request);
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private async Task RunAsync(string key, ContentInstallRequest request, ContentInstallState state,
        CancellationToken ct,
        Func<IServiceProvider, ContentInstallState, Task>? onComplete = null,
        Func<IServiceProvider, ContentInstallState, Exception, Task>? onError = null)
    {
        try
        {
            if (request.Type == ContentType.Server)
                await UpdateServerStatusAsync(request.ServerInstanceId, ServerInstanceStatus.Downloading, CancellationToken.None);

            var installer = _installers[request.Type];
            await installer.InstallAsync(request, state, ct);

            // Persist to DB BEFORE signalling completion so the UI sees updated data
            if (request.Type == ContentType.Server)
            {
                var buildId = DateTime.UtcNow.ToString("yyyyMMddHHmm");
                state.AddLog($"Build stamp: {buildId}");
                await UpdateServerInstallAsync(request.ServerInstanceId, DateTime.UtcNow, buildId, CancellationToken.None);
            }

            // Fire caller-provided completion callback with a fresh scope
            if (onComplete is not null)
            {
                using var scope = scopeFactory.CreateScope();
                await onComplete(scope.ServiceProvider, state);
            }

            state.AddLog("All steps complete.");
            state.IsDownloading = false;
            state.IsComplete = true;
            state.NotifyChanged();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Content install cancelled: {Key}", key);
            state.AddLog("Cancelled.");
            state.IsDownloading = false;
            state.ErrorMessage = "Cancelled";

            if (state.CurrentStep is { Status: ContentStepStatus.InProgress })
                state.FailStep(state.CurrentStepIndex, "Cancelled");

            if (onError is not null)
            {
                using var scope = scopeFactory.CreateScope();
                await onError(scope.ServiceProvider, state, new OperationCanceledException());
            }

            state.NotifyChanged();

            if (request.Type == ContentType.Server)
                await UpdateServerStatusAsync(request.ServerInstanceId, ServerInstanceStatus.Stopped, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Content install failed: {Key}", key);
            state.AddLog($"ERROR: {ex.Message}");
            state.IsDownloading = false;
            state.ErrorMessage = ex.Message;

            if (state.CurrentStep is { Status: ContentStepStatus.InProgress })
                state.FailStep(state.CurrentStepIndex, ex.Message);

            if (onError is not null)
            {
                using var scope = scopeFactory.CreateScope();
                await onError(scope.ServiceProvider, state, ex);
            }

            state.NotifyChanged();

            if (request.Type == ContentType.Server)
                await UpdateServerStatusAsync(request.ServerInstanceId, ServerInstanceStatus.Stopped, CancellationToken.None);
        }
        finally
        {
            _active.TryRemove(key, out _);
        }
    }

    private async Task UpdateServerStatusAsync(int instanceId, ServerInstanceStatus status, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var instance = await db.ServerInstances.FindAsync([instanceId], ct);
        if (instance is null) return;
        instance.Status = status;
        await db.SaveChangesAsync(ct);
    }

    private async Task UpdateServerInstallAsync(int instanceId, DateTime installedAt, string buildId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
        var instance = await db.ServerInstances.FindAsync([instanceId], ct);
        if (instance is null) return;
        instance.InstalledAt = installedAt;
        instance.InstalledBuildId = buildId;
        instance.Status = ServerInstanceStatus.Stopped;
        await db.SaveChangesAsync(ct);
    }
}
