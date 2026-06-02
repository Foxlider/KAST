using KAST.Core.Enums;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Services;
using KAST.Infrastructure.Services.Content;
using KAST.UI.Services;
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
        group.MapPresetsApi();
        group.MapMissionsApi();
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
            ModDownloadManager downloadManager, CancellationToken ct) =>
        {
            var mod = await modService.GetModByIdAsync(id, ct);
            if (mod is null) return Results.NotFound();

            await downloadManager.StartDownloadAsync(id, isUpdate: false, ct);

            return Results.Ok();
        });

        g.MapPost("/{id:int}/update", async (int id, IModService modService,
            ModDownloadManager downloadManager, CancellationToken ct) =>
        {
            var mod = await modService.GetModByIdAsync(id, ct);
            if (mod is null) return Results.NotFound();

            await downloadManager.StartDownloadAsync(id, isUpdate: true, ct);

            return Results.Ok();
        });

        g.MapPost("/check-updates", async (IModService svc, CancellationToken ct) =>
        {
            await svc.CheckForUpdatesAsync(ct);
            return Results.Ok();
        });

        g.MapPost("/update-all", async (ModDownloadManager downloadManager, CancellationToken ct) =>
        {
            var queued = await downloadManager.StartAllOutdatedAsync(ct);
            return Results.Accepted(value: new { queued });
        });
    }

    // ── Presets ──────────────────────────────────────────────────────────────

    private static void MapPresetsApi(this RouteGroupBuilder root)
    {
        var g = root.MapGroup("/presets").WithTags("Presets");

        g.MapGet("/instance/{instanceId:int}", async (int instanceId, IModPresetService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetPresetsForInstanceAsync(instanceId, ct)));

        g.MapGet("/{id:int}", async (int id, IModPresetService svc, CancellationToken ct) =>
        {
            var preset = await svc.GetPresetByIdAsync(id, ct);
            return preset is null ? Results.NotFound() : Results.Ok(preset);
        });

        g.MapPost("/instance/{instanceId:int}/save", async (int instanceId, SaveKastPresetRequest req, IModPresetService svc, CancellationToken ct) =>
        {
            var preset = await svc.SaveInstanceAsKastPresetAsync(instanceId, req.Name, ct);
            return Results.Created($"/api/presets/{preset.Id}", preset);
        });

        g.MapPost("/instance/{instanceId:int}/import", async (int instanceId, ImportPresetRequest req, IModPresetService svc, CancellationToken ct) =>
        {
            var preset = await svc.ImportFromArmaHtmlAsync(instanceId, req.Name, req.HtmlContent, ct);
            return Results.Created($"/api/presets/{preset.Id}", preset);
        });

        g.MapPut("/{id:int}", async (int id, ModPreset preset, IModPresetService svc, CancellationToken ct) =>
        {
            preset.Id = id;
            return Results.Ok(await svc.UpdatePresetAsync(preset, ct));
        });

        g.MapDelete("/{id:int}", async (int id, IModPresetService svc, CancellationToken ct) =>
        {
            await svc.DeletePresetAsync(id, ct);
            return Results.NoContent();
        });

        g.MapPost("/{id:int}/apply/{instanceId:int}", async (int id, int instanceId, IModPresetService svc, CancellationToken ct) =>
        {
            await svc.ApplyPresetAsync(id, instanceId, ct);
            return Results.Ok();
        });
    }

    // ── Missions ─────────────────────────────────────────────────────────────

    private static void MapMissionsApi(this RouteGroupBuilder root)
    {
        var mg = root.MapGroup("/missions").WithTags("Missions");

        // Missions
        mg.MapGet("/instance/{instanceId:int}", async (int instanceId, string? search, [FromQuery] string? tags, string? map, IMissionService svc, CancellationToken ct) =>
        {
            List<int>? tagIdList = null;
            if (!string.IsNullOrWhiteSpace(tags))
                tagIdList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();

            var missions = await svc.SearchMissionsAsync(instanceId, search, tagIdList, map, ct);
            return Results.Ok(missions);
        });

        mg.MapPost("/instance/{instanceId:int}/upload", async (int instanceId, HttpRequest request, IMissionService svc, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data");

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest("No file provided");

            await using var stream = file.OpenReadStream();
            var mission = await svc.UploadMissionAsync(instanceId, file.FileName, stream, ct);
            return Results.Created($"/api/missions/{mission.Id}", mission);
        }).DisableAntiforgery();

        mg.MapGet("/{id:int}", async (int id, IMissionService svc, CancellationToken ct) =>
        {
            var mission = await svc.GetMissionByIdAsync(id, ct);
            return mission is null ? Results.NotFound() : Results.Ok(mission);
        });

        mg.MapPut("/{id:int}", async (int id, Mission mission, IMissionService svc, CancellationToken ct) =>
        {
            mission.Id = id;
            return Results.Ok(await svc.UpdateMissionAsync(mission, ct));
        });

        mg.MapDelete("/{id:int}", async (int id, IMissionService svc, CancellationToken ct) =>
        {
            await svc.DeleteMissionAsync(id, ct);
            return Results.NoContent();
        });

        mg.MapGet("/{id:int}/download", async (int id, IMissionService svc, CancellationToken ct) =>
        {
            var stream = await svc.GetMissionFileStreamAsync(id, ct);
            if (stream is null) return Results.NotFound();
            return Results.File(stream, "application/octet-stream");
        });

        // Tags
        mg.MapGet("/instance/{instanceId:int}/tags", async (int instanceId, IMissionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetTagsForInstanceAsync(instanceId, ct)));

        mg.MapPost("/instance/{instanceId:int}/tags", async (int instanceId, CreateTagRequest req, IMissionService svc, CancellationToken ct) =>
        {
            var tag = await svc.CreateTagAsync(instanceId, req.Name, ct);
            return Results.Created($"/api/missions/instance/{instanceId}/tags/{tag.Id}", tag);
        });

        mg.MapDelete("/tags/{id:int}", async (int id, IMissionService svc, CancellationToken ct) =>
        {
            await svc.DeleteTagAsync(id, ct);
            return Results.NoContent();
        });

        mg.MapPost("/{id:int}/tags/{tagId:int}", async (int id, int tagId, IMissionService svc, CancellationToken ct) =>
        {
            await svc.AssignTagAsync(id, tagId, ct);
            return Results.Ok();
        });

        mg.MapDelete("/{id:int}/tags/{tagId:int}", async (int id, int tagId, IMissionService svc, CancellationToken ct) =>
        {
            await svc.RemoveTagAsync(id, tagId, ct);
            return Results.NoContent();
        });

        // Campaigns
        var cg = root.MapGroup("/campaigns").WithTags("Campaigns");

        cg.MapGet("/instance/{instanceId:int}", async (int instanceId, IMissionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetCampaignsForInstanceAsync(instanceId, ct)));

        cg.MapGet("/{id:int}", async (int id, IMissionService svc, CancellationToken ct) =>
        {
            var campaign = await svc.GetCampaignByIdAsync(id, ct);
            return campaign is null ? Results.NotFound() : Results.Ok(campaign);
        });

        cg.MapPost("/instance/{instanceId:int}", async (int instanceId, Campaign campaign, IMissionService svc, CancellationToken ct) =>
        {
            campaign.ServerInstanceId = instanceId;
            var created = await svc.CreateCampaignAsync(campaign, ct);
            return Results.Created($"/api/campaigns/{created.Id}", created);
        });

        cg.MapPut("/{id:int}", async (int id, Campaign campaign, IMissionService svc, CancellationToken ct) =>
        {
            campaign.Id = id;
            return Results.Ok(await svc.UpdateCampaignAsync(campaign, ct));
        });

        cg.MapDelete("/{id:int}", async (int id, IMissionService svc, CancellationToken ct) =>
        {
            await svc.DeleteCampaignAsync(id, ct);
            return Results.NoContent();
        });

        cg.MapPost("/{id:int}/missions", async (int id, AddCampaignMissionRequest req, IMissionService svc, CancellationToken ct) =>
        {
            await svc.AddMissionToCampaignAsync(id, req.MissionId, req.OrderIndex, ct);
            return Results.Ok();
        });

        cg.MapDelete("/{id:int}/missions/{missionId:int}", async (int id, int missionId, IMissionService svc, CancellationToken ct) =>
        {
            await svc.RemoveMissionFromCampaignAsync(id, missionId, ct);
            return Results.NoContent();
        });

        cg.MapPut("/{id:int}/missions/reorder", async (int id, ReorderRequest req, IMissionService svc, CancellationToken ct) =>
        {
            await svc.ReorderCampaignMissionsAsync(id, req.OrderedMissionIds, ct);
            return Results.Ok();
        });

        // Sets
        var sg = root.MapGroup("/sets").WithTags("Sets");

        sg.MapGet("/instance/{instanceId:int}", async (int instanceId, IMissionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetSetsForInstanceAsync(instanceId, ct)));

        sg.MapGet("/{id:int}", async (int id, IMissionService svc, CancellationToken ct) =>
        {
            var set = await svc.GetSetByIdAsync(id, ct);
            return set is null ? Results.NotFound() : Results.Ok(set);
        });

        sg.MapPost("/instance/{instanceId:int}", async (int instanceId, Set set, IMissionService svc, CancellationToken ct) =>
        {
            set.ServerInstanceId = instanceId;
            var created = await svc.CreateSetAsync(set, ct);
            return Results.Created($"/api/sets/{created.Id}", created);
        });

        sg.MapPut("/{id:int}", async (int id, Set set, IMissionService svc, CancellationToken ct) =>
        {
            set.Id = id;
            return Results.Ok(await svc.UpdateSetAsync(set, ct));
        });

        sg.MapDelete("/{id:int}", async (int id, IMissionService svc, CancellationToken ct) =>
        {
            await svc.DeleteSetAsync(id, ct);
            return Results.NoContent();
        });

        sg.MapPost("/{id:int}/missions", async (int id, AddSetMissionRequest req, IMissionService svc, CancellationToken ct) =>
        {
            await svc.AddMissionToSetAsync(id, req.MissionId, ct);
            return Results.Ok();
        });

        sg.MapDelete("/{id:int}/missions/{missionId:int}", async (int id, int missionId, IMissionService svc, CancellationToken ct) =>
        {
            await svc.RemoveMissionFromSetAsync(id, missionId, ct);
            return Results.NoContent();
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
        settings.ParallelDownloads);

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
        int ParallelDownloads);

}

public record ImportLocalModRequest(string Path, string Name);
public record CreateApiKeyRequest(string Name);
public record SaveKastPresetRequest(string Name);
public record ImportPresetRequest(string Name, string HtmlContent);
public record CreateTagRequest(string Name);
public record AddCampaignMissionRequest(int MissionId, int OrderIndex);
public record AddSetMissionRequest(int MissionId);
public record ReorderRequest(List<int> OrderedMissionIds);
