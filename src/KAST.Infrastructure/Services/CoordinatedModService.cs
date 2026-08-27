using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

/// <summary>
/// Provides bounded, independently cancellable workers for bulk mod updates.
/// </summary>
public sealed class CoordinatedModService(
    IModService modService,
    IServiceScopeFactory scopeFactory,
    ISettingsService settingsService,
    IModDownloadCancellationRegistry cancellationRegistry,
    ILogger<CoordinatedModService> logger) : IModUpdateCoordinator
{
    public async Task UpdateAllOutdatedModsAsync(CancellationToken ct = default)
    {
        var mods = await modService.GetAllModsAsync(ct);
        var pendingMods = mods.Where(mod =>
            mod.Source == ModSource.SteamWorkshop &&
            mod.Status is ModStatus.NotInstalled or ModStatus.UpdateAvailable or ModStatus.Error)
            .ToList();

        var settings = await settingsService.GetSettingsAsync(ct);
        var maxConcurrentMods = Math.Clamp(
            settings.BulkModDownloadConcurrency,
            DownloadConcurrency.MinimumBulkModDownloads,
            DownloadConcurrency.MaximumBulkModDownloads);
        using var semaphore = new SemaphoreSlim(maxConcurrentMods);

        var workers = pendingMods.Select(UpdateOneAsync).ToList();
        try
        {
            await Task.WhenAll(workers);
        }
        finally
        {
            foreach (var mod in pendingMods)
                cancellationRegistry.ClearPendingCancellation(mod.Id);
        }

        async Task UpdateOneAsync(SteamMod mod)
        {
            using var modCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var registration = cancellationRegistry.Register(mod.Id, modCts);
            var enteredSemaphore = false;
            try
            {
                await semaphore.WaitAsync(modCts.Token);
                enteredSemaphore = true;

                using var scope = scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<IModService>();
                if (mod.Status == ModStatus.NotInstalled)
                    await worker.DownloadModAsync(mod.Id, ct: modCts.Token);
                else
                    await worker.UpdateModFilesAsync(mod.Id, ct: modCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The bulk parent token cancels every linked worker, including
                // workers waiting for a semaphore slot.
                throw;
            }
            catch (OperationCanceledException ex)
            {
                logger.LogInformation(ex, "Bulk update cancelled for mod {Id} ({Name})", mod.Id, mod.Name);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "Bulk update failed for mod {Id} ({Name})", mod.Id, mod.Name);
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Bulk update failed for mod {Id} ({Name})", mod.Id, mod.Name);
            }
            finally
            {
                if (enteredSemaphore)
                    semaphore.Release();
            }
        }
    }
}
