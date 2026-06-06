using System.Diagnostics;
using System.Runtime.InteropServices;
using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public class ServerInstanceService(
    KastDbContext db,
    IProcessManagerService processManager,
    IAppEventBroadcaster broadcaster,
    ILogger<ServerInstanceService> logger,
    IOutputSanitizer sanitizer,
    IServerConsoleLogTailer? consoleLogTailer = null,
    IHostEnvironment? hostEnvironment = null) : IServerInstanceService
{
    public async Task<IReadOnlyList<ServerInstance>> GetAllInstancesAsync(CancellationToken ct = default)
        => await db.ServerInstances
            .Include(s => s.Mods).ThenInclude(m => m.SteamMod)
            .Include(s => s.HeadlessClients)
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public async Task<ServerInstance?> GetInstanceByIdAsync(int id, CancellationToken ct = default)
        => await db.ServerInstances
            .Include(s => s.Mods).ThenInclude(m => m.SteamMod)
            .Include(s => s.HeadlessClients)
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<ServerInstance> CreateInstanceAsync(ServerInstance instance, CancellationToken ct = default)
    {
        using var activity = KastActivitySources.Instances.StartActivity(
            "kast.instance.create", ActivityKind.Internal);
        activity?.SetTag("instance.name", instance.Name);
        activity?.SetTag("instance.port", instance.Port);

        instance.CreatedAt = DateTime.UtcNow;

        // Clear SteamMod navigations — EF only needs the FK (SteamModId) to write the join rows.
        // The navigation objects may already be tracked by this context from a prior query,
        // which causes an identity-map conflict when db.Add() walks the object graph.
        foreach (var sim in instance.Mods)
            sim.SteamMod = null!;

        db.ServerInstances.Add(instance);
        await db.SaveChangesAsync(ct);

        activity?.SetTag("instance.id", instance.Id);
        return instance;
    }

    public async Task<ServerInstance> UpdateInstanceAsync(ServerInstance instance, CancellationToken ct = default)
    {
        instance.LastModified = DateTime.UtcNow;

        // Use ExecuteUpdateAsync to update only scalar properties via a direct SQL UPDATE.
        // This avoids EF walking the navigation graph (Mods, HeadlessClients) which would
        // conflict with ServerInstanceMod entities already tracked by earlier mod operations
        // (AddModToInstanceAsync / UpdateModFlagsAsync / RemoveModFromInstanceAsync).
        await db.ServerInstances
            .Where(s => s.Id == instance.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Name, instance.Name)
                .SetProperty(x => x.InstallPath, instance.InstallPath)
                .SetProperty(x => x.Port, instance.Port)
                .SetProperty(x => x.SteamQueryPort, instance.SteamQueryPort)
                .SetProperty(x => x.RestartPolicy, instance.RestartPolicy)
                .SetProperty(x => x.MaxRestartAttempts, instance.MaxRestartAttempts)
                .SetProperty(x => x.AutoStartTime, instance.AutoStartTime)
                .SetProperty(x => x.AutoStopTime, instance.AutoStopTime)
                .SetProperty(x => x.ScheduleEnabled, instance.ScheduleEnabled)
                .SetProperty(x => x.ServerCfgContent, instance.ServerCfgContent)
                .SetProperty(x => x.BasicCfgContent, instance.BasicCfgContent)
                .SetProperty(x => x.ArmaProfileContent, instance.ArmaProfileContent)
                .SetProperty(x => x.AdditionalParameters, instance.AdditionalParameters)
                .SetProperty(x => x.ContactDlc, instance.ContactDlc)
                .SetProperty(x => x.GmDlc, instance.GmDlc)
                .SetProperty(x => x.PfDlc, instance.PfDlc)
                .SetProperty(x => x.CslaDlc, instance.CslaDlc)
                .SetProperty(x => x.WsDlc, instance.WsDlc)
                .SetProperty(x => x.SpeDlc, instance.SpeDlc)
                .SetProperty(x => x.RfDlc, instance.RfDlc)
                .SetProperty(x => x.EfDlc, instance.EfDlc)
                .SetProperty(x => x.HeadlessClientCount, instance.HeadlessClientCount)
                .SetProperty(x => x.LastModified, instance.LastModified),
            ct);

        return instance;
    }

    public async Task DeleteInstanceAsync(int id, bool deleteFiles = false, CancellationToken ct = default)
    {
        var instance = await db.ServerInstances
            .Include(s => s.Mods).ThenInclude(m => m.SteamMod)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (instance == null)
            return;

        using var activity = KastActivitySources.Instances.StartActivity(
            "kast.instance.delete", ActivityKind.Internal);
        activity?.SetTag("instance.id", id);
        activity?.SetTag("instance.name", instance.Name);

        if (instance.Status == ServerInstanceStatus.Running)
            await StopInstanceAsync(id, ct);
        else if (consoleLogTailer is not null)
            await consoleLogTailer.StopFollowingAsync(id);

        // Capture install path & check whether it's shared with other instances
        // BEFORE we drop this row, so explicit file deletion cannot wipe shared data.
        var installPath = instance.InstallPath;
        var isInstallPathShared = !string.IsNullOrEmpty(installPath)
            && await db.ServerInstances
                .AnyAsync(s => s.Id != id && s.InstallPath == installPath, ct);

        activity?.SetTag("instance.path_shared", isInstallPathShared);
        db.ServerInstances.Remove(instance);
        await db.SaveChangesAsync(ct);

        // Best-effort filesystem cleanup — never let a stray I/O failure
        // resurrect the row we just deleted from the DB.
        try
        {
            DeleteInstanceFiles(instance, wipeInstallPath: deleteFiles && !isInstallPathShared);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, sanitizer.Sanitize(ex.Message));
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                ["exception.type"]    = ex.GetType().Name,
                ["exception.message"] = sanitizer.Sanitize(ex.Message)
            }));
            logger.LogError(ex,
                "Failed to clean up files for deleted instance {Id} ({Name})",
                id, instance.Name);
        }
    }

    /// <summary>
    /// Removes on-disk artefacts owned by the instance: per-instance config dir,
    /// mod symlinks created for this instance, and optionally the whole install
    /// directory when no other instance shares it.
    /// </summary>
    private void DeleteInstanceFiles(ServerInstance instance, bool wipeInstallPath)
    {
        if (string.IsNullOrWhiteSpace(instance.InstallPath))
            return;

        // 1) Per-instance config/profile directory: {InstallPath}/KAST/{id}
        var configDir = GetInstanceConfigDirectory(instance);
        TryDeleteDirectory(configDir, recursive: true);

        // 1b) On Linux, clean up the profile symlink we created in the system profiles dir.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            CleanupLinuxProfileSymlink(instance);

        // 2) Mod symlinks created for this instance's mods (don't touch the mod
        //    source itself — that's shared and owned by ModService).
        var modsDir = GetInstanceModsDirectory(instance);
        if (Directory.Exists(modsDir))
        {
            foreach (var modLink in instance.Mods)
            {
                var mod = modLink.SteamMod;
                if (mod == null || string.IsNullOrEmpty(mod.Name))
                    continue;

                var linkPath = Path.Combine(modsDir, $"@{SanitizeModName(mod.Name)}");
                if (!Path.Exists(linkPath))
                    continue;

                try
                {
                    // Only delete if it's actually a symlink we created.
                    if (Directory.ResolveLinkTarget(linkPath, false) != null)
                        Directory.Delete(linkPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to remove mod symlink for instance {Id}", instance.Id);
                }
            }
        }

        // 3) Full install path — only when no other instance is using it.
        if (wipeInstallPath)
        {
            TryDeleteDirectory(GetInstanceInstallDirectory(instance), recursive: true);
            logger.LogInformation(
                "Wiped install directory for instance {Id}",
                instance.Id);
        }
        else
        {
            logger.LogInformation(
                "Kept install directory shared with other instances for instance {Id}",
                instance.Id);
        }
    }

    private void TryDeleteDirectory(string path, bool recursive)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete instance directory");
        }
    }

    public async Task StartInstanceAsync(int id, CancellationToken ct = default)
    {
        var instance = await db.ServerInstances
            .Include(s => s.Mods).ThenInclude(m => m.SteamMod)
            .Include(s => s.HeadlessClients)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new InvalidOperationException($"Server instance {id} not found");

        if (instance.Status == ServerInstanceStatus.Running)
            return;

        using var activity = KastActivitySources.Instances.StartActivity(
            "kast.instance.start", ActivityKind.Internal);
        activity?.SetTag("instance.id", id);
        activity?.SetTag("instance.name", instance.Name);
        activity?.SetTag("instance.port", instance.Port);

        await LinkModsAsync(id, ct);
        activity?.AddEvent(new ActivityEvent("instance.mods_linked"));

        // Write config files to disk before launch
        WriteConfigFiles(instance);
        activity?.AddEvent(new ActivityEvent("instance.config_written"));

        instance.Status = ServerInstanceStatus.Starting;
        await db.SaveChangesAsync(ct);
        await broadcaster.BroadcastServerStatusChangedAsync(new ServerStatusChangedEvent(instance.Id, instance.Status.ToString()));

        try
        {
            var sessionStartedUtc = DateTime.UtcNow;
            var executable = GetServerExecutable(instance);
            var args = BuildLaunchArguments(instance);

            activity?.SetTag("instance.executable", sanitizer.ToDisplayPath(executable));

            logger.LogInformation("Starting server {Name}", instance.Name);

            // Callbacks for stdout/stderr lines and process exit
            void OnOutputLine(int pid, string line)
            {
                _ = broadcaster.BroadcastLogEntryAsync(new LogEntryEvent(id, line, DateTime.UtcNow));
            }

            void OnProcessExited(int pid, int exitCode)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var newStatus = exitCode == 0 ? ServerInstanceStatus.Stopped : ServerInstanceStatus.Crashed;
                        await broadcaster.BroadcastServerStatusChangedAsync(
                            new ServerStatusChangedEvent(id, newStatus.ToString()));
                        await broadcaster.BroadcastLogEntryAsync(
                            new LogEntryEvent(id, $"Process exited with code {exitCode}", DateTime.UtcNow));
                        if (consoleLogTailer is not null)
                            await consoleLogTailer.StopFollowingAsync(id);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error handling process exit for instance {Id}", id);
                    }
                });
            }

            var pid = await processManager.StartServerProcessAsync(executable, args, OnOutputLine, OnProcessExited, ct);
            consoleLogTailer?.StartFollowing(instance, sessionStartedUtc, replayExistingContent: true);
            instance.ProcessId = pid;
            instance.Status = ServerInstanceStatus.Running;
            instance.StartedAt = DateTime.UtcNow;

            activity?.SetTag("instance.pid", pid);
            activity?.AddEvent(new ActivityEvent("instance.process_started", tags: new ActivityTagsCollection
            {
                ["pid"] = pid
            }));

            // Start headless clients
            foreach (var hc in instance.HeadlessClients)
            {
                var hcArgs = BuildHeadlessClientArguments(instance);
                var hcPid = await processManager.StartServerProcessAsync(executable, hcArgs, OnOutputLine, null, ct);
                hc.ProcessId = hcPid;
                hc.Status = ServerInstanceStatus.Running;
                hc.StartedAt = DateTime.UtcNow;
                activity?.AddEvent(new ActivityEvent("instance.headless_client_started", tags: new ActivityTagsCollection
                {
                    ["hc.pid"] = hcPid
                }));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start server instance {Id}", id);
            activity?.SetStatus(ActivityStatusCode.Error, sanitizer.Sanitize(ex.Message));
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                ["exception.type"]    = ex.GetType().Name,
                ["exception.message"] = sanitizer.Sanitize(ex.Message)
            }));
            // If the process never got a PID the server never actually ran — reset to Stopped
            // so the user can try again. Crashed is reserved for processes that ran and then died.
            instance.Status = instance.ProcessId.HasValue
                ? ServerInstanceStatus.Crashed
                : ServerInstanceStatus.Stopped;
            instance.StartedAt = null;
            throw;
        }
        finally
        {
            await db.SaveChangesAsync(ct);
            await broadcaster.BroadcastServerStatusChangedAsync(new ServerStatusChangedEvent(instance.Id, instance.Status.ToString()));
        }
    }

    public async Task StopInstanceAsync(int id, CancellationToken ct = default)
    {
        var instance = await db.ServerInstances
            .Include(s => s.HeadlessClients)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new InvalidOperationException($"Server instance {id} not found");

        using var activity = KastActivitySources.Instances.StartActivity(
            "kast.instance.stop", ActivityKind.Internal);
        activity?.SetTag("instance.id", id);
        activity?.SetTag("instance.name", instance.Name);
        if (instance.ProcessId.HasValue)
            activity?.SetTag("instance.pid", instance.ProcessId.Value);

        instance.Status = ServerInstanceStatus.Stopping;
        await db.SaveChangesAsync(ct);
        await broadcaster.BroadcastServerStatusChangedAsync(new ServerStatusChangedEvent(instance.Id, instance.Status.ToString()));

        var stopFailures = new List<Exception>();

        // Stop headless clients first
        foreach (var hc in instance.HeadlessClients.Where(h => h.ProcessId.HasValue))
        {
            var pid = hc.ProcessId!.Value;
            try
            {
                await processManager.StopProcessAsync(pid, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to stop headless client process {Pid} for instance {Id}", pid, id);
                if (processManager.IsProcessRunning(pid))
                {
                    hc.Status = ServerInstanceStatus.Running;
                    stopFailures.Add(ex);
                    continue;
                }
            }

            hc.ProcessId = null;
            hc.Status = ServerInstanceStatus.Stopped;
            activity?.AddEvent(new ActivityEvent("instance.headless_client_stopped"));
        }

        var serverStillRunning = false;
        if (instance.ProcessId.HasValue)
        {
            var pid = instance.ProcessId.Value;
            try
            {
                await processManager.StopProcessAsync(pid, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to stop server process {Pid} for instance {Id}", pid, id);
                serverStillRunning = processManager.IsProcessRunning(pid);
                if (serverStillRunning)
                    stopFailures.Add(ex);
            }
        }

        if (!serverStillRunning)
        {
            instance.ProcessId = null;
            instance.Status = ServerInstanceStatus.Stopped;
            instance.StartedAt = null;
            if (consoleLogTailer is not null)
                await consoleLogTailer.StopFollowingAsync(id);
        }
        else
        {
            instance.Status = ServerInstanceStatus.Running;
        }

        await db.SaveChangesAsync(ct);
        await broadcaster.BroadcastServerStatusChangedAsync(new ServerStatusChangedEvent(instance.Id, instance.Status.ToString()));

        if (stopFailures.Count > 0)
            throw new InvalidOperationException(
                $"Failed to stop server instance {id}. One or more processes are still running.",
                stopFailures[0]);
    }

    public async Task RestartInstanceAsync(int id, CancellationToken ct = default)
    {
        var instance = await db.ServerInstances.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Server instance {id} not found");

        using var activity = KastActivitySources.Instances.StartActivity(
            "kast.instance.restart", ActivityKind.Internal);
        activity?.SetTag("instance.id", id);
        activity?.SetTag("instance.name", instance.Name);

        instance.Status = ServerInstanceStatus.Restarting;
        await db.SaveChangesAsync(ct);
        await broadcaster.BroadcastServerStatusChangedAsync(new ServerStatusChangedEvent(instance.Id, instance.Status.ToString()));

        try
        {
            await StopInstanceAsync(id, ct);
            await StartInstanceAsync(id, ct);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, sanitizer.Sanitize(ex.Message));
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                ["exception.type"]    = ex.GetType().Name,
                ["exception.message"] = sanitizer.Sanitize(ex.Message)
            }));
            throw;
        }
    }

    public async Task AddModToInstanceAsync(int instanceId, int modId, int loadOrder = 0, bool isClientSide = false, bool isServerSide = false, CancellationToken ct = default)
    {
        var exists = await db.ServerInstanceMods
            .AnyAsync(m => m.ServerInstanceId == instanceId && m.SteamModId == modId, ct);

        if (!exists)
        {
            db.ServerInstanceMods.Add(new ServerInstanceMod
            {
                ServerInstanceId = instanceId,
                SteamModId = modId,
                IsClientSide = isClientSide,
                IsServerSide = isServerSide,
                LoadOrder = loadOrder
            });
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UpdateModFlagsAsync(int instanceId, int modId, bool isClientSide, bool isServerSide, CancellationToken ct = default)
    {
        var link = await db.ServerInstanceMods
            .FirstOrDefaultAsync(m => m.ServerInstanceId == instanceId && m.SteamModId == modId, ct);
        if (link != null)
        {
            link.IsClientSide = isClientSide;
            link.IsServerSide = isServerSide;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RemoveModFromInstanceAsync(int instanceId, int modId, CancellationToken ct = default)
    {
        var link = await db.ServerInstanceMods
            .OrderBy(m => m.ServerInstanceId)
            .FirstOrDefaultAsync(m => m.ServerInstanceId == instanceId && m.SteamModId == modId, ct);

        if (link != null)
        {
            db.ServerInstanceMods.Remove(link);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UpdateModLoadOrderAsync(int instanceId, int modId, int newOrder, CancellationToken ct = default)
    {
        var link = await db.ServerInstanceMods
            .OrderBy(m => m.ServerInstanceId)
            .FirstOrDefaultAsync(m => m.ServerInstanceId == instanceId && m.SteamModId == modId, ct);

        if (link != null)
        {
            link.LoadOrder = newOrder;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task LinkModsAsync(int instanceId, CancellationToken ct = default)
    {
        var instance = await db.ServerInstances
            .Include(s => s.Mods).ThenInclude(m => m.SteamMod)
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(s => s.Id == instanceId, ct)
            ?? throw new InvalidOperationException($"Server instance {instanceId} not found");

        using var activity = KastActivitySources.Instances.StartActivity(
            "kast.instance.link_mods", ActivityKind.Internal);
        activity?.SetTag("instance.id", instanceId);
        activity?.SetTag("instance.name", instance.Name);
        activity?.SetTag("instance.mods_total", instance.Mods.Count);

        var modsDir = GetInstanceModsDirectory(instance);
        Directory.CreateDirectory(modsDir);

        int linkedCount = 0;
        foreach (var mod in instance.Mods.Select(modLink => modLink.SteamMod))
        {
            if (string.IsNullOrEmpty(mod.LocalPath) || !Directory.Exists(mod.LocalPath))
                continue;

            var linkPath = Path.Combine(modsDir, $"@{SanitizeModName(mod.Name)}");

            if (Path.Exists(linkPath))
            {
                if (Directory.ResolveLinkTarget(linkPath, false) != null)
                    Directory.Delete(linkPath);
                else
                    continue;
            }

            Directory.CreateSymbolicLink(linkPath, mod.LocalPath);
            logger.LogInformation("Linked mod {ModName}", mod.Name);
            linkedCount++;
        }

        activity?.SetTag("instance.mods_linked", linkedCount);
    }

    private string GetServerExecutable(ServerInstance instance)
    {
        var installPath = GetInstanceInstallDirectory(instance);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(installPath, "arma3server_x64.exe");

        return Path.Combine(installPath, "arma3server_x64");
    }

    private string GetInstanceInstallDirectory(ServerInstance instance)
    {
        var installPath = Environment.ExpandEnvironmentVariables(instance.InstallPath);
        return Path.IsPathFullyQualified(installPath)
            ? Path.GetFullPath(installPath)
            : Path.GetFullPath(installPath, hostEnvironment?.ContentRootPath ?? AppContext.BaseDirectory);
    }

    private string GetInstanceConfigDirectory(ServerInstance instance)
        => Path.Combine(GetInstanceInstallDirectory(instance), "KAST", instance.Id.ToString());

    private string GetInstanceModsDirectory(ServerInstance instance)
        => Path.Combine(GetInstanceInstallDirectory(instance), "mods");

    public void WriteConfigFiles(ServerInstance instance)
    {
        if (!string.IsNullOrEmpty(instance.InstallPath))
            Directory.CreateDirectory(GetInstanceInstallDirectory(instance));

        // Per-instance config directory
        var configDir = GetInstanceConfigDirectory(instance);
        Directory.CreateDirectory(configDir);

        try
        {
            if (instance.ServerCfgContent != null)
            {
                var path = Path.Join(configDir, "server.cfg");
                File.WriteAllText(path, instance.ServerCfgContent);
            }

            if (instance.BasicCfgContent != null)
            {
                var path = Path.Join(configDir, "basic.cfg");
                File.WriteAllText(path, instance.BasicCfgContent);
            }

            if (instance.ArmaProfileContent != null)
            {
                var profileName = $"server_{instance.Id}";
                var path = Path.Join(configDir, $"{profileName}.Arma3Profile");
                File.WriteAllText(path, instance.ArmaProfileContent);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not write config files for instance {Id} — server may be running", instance.Id);
        }

        // On Linux the -profiles= flag is broken; symlink the expected profile directory
        // into our managed config folder so Arma reads/writes the right profile.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            EnsureLinuxProfileSymlink(instance, configDir);
    }

    public string GetCommandLine(ServerInstance instance)
    {
        var executable = GetServerExecutable(instance);
        var args = BuildLaunchArguments(instance);
        return $"{executable} {args}";
    }

    private string BuildLaunchArguments(ServerInstance instance)
    {
        var configDir = GetInstanceConfigDirectory(instance);
        var profileName = $"server_{instance.Id}";

        var args = new List<string>
        {
            $"-port={instance.Port}",
            "-nosplash",
            "-world=empty",
            $"-name={profileName}"
        };

        // -profiles= is broken on Linux; the profile directory is handled via a symlink.
        // On Windows it works normally.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            args.Add($"\"-profiles={configDir}\"");

        if (instance.ServerCfgContent != null)
            args.Add($"\"-config={Path.Combine(configDir, "server.cfg")}\"");

        if (instance.BasicCfgContent != null)
            args.Add($"\"-cfg={Path.Combine(configDir, "basic.cfg")}\"");

        AddServerConfigArgs(instance, configDir, args);
        AddModArgs(instance, args);

        if (!string.IsNullOrEmpty(instance.AdditionalParameters))
            args.Add(instance.AdditionalParameters);

        return string.Join(" ", args);
    }

    private static void AddServerConfigArgs(ServerInstance instance, string configDir, List<string> args)
    {
        if (instance.ServerCfgContent == null)
            return;

        var cfgService = new ServerConfigService();
        var cfg = cfgService.ParseServerConfig(instance.ServerCfgContent);

        if (cfg.NetlogEnabled) args.Add("-netlog");
        if (cfg.AutoInit) args.Add("-autoInit");
        if (cfg.AllowedFilePatching > 0) args.Add("-filePatching");
        if (cfg.EnableHT) args.Add("-enableHT");
        if (cfg.EnableRanking)
            args.Add($"\"-ranking={Path.Combine(configDir, "ranking.log")}\"");
        if (cfg.MaxMemOverride && cfg.MaxMem > 0)
            args.Add($"-maxMem={cfg.MaxMem}");
        if (cfg.CpuCountOverride && cfg.CpuCount > 0)
            args.Add($"-cpuCount={cfg.CpuCount}");
    }

    private void AddModArgs(ServerInstance instance, List<string> args)
    {
        var dlcMods = GetDlcModsList(instance);
        var clientMods = GetClientModsList(instance);
        var allPlayerMods = dlcMods.Concat(clientMods).ToList();

        if (allPlayerMods.Count > 0)
            args.Add($"\"-mod={string.Join(";", allPlayerMods)}\"");

        var serverMods = GetServerModsList(instance);
        if (!string.IsNullOrEmpty(serverMods))
            args.Add($"\"-serverMod={serverMods}\"");
    }

    private static List<string> GetDlcModsList(ServerInstance instance)
    {
        var dlcMods = new List<string>();
        if (instance.ContactDlc) dlcMods.Add("contact");
        if (instance.GmDlc) dlcMods.Add("gm");
        if (instance.PfDlc) dlcMods.Add("vn");
        if (instance.CslaDlc) dlcMods.Add("csla");
        if (instance.WsDlc) dlcMods.Add("ws");
        if (instance.SpeDlc) dlcMods.Add("spe");
        if (instance.RfDlc) dlcMods.Add("rf");
        if (instance.EfDlc) dlcMods.Add("ef");
        return dlcMods;
    }

    private List<string> GetClientModsList(ServerInstance instance)
    {
        return instance.Mods
            .Where(m => m.IsClientSide)
            .OrderBy(m => m.LoadOrder)
            .Select(m => Path.Combine(GetInstanceModsDirectory(instance), $"@{SanitizeModName(m.SteamMod.Name)}"))
            .ToList();
    }

    private string GetServerModsList(ServerInstance instance)
    {
        var serverMods = instance.Mods
            .Where(m => m.IsServerSide)
            .OrderBy(m => m.LoadOrder)
            .Select(m => Path.Combine(GetInstanceModsDirectory(instance), $"@{SanitizeModName(m.SteamMod.Name)}"));

        return string.Join(";", serverMods);
    }

    private static string BuildHeadlessClientArguments(ServerInstance instance)
    {
        return $"-client -connect=127.0.0.1 -port={instance.Port} -nosound -world=empty";
    }

    private static string SanitizeModName(string name)
    {
        var sanitized = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        return string.IsNullOrEmpty(sanitized) ? "mod" : sanitized;
    }

    /// <summary>
    /// On Linux, Arma 3 ignores -profiles= and always writes profiles to
    /// ~/.local/share/Arma 3 - Other Profiles/&lt;name&gt;/. We create a symlink
    /// from that expected path to our managed KAST config directory so the
    /// profile file ends up where KAST expects it.
    /// </summary>
    private void EnsureLinuxProfileSymlink(ServerInstance instance, string configDir)
    {
        var profileName = $"server_{instance.Id}";
        var armaProfilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Arma 3 - Other Profiles");
        var symlinkPath = Path.Combine(armaProfilesDir, profileName);

        try
        {
            Directory.CreateDirectory(armaProfilesDir);

            if (Directory.Exists(symlinkPath) || File.Exists(symlinkPath))
            {
                if (Directory.ResolveLinkTarget(symlinkPath, false) != null)
                    File.Delete(symlinkPath); // remove stale symlink (unlink, not rmdir)
                else
                {
                    logger.LogWarning(
                        "Profile path for instance {Id} exists and is not a symlink; skipping symlink creation",
                        instance.Id);
                    return;
                }
            }

            var absoluteConfigDir = Path.GetFullPath(configDir);
            Directory.CreateSymbolicLink(symlinkPath, absoluteConfigDir);
            logger.LogInformation(
                "Created Linux profile symlink for instance {Id}", instance.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to create Linux profile symlink for instance {Id}", instance.Id);
        }
    }

    private void CleanupLinuxProfileSymlink(ServerInstance instance)
    {
        var profileName = $"server_{instance.Id}";
        var armaProfilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Arma 3 - Other Profiles");
        var symlinkPath = Path.Combine(armaProfilesDir, profileName);

        try
        {
            // File.Delete uses unlink() which is the correct syscall for removing a symlink on Linux.
            // Directory.Delete uses rmdir() which always fails on symlinks.
            if (File.Exists(symlinkPath) || Directory.Exists(symlinkPath))
            {
                File.Delete(symlinkPath);
                logger.LogInformation("Removed Linux profile symlink");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to remove Linux profile symlink for instance {Id}", instance.Id);
        }
    }
}
