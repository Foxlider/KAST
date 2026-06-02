using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services.Content;

namespace KAST.UI.Services;

public sealed class ModDownloadManager(
    IServiceScopeFactory scopeFactory,
    IContentOrchestrator orchestrator,
    ContentProgressTracker tracker,
    ILogger<ModDownloadManager> logger)
{
    public int ActiveCount => tracker.GetAll()
        .Count(s => s.Type == ContentType.SteamMod && s.IsDownloading);

    public bool IsActive(int modId) => orchestrator.IsRunning(ContentProgressTracker.ModKey(modId));

    public async Task<int> StartAllOutdatedAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var modService = scope.ServiceProvider.GetRequiredService<IModService>();
        var mods = await modService.GetAllModsAsync(ct);

        var queued = 0;
        foreach (var mod in mods.Where(m =>
                     m.Source == ModSource.SteamWorkshop &&
                     m.Status is ModStatus.NotInstalled or ModStatus.UpdateAvailable or ModStatus.Error))
        {
            ct.ThrowIfCancellationRequested();
            var isUpdate = mod.Status is not ModStatus.NotInstalled;
            if (await StartDownloadAsync(mod.Id, isUpdate, ct))
                queued++;
        }

        return queued;
    }

    public async Task<bool> StartDownloadAsync(int modId, bool isUpdate, CancellationToken ct = default)
    {
        var key = ContentProgressTracker.ModKey(modId);
        if (orchestrator.IsRunning(key))
            return false;

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var modService = sp.GetRequiredService<IModService>();
        var settingsService = sp.GetRequiredService<ISettingsService>();

        var mod = await modService.GetModByIdAsync(modId, ct);
        if (mod is null || mod.Source != ModSource.SteamWorkshop)
            return false;

        var settings = await settingsService.GetSettingsAsync(ct);
        var destinationPath = ResolveDestinationPath(mod, settings.ModsDirectory, isUpdate);
        var maxParallelDownloads = Math.Clamp(settings.ParallelDownloads, 1, 64);
        var maxParallelModDownloads = Math.Clamp(settings.ParallelModDownloads, 1, 16);

        orchestrator.StartModInstall(
            mod.Id,
            ContentType.SteamMod,
            destinationPath,
            mod.WorkshopId,
            expectedSizeBytes: mod.ExpectedSizeBytes,
            onStarted: (callbackSp, _) => MarkModStartedAsync(callbackSp, modId, isUpdate),
            onComplete: (callbackSp, state) => CompleteModInstallAsync(callbackSp, modId, destinationPath, state),
            onError: (callbackSp, _, ex) => FailModInstallAsync(callbackSp, modId, ex),
            maxParallelDownloads: maxParallelDownloads,
            maxParallelModDownloads: maxParallelModDownloads);

        return true;
    }

    public bool StartDownload(int modId, bool isUpdate)
    {
        if (IsActive(modId))
            return false;

        _ = Task.Run(async () =>
        {
            try
            {
                await StartDownloadAsync(modId, isUpdate);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "Failed to queue mod download {ModId}.", modId);
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Failed to queue mod download {ModId}.", modId);
            }
        });

        return true;
    }

    public bool Cancel(int modId)
    {
        var key = ContentProgressTracker.ModKey(modId);
        if (!orchestrator.IsRunning(key))
            return false;

        orchestrator.Cancel(key);
        return true;
    }

    public void CancelAll()
    {
        foreach (var state in tracker.GetAll()
                     .Where(s => s.Type == ContentType.SteamMod && s.IsDownloading))
            orchestrator.Cancel(state.Key);
    }

    private static string ResolveDestinationPath(SteamMod mod, string modsDirectory, bool isUpdate)
    {
        if (isUpdate && !string.IsNullOrWhiteSpace(mod.LocalPath))
            return mod.LocalPath;

        return Path.Join(modsDirectory, mod.WorkshopId.ToString());
    }

    private static async Task MarkModStartedAsync(IServiceProvider sp, int modId, bool isUpdate)
    {
        var modService = sp.GetRequiredService<IModService>();
        var broadcaster = sp.GetRequiredService<IAppEventBroadcaster>();

        var mod = await modService.GetModByIdAsync(modId, CancellationToken.None);
        if (mod is null)
            return;

        mod.Status = isUpdate ? ModStatus.Updating : ModStatus.Downloading;
        await modService.UpdateModAsync(mod, CancellationToken.None);
        await broadcaster.BroadcastModStatusChangedAsync(
            new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));
    }

    private static async Task CompleteModInstallAsync(IServiceProvider sp, int modId, string destinationPath, ContentInstallState state)
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
        await broadcaster.BroadcastModStatusChangedAsync(
            new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));
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
        await broadcaster.BroadcastModStatusChangedAsync(
            new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));
    }

    private static ModStatus RecoverableStatus(SteamMod mod)
    {
        return mod.InstalledManifestId > 0 || !string.IsNullOrWhiteSpace(mod.LocalPath)
            ? ModStatus.UpdateAvailable
            : ModStatus.NotInstalled;
    }
}
