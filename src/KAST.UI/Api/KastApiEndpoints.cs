using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace KAST.UI.Api;

/// <summary>
/// Extension method that maps all KAST minimal API groups under the given route prefix.
/// Usage: app.MapGroup("/api").MapKastApi()
/// </summary>
public static class KastApiEndpoints
{
    public static RouteGroupBuilder MapKastApi(this RouteGroupBuilder group)
    {
        group.MapServersApi();
        group.MapModsApi();
        group.MapMonitoringApi();
        group.MapSettingsApi();
        return group;
    }

    // ── Servers ──────────────────────────────────────────────────────────────

    private static void MapServersApi(this RouteGroupBuilder root)
    {
        var g = root.MapGroup("/servers").WithTags("Servers");

        g.MapGet("/", async (IServerInstanceService svc, [FromServices] IOutputSanitizer sanitizer, CancellationToken ct) =>
            Results.Ok((await svc.GetAllInstancesAsync(ct)).Select(instance => ToServerDto(instance, sanitizer))));

        g.MapGet("/{id:int}", async (int id, IServerInstanceService svc, [FromServices] IOutputSanitizer sanitizer, CancellationToken ct) =>
        {
            var inst = await svc.GetInstanceByIdAsync(id, ct);
            return inst is null ? Results.NotFound() : Results.Ok(ToServerDto(inst, sanitizer));
        });

        g.MapPost("/", async (ServerInstance instance, IServerInstanceService svc, [FromServices] IOutputSanitizer sanitizer, CancellationToken ct) =>
        {
            var created = await svc.CreateInstanceAsync(instance, ct);
            return Results.Created($"/api/servers/{created.Id}", ToServerDto(created, sanitizer));
        });

        g.MapPut("/{id:int}", async (int id, ServerInstance instance, IServerInstanceService svc, [FromServices] IOutputSanitizer sanitizer, CancellationToken ct) =>
        {
            instance.Id = id;
            return Results.Ok(ToServerDto(await svc.UpdateInstanceAsync(instance, ct), sanitizer));
        });

        g.MapDelete("/{id:int}", async (int id, IServerInstanceService svc, CancellationToken ct) =>
        {
            await svc.DeleteInstanceAsync(id, ct);
            return Results.NoContent();
        });

        g.MapPost("/{id:int}/start", async (int id, IServerInstanceService svc, CancellationToken ct) =>
        {
            await svc.StartInstanceAsync(id, ct);
            return Results.Ok();
        });

        g.MapPost("/{id:int}/stop", async (int id, IServerInstanceService svc, CancellationToken ct) =>
        {
            await svc.StopInstanceAsync(id, ct);
            return Results.Ok();
        });

        g.MapPost("/{id:int}/restart", async (int id, IServerInstanceService svc, CancellationToken ct) =>
        {
            await svc.RestartInstanceAsync(id, ct);
            return Results.Ok();
        });

        g.MapPost("/{id:int}/mods/{modId:int}", async (int id, int modId, [FromQuery] int loadOrder, IServerInstanceService svc, CancellationToken ct) =>
        {
            await svc.AddModToInstanceAsync(id, modId, loadOrder, ct: ct);
            return Results.Ok();
        });

        g.MapDelete("/{id:int}/mods/{modId:int}", async (int id, int modId, IServerInstanceService svc, CancellationToken ct) =>
        {
            await svc.RemoveModFromInstanceAsync(id, modId, ct);
            return Results.NoContent();
        });
    }

    // ── Mods ─────────────────────────────────────────────────────────────────

    private static void MapModsApi(this RouteGroupBuilder root)
    {
        var g = root.MapGroup("/mods").WithTags("Mods");

        g.MapGet("/", async (IModService svc, [FromServices] IOutputSanitizer sanitizer, CancellationToken ct) =>
            Results.Ok((await svc.GetAllModsAsync(ct)).Select(mod => ToModDto(mod, sanitizer))));

        g.MapGet("/{id:int}", async (int id, IModService svc, [FromServices] IOutputSanitizer sanitizer, CancellationToken ct) =>
        {
            var mod = await svc.GetModByIdAsync(id, ct);
            return mod is null ? Results.NotFound() : Results.Ok(ToModDto(mod, sanitizer));
        });

        g.MapPost("/workshop/{workshopId:long}", async (long workshopId, IModService svc, [FromServices] IOutputSanitizer sanitizer, CancellationToken ct) =>
        {
            var mod = await svc.AddWorkshopModAsync(workshopId, ct);
            return Results.Created($"/api/mods/{mod.Id}", ToModDto(mod, sanitizer));
        });

        g.MapPost("/local", async (ImportLocalModRequest req, IModService svc, [FromServices] IOutputSanitizer sanitizer, CancellationToken ct) =>
        {
            var mod = await svc.ImportLocalModAsync(req.Path, req.Name, ct);
            return Results.Created($"/api/mods/{mod.Id}", ToModDto(mod, sanitizer));
        });

        g.MapDelete("/{id:int}", async (int id, IModService svc, CancellationToken ct) =>
        {
            await svc.DeleteModAsync(id, ct);
            return Results.NoContent();
        });

        g.MapPost("/{id:int}/download", async (int id, IModService modService,
            IContentOrchestrator orchestrator, ISettingsService settingsService,
            IFileSystemService fs, IAppEventBroadcaster broadcaster, CancellationToken ct) =>
        {
            var mod = await modService.GetModByIdAsync(id, ct);
            if (mod is null) return Results.NotFound();

            mod.Status = ModStatus.Downloading;
            await modService.UpdateModAsync(mod, ct);
            await broadcaster.BroadcastModStatusChangedAsync(new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));

            var settings = await settingsService.GetSettingsAsync(ct);
            var destPath = Path.Combine(settings.ModsDirectory, mod.WorkshopId.ToString());

            orchestrator.StartModInstall(mod.Id, ContentType.SteamMod, destPath, mod.WorkshopId,
                onComplete: async (sp, state) =>
                {
                    var db = sp.GetRequiredService<KastDbContext>();
                    var m = await db.Mods.FindAsync(id, ct);
                    if (m is null) return;
                    m.Status = ModStatus.Installed;
                    m.LocalPath = Path.GetFullPath(destPath);
                    m.SizeBytes = fs.GetDirectorySize(destPath);
                    m.LastUpdatedLocal = DateTime.UtcNow;
                    if (state?.InstalledManifestId > 0)
                    {
                        m.InstalledManifestId = state.InstalledManifestId;
                        m.SteamManifestId = state.InstalledManifestId;
                    }
                    await db.SaveChangesAsync(ct);
                    await sp.GetRequiredService<IAppEventBroadcaster>()
                        .BroadcastModStatusChangedAsync(new ModStatusChangedEvent(m.Id, m.Status.ToString()));
                },
                onError: async (sp, _, _) =>
                {
                    var db = sp.GetRequiredService<KastDbContext>();
                    var m = await db.Mods.FindAsync(id, ct);
                    if (m is null) return;
                    m.Status = ModStatus.Error;
                    await db.SaveChangesAsync(ct);
                    await sp.GetRequiredService<IAppEventBroadcaster>()
                        .BroadcastModStatusChangedAsync(new ModStatusChangedEvent(m.Id, m.Status.ToString()));
                });

            return Results.Ok();
        });

        g.MapPost("/{id:int}/update", async (int id, IModService modService,
            IContentOrchestrator orchestrator, IFileSystemService fs,
            IAppEventBroadcaster broadcaster, CancellationToken ct) =>
        {
            var mod = await modService.GetModByIdAsync(id, ct);
            if (mod is null) return Results.NotFound();

            mod.Status = ModStatus.Updating;
            await modService.UpdateModAsync(mod, ct);
            await broadcaster.BroadcastModStatusChangedAsync(new ModStatusChangedEvent(mod.Id, mod.Status.ToString()));

            var destPath = mod.LocalPath;

            orchestrator.StartModInstall(mod.Id, ContentType.SteamMod, destPath, mod.WorkshopId,
                onComplete: async (sp, state) =>
                {
                    var db = sp.GetRequiredService<KastDbContext>();
                    var m = await db.Mods.FindAsync(id, ct);
                    if (m is null) return;
                    m.Status = ModStatus.Installed;
                    m.SizeBytes = fs.GetDirectorySize(destPath);
                    m.LastUpdatedLocal = DateTime.UtcNow;
                    if (state?.InstalledManifestId > 0)
                    {
                        m.InstalledManifestId = state.InstalledManifestId;
                        m.SteamManifestId = state.InstalledManifestId;
                    }
                    await db.SaveChangesAsync(ct);
                    await sp.GetRequiredService<IAppEventBroadcaster>()
                        .BroadcastModStatusChangedAsync(new ModStatusChangedEvent(m.Id, m.Status.ToString()));
                },
                onError: async (sp, _, _) =>
                {
                    var db = sp.GetRequiredService<KastDbContext>();
                    var m = await db.Mods.FindAsync(id, ct);
                    if (m is null) return;
                    m.Status = ModStatus.Error;
                    await db.SaveChangesAsync(ct);
                    await sp.GetRequiredService<IAppEventBroadcaster>()
                        .BroadcastModStatusChangedAsync(new ModStatusChangedEvent(m.Id, m.Status.ToString()));
                });

            return Results.Ok();
        });

        g.MapPost("/check-updates", async (IModService svc, CancellationToken ct) =>
        {
            await svc.CheckForUpdatesAsync(ct);
            return Results.Ok();
        });

        g.MapPost("/update-all", async (IModUpdateCoordinator coordinator, CancellationToken ct) =>
        {
            // Do not detach a scoped service from the request: its DbContext would
            // be disposed while the updates were still running.
            await coordinator.UpdateAllOutdatedModsAsync(ct);
            return Results.Ok();
        });
    }

    // ── Monitoring ────────────────────────────────────────────────────────────

    private static void MapMonitoringApi(this RouteGroupBuilder root)
    {
        var g = root.MapGroup("/monitoring").WithTags("Monitoring");

        g.MapGet("/host", async (IMonitoringService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetHostMetricsAsync(ct)));

        g.MapGet("/instances", async (IMonitoringService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAllInstanceMetricsAsync(ct)));

        g.MapGet("/instances/{id:int}", async (int id, IMonitoringService svc, CancellationToken ct) =>
        {
            var metrics = await svc.GetInstanceMetricsAsync(id, ct);
            return metrics is null ? Results.NotFound() : Results.Ok(metrics);
        });
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    private static void MapSettingsApi(this RouteGroupBuilder root)
    {
        var g = root.MapGroup("/settings").WithTags("Settings");

        g.MapGet("/", async (ISettingsService svc, [FromServices] IOutputSanitizer sanitizer, CancellationToken ct) =>
            Results.Ok(ToSettingsDto(await svc.GetSettingsAsync(ct), sanitizer)));

        g.MapPut("/", async (KastSettings settings, ISettingsService svc, CancellationToken ct) =>
        {
            await svc.UpdateSettingsAsync(settings, ct);
            return Results.Ok();
        });

        g.MapGet("/api-keys", async (IApiKeyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAllKeysAsync(ct)));

        g.MapPost("/api-keys", async (CreateApiKeyRequest req, IApiKeyService svc, CancellationToken ct) =>
        {
            var (key, raw) = await svc.CreateApiKeyAsync(req.Name, ct);
            return Results.Ok(new { key, rawKey = raw });
        });

        g.MapDelete("/api-keys/{id:int}", async (int id, IApiKeyService svc, CancellationToken ct) =>
        {
            await svc.RevokeKeyAsync(id, ct);
            return Results.NoContent();
        });
    }

    private static SafeServerInstanceDto ToServerDto(ServerInstance instance, IOutputSanitizer sanitizer) => new(
        instance.Id,
        instance.Name,
        sanitizer.ToDisplayPath(instance.InstallPath),
        instance.Port,
        instance.SteamQueryPort,
        instance.Status,
        instance.RestartPolicy,
        instance.MaxRestartAttempts,
        instance.AutoStartTime,
        instance.AutoStopTime,
        instance.ScheduleEnabled,
        instance.ServerCfgContent,
        instance.BasicCfgContent,
        instance.ArmaProfileContent,
        instance.AdditionalParameters,
        instance.ContactDlc,
        instance.GmDlc,
        instance.PfDlc,
        instance.CslaDlc,
        instance.WsDlc,
        instance.SpeDlc,
        instance.RfDlc,
        instance.EfDlc,
        instance.InstalledAt,
        instance.InstalledBuildId,
        instance.ProcessId,
        instance.StartedAt,
        instance.HeadlessClientCount,
        instance.CreatedAt,
        instance.LastModified,
        instance.Mods.Select(mod => ToServerModDto(mod, sanitizer)).ToList(),
        instance.HeadlessClients.Select(ToHeadlessClientDto).ToList());

    private static SafeServerInstanceModDto ToServerModDto(ServerInstanceMod mod, IOutputSanitizer sanitizer) => new(
        mod.ServerInstanceId,
        mod.SteamModId,
        mod.IsClientSide,
        mod.IsServerSide,
        mod.LoadOrder,
        mod.AddedAt,
        ToModDto(mod.SteamMod, sanitizer));

    private static SafeHeadlessClientDto ToHeadlessClientDto(HeadlessClient client) => new(
        client.Id,
        client.ServerInstanceId,
        client.ProcessId,
        client.Status,
        client.StartedAt);

    private static SafeSteamModDto ToModDto(SteamMod mod, IOutputSanitizer sanitizer) => new(
        mod.Id,
        mod.WorkshopId,
        sanitizer.ToDisplayPath(mod.LocalPath),
        mod.Name,
        mod.Description,
        mod.ThumbnailUrl,
        mod.Author,
        mod.SizeBytes,
        mod.ExpectedSizeBytes,
        mod.Source,
        mod.Status,
        mod.LastUpdatedSteam,
        mod.LastUpdatedLocal,
        mod.LastChecked,
        mod.Comment,
        mod.CreatedAt,
        mod.InstalledManifestId,
        mod.SteamManifestId,
        mod.IsClientSide,
        mod.IsServerSide);

    private static SafeKastSettingsDto ToSettingsDto(KastSettings settings, IOutputSanitizer sanitizer) => new(
        settings.Id,
        settings.Arma3ServerAppId,
        sanitizer.ToDisplayPath(settings.ModsDirectory),
        sanitizer.ToDisplayPath(settings.ServersDirectory),
        settings.ThemeMode,
        settings.MetricsIntervalSeconds,
        settings.ParallelDownloads,
        settings.BulkModDownloadConcurrency);

    public record SafeServerInstanceDto(
        int Id,
        string Name,
        string InstallPath,
        int Port,
        int SteamQueryPort,
        ServerInstanceStatus Status,
        RestartPolicy RestartPolicy,
        int MaxRestartAttempts,
        string? AutoStartTime,
        string? AutoStopTime,
        bool ScheduleEnabled,
        string? ServerCfgContent,
        string? BasicCfgContent,
        string? ArmaProfileContent,
        string? AdditionalParameters,
        bool ContactDlc,
        bool GmDlc,
        bool PfDlc,
        bool CslaDlc,
        bool WsDlc,
        bool SpeDlc,
        bool RfDlc,
        bool EfDlc,
        DateTime? InstalledAt,
        string? InstalledBuildId,
        int? ProcessId,
        DateTime? StartedAt,
        int HeadlessClientCount,
        DateTime CreatedAt,
        DateTime? LastModified,
        IReadOnlyList<SafeServerInstanceModDto> Mods,
        IReadOnlyList<SafeHeadlessClientDto> HeadlessClients);

    public record SafeServerInstanceModDto(
        int ServerInstanceId,
        int SteamModId,
        bool IsClientSide,
        bool IsServerSide,
        int LoadOrder,
        DateTime AddedAt,
        SafeSteamModDto SteamMod);

    public record SafeHeadlessClientDto(
        int Id,
        int ServerInstanceId,
        int? ProcessId,
        ServerInstanceStatus Status,
        DateTime? StartedAt);

    public record SafeSteamModDto(
        int Id,
        long WorkshopId,
        string LocalPath,
        string Name,
        string? Description,
        string? ThumbnailUrl,
        string? Author,
        long SizeBytes,
        long ExpectedSizeBytes,
        ModSource Source,
        ModStatus Status,
        DateTime? LastUpdatedSteam,
        DateTime? LastUpdatedLocal,
        DateTime? LastChecked,
        string? Comment,
        DateTime CreatedAt,
        ulong InstalledManifestId,
        ulong SteamManifestId,
        bool IsClientSide,
        bool IsServerSide);

    public record SafeKastSettingsDto(
        int Id,
        int Arma3ServerAppId,
        string ModsDirectory,
        string ServersDirectory,
        string ThemeMode,
        int MetricsIntervalSeconds,
        int ParallelDownloads,
        int BulkModDownloadConcurrency);

    public record ImportLocalModRequest(string Path, string Name);
    public record CreateApiKeyRequest(string Name);
}
