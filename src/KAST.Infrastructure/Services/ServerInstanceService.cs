using System.Runtime.InteropServices;
using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public class ServerInstanceService(
    KastDbContext db,
    IProcessManagerService processManager,
    IAppEventBroadcaster broadcaster,
    ILogger<ServerInstanceService> logger) : IServerInstanceService
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
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<ServerInstance> CreateInstanceAsync(ServerInstance instance, CancellationToken ct = default)
    {
        instance.CreatedAt = DateTime.UtcNow;

        // Clear SteamMod navigations — EF only needs the FK (SteamModId) to write the join rows.
        // The navigation objects may already be tracked by this context from a prior query,
        // which causes an identity-map conflict when db.Add() walks the object graph.
        foreach (var sim in instance.Mods)
            sim.SteamMod = null!;

        db.ServerInstances.Add(instance);
        await db.SaveChangesAsync(ct);
        return instance;
    }

    public async Task<ServerInstance> UpdateInstanceAsync(ServerInstance instance, CancellationToken ct = default)
    {
        instance.LastModified = DateTime.UtcNow;
        db.ServerInstances.Update(instance);
        await db.SaveChangesAsync(ct);
        return instance;
    }

    public async Task DeleteInstanceAsync(int id, CancellationToken ct = default)
    {
        var instance = await db.ServerInstances.FindAsync([id], ct);
        if (instance != null)
        {
            if (instance.Status == ServerInstanceStatus.Running)
                await StopInstanceAsync(id, ct);

            db.ServerInstances.Remove(instance);
            await db.SaveChangesAsync(ct);
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

        await LinkModsAsync(id, ct);

        // Write config files to disk before launch
        WriteConfigFiles(instance);

        instance.Status = ServerInstanceStatus.Starting;
        await db.SaveChangesAsync(ct);
        await broadcaster.BroadcastServerStatusChangedAsync(new ServerStatusChangedEvent(instance.Id, instance.Status.ToString()));

        try
        {
            var executable = GetServerExecutable(instance);
            var args = BuildLaunchArguments(instance);

            logger.LogInformation("Starting server {Name} with args: {Args}", instance.Name, args);

            var pid = await processManager.StartServerProcessAsync(executable, args, ct);
            instance.ProcessId = pid;
            instance.Status = ServerInstanceStatus.Running;
            instance.StartedAt = DateTime.UtcNow;

            // Start headless clients
            foreach (var hc in instance.HeadlessClients)
            {
                var hcArgs = BuildHeadlessClientArguments(instance);
                var hcPid = await processManager.StartServerProcessAsync(executable, hcArgs, ct);
                hc.ProcessId = hcPid;
                hc.Status = ServerInstanceStatus.Running;
                hc.StartedAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start server instance {Id}", id);
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

        instance.Status = ServerInstanceStatus.Stopping;
        await db.SaveChangesAsync(ct);
        await broadcaster.BroadcastServerStatusChangedAsync(new ServerStatusChangedEvent(instance.Id, instance.Status.ToString()));

        // Stop headless clients first
        foreach (var hc in instance.HeadlessClients.Where(h => h.ProcessId.HasValue))
        {
            await processManager.StopProcessAsync(hc.ProcessId!.Value, ct);
            hc.ProcessId = null;
            hc.Status = ServerInstanceStatus.Stopped;
        }

        if (instance.ProcessId.HasValue)
        {
            await processManager.StopProcessAsync(instance.ProcessId.Value, ct);
        }

        instance.ProcessId = null;
        instance.Status = ServerInstanceStatus.Stopped;
        instance.StartedAt = null;
        await db.SaveChangesAsync(ct);
        await broadcaster.BroadcastServerStatusChangedAsync(new ServerStatusChangedEvent(instance.Id, instance.Status.ToString()));
    }

    public async Task RestartInstanceAsync(int id, CancellationToken ct = default)
    {
        var instance = await db.ServerInstances.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Server instance {id} not found");

        instance.Status = ServerInstanceStatus.Restarting;
        await db.SaveChangesAsync(ct);
        await broadcaster.BroadcastServerStatusChangedAsync(new ServerStatusChangedEvent(instance.Id, instance.Status.ToString()));

        await StopInstanceAsync(id, ct);
        await StartInstanceAsync(id, ct);
    }

    public async Task AddModToInstanceAsync(int instanceId, int modId, int loadOrder = 0, CancellationToken ct = default)
    {
        var exists = await db.ServerInstanceMods
            .AnyAsync(m => m.ServerInstanceId == instanceId && m.SteamModId == modId, ct);

        if (!exists)
        {
            db.ServerInstanceMods.Add(new ServerInstanceMod
            {
                ServerInstanceId = instanceId,
                SteamModId = modId,
                LoadOrder = loadOrder
            });
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
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(s => s.Id == instanceId, ct)
            ?? throw new InvalidOperationException($"Server instance {instanceId} not found");

        var modsDir = Path.Combine(instance.InstallPath, "mods");
        Directory.CreateDirectory(modsDir);

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
            logger.LogInformation("Linked mod {ModName} -> {LinkPath}", mod.Name, linkPath);
        }
    }

    private static string GetServerExecutable(ServerInstance instance)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(instance.InstallPath, "arma3server_x64.exe");

        return Path.Combine(instance.InstallPath, "arma3server_x64");
    }

    private static void WriteConfigFiles(ServerInstance instance)
    {
        if (!string.IsNullOrEmpty(instance.InstallPath))
            Directory.CreateDirectory(instance.InstallPath);

        // Per-instance config directory
        var configDir = Path.Combine(instance.InstallPath, "Servers", instance.Id.ToString());
        Directory.CreateDirectory(configDir);

        if (instance.ServerCfgContent != null)
        {
            var path = Path.Combine(configDir, "server.cfg");
            File.WriteAllText(path, instance.ServerCfgContent);
        }

        if (instance.BasicCfgContent != null)
        {
            var path = Path.Combine(configDir, "basic.cfg");
            File.WriteAllText(path, instance.BasicCfgContent);
        }

        if (instance.ArmaProfileContent != null)
        {
            // Profile file goes in the profiles directory with instance-specific name
            var profileDir = Path.Combine(instance.InstallPath, "Servers", instance.Id.ToString());
            Directory.CreateDirectory(profileDir);
            var profileName = $"server_{instance.Id}";
            var path = Path.Combine(profileDir, $"{profileName}.Arma3Profile");
            File.WriteAllText(path, instance.ArmaProfileContent);
        }
    }

    private static string BuildLaunchArguments(ServerInstance instance)
    {
        var configDir = Path.Combine(instance.InstallPath, "Servers", instance.Id.ToString());
        var profileName = $"server_{instance.Id}";

        var args = new List<string>
        {
            $"-port={instance.Port}",
            "-nosplash",
            "-world=empty",
            $"\"-profiles={configDir}\"",
            $"-name={profileName}"
        };

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

    private static void AddModArgs(ServerInstance instance, List<string> args)
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

    private static List<string> GetClientModsList(ServerInstance instance)
    {
        return instance.Mods
            .Where(m => !m.IsServerSide)
            .OrderBy(m => m.LoadOrder)
            .Select(m => Path.Combine(instance.InstallPath, "mods", $"@{SanitizeModName(m.SteamMod.Name)}"))
            .ToList();
    }

    private static string GetServerModsList(ServerInstance instance)
    {
        var serverMods = instance.Mods
            .Where(m => m.IsServerSide)
            .OrderBy(m => m.LoadOrder)
            .Select(m => Path.Combine(instance.InstallPath, "mods", $"@{SanitizeModName(m.SteamMod.Name)}"));

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
}
