using KAST.Core.Enums;
using KAST.Core.Models;

namespace KAST.Core.Interfaces;

/// <summary>
/// Singleton service that queues content installs, manages cancellation,
/// and delegates to the appropriate <see cref="IContentInstaller"/>.
/// The UI layer only calls this interface — all download logic lives in Infrastructure.
/// </summary>
public interface IContentOrchestrator
{
    /// <summary>Returns true if a download for the given key is currently active.</summary>
    bool IsRunning(string key);

    /// <summary>
    /// Starts a server file install for the given instance, or returns the existing
    /// in-progress state if one is already running.
    /// </summary>
    ContentInstallState StartServerInstall(
        int instanceId,
        string installPath,
        ServerInstance instance,
        int maxParallelDownloads);

    /// <summary>
    /// Starts a mod install (Steam Workshop, local folder, or ZIP).
    /// <paramref name="onComplete"/> and <paramref name="onError"/> are invoked with a
    /// scoped <see cref="IServiceProvider"/> after the operation finishes.
    /// </summary>
    ContentInstallState StartModInstall(
        int modId,
        ContentType type,
        string destinationPath,
        long workshopId = 0,
        string? sourcePath = null,
        long expectedSizeBytes = 0,
        Func<IServiceProvider, ContentInstallState, Task>? onStarted = null,
        Func<IServiceProvider, ContentInstallState, Task>? onComplete = null,
        Func<IServiceProvider, ContentInstallState, Exception, Task>? onError = null,
        int maxParallelDownloads = 4,
        int maxParallelModDownloads = 1);

    /// <summary>
    /// Runs a mod install and completes only after the installer finishes.
    /// Intended for hosted queue workers that own their own scheduling.
    /// </summary>
    Task<ContentInstallState> RunModInstallAsync(
        int modId,
        ContentType type,
        string destinationPath,
        long workshopId = 0,
        string? sourcePath = null,
        long expectedSizeBytes = 0,
        Func<IServiceProvider, ContentInstallState, Task>? onStarted = null,
        Func<IServiceProvider, ContentInstallState, Task>? onComplete = null,
        Func<IServiceProvider, ContentInstallState, Exception, Task>? onError = null,
        int maxParallelDownloads = 4,
        int maxParallelModDownloads = 1,
        CancellationToken ct = default);

    /// <summary>Cancels the active download for the given key.</summary>
    void Cancel(string key);

    /// <summary>Runs post-install validation and returns check results.</summary>
    IReadOnlyList<ContentValidationResult> Validate(ContentType type, ContentInstallRequest request);

    /// <summary>Returns the current install state for the given key, or null if not found.</summary>
    ContentInstallState? GetState(string key);
}
