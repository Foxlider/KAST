using System.Runtime.InteropServices;
using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public class ServerInstanceService(
    KastDbContext db,
    IProcessManagerService processManager,
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
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<ServerInstance> CreateInstanceAsync(ServerInstance instance, CancellationToken ct = default)
    {
        instance.CreatedAt = DateTime.UtcNow;
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
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new InvalidOperationException($"Server instance {id} not found");

        if (instance.Status == ServerInstanceStatus.Running)
            return;

        await LinkModsAsync(id, ct);

        instance.Status = ServerInstanceStatus.Starting;
        await db.SaveChangesAsync(ct);

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
            instance.Status = ServerInstanceStatus.Crashed;
            throw;
        }
        finally
        {
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task StopInstanceAsync(int id, CancellationToken ct = default)
    {
        var instance = await db.ServerInstances
            .Include(s => s.HeadlessClients)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new InvalidOperationException($"Server instance {id} not found");

        instance.Status = ServerInstanceStatus.Stopping;
        await db.SaveChangesAsync(ct);

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
    }

    public async Task RestartInstanceAsync(int id, CancellationToken ct = default)
    {
        var instance = await db.ServerInstances.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Server instance {id} not found");

        instance.Status = ServerInstanceStatus.Restarting;
        await db.SaveChangesAsync(ct);

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
            .FirstOrDefaultAsync(s => s.Id == instanceId, ct)
            ?? throw new InvalidOperationException($"Server instance {instanceId} not found");

        var modsDir = Path.Combine(instance.InstallPath, "mods");
        Directory.CreateDirectory(modsDir);

        foreach (var modLink in instance.Mods)
        {
            var mod = modLink.SteamMod;
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

    private static string BuildLaunchArguments(ServerInstance instance)
    {
        var args = new List<string>
        {
            $"-port={instance.Port}",
            "-nosplash",
            "-world=empty"
        };

        if (instance.ServerCfgContent != null)
            args.Add($"-config={Path.Combine(instance.InstallPath, "server.cfg")}");

        if (instance.BasicCfgContent != null)
            args.Add($"-cfg={Path.Combine(instance.InstallPath, "basic.cfg")}");

        // Build mod list
        var serverMods = instance.Mods
            .Where(m => m.IsServerSide)
            .OrderBy(m => m.LoadOrder)
            .Select(m => Path.Combine(instance.InstallPath, "mods", $"@{SanitizeModName(m.SteamMod.Name)}"));

        var modList = string.Join(";", serverMods);
        if (!string.IsNullOrEmpty(modList))
            args.Add($"\"-mod={modList}\"");

        if (!string.IsNullOrEmpty(instance.AdditionalParameters))
            args.Add(instance.AdditionalParameters);

        return string.Join(" ", args);
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
