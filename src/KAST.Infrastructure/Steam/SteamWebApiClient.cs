using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KAST.Core.Interfaces;
using KAST.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Steam;

public class SteamWebApiClient(HttpClient httpClient, ILogger<SteamWebApiClient> logger, IOutputSanitizer sanitizer)
{
    private const string BaseUrl = "https://api.steampowered.com";

    public async Task<WorkshopItemInfo?> GetPublishedFileDetailsAsync(long workshopId, CancellationToken ct = default)
    {
        var results = await GetPublishedFileDetailsBatchAsync([workshopId], ct);
        return results.FirstOrDefault();
    }

    public async Task<IReadOnlyList<WorkshopItemInfo>> GetPublishedFileDetailsBatchAsync(
        long[] workshopIds, CancellationToken ct = default)
    {
        if (workshopIds.Length == 0) return [];

        using var activity = KastActivitySources.Steam.StartActivity(
            "kast.steam.api.workshop_info", ActivityKind.Client);
        activity?.SetTag("workshop.ids_count", workshopIds.Length);
        if (workshopIds.Length <= 10)
            activity?.SetTag("workshop.ids", string.Join(",", workshopIds));

        try
        {

        var formData = new Dictionary<string, string>
        {
            ["itemcount"] = workshopIds.Length.ToString()
        };
        for (int i = 0; i < workshopIds.Length; i++)
            formData[$"publishedfileids[{i}]"] = workshopIds[i].ToString();

        using var content = new FormUrlEncodedContent(formData);

        logger.LogDebug("Fetching published file details for {Count} items", workshopIds.Length);

        var response = await httpClient.PostAsync(
            $"{BaseUrl}/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
            content, ct);
        response.EnsureSuccessStatusCode();

        var rawJson = await response.Content.ReadAsStringAsync(ct);
        logger.LogDebug("Steam API raw response: {Json}", rawJson);

        var json = System.Text.Json.JsonSerializer.Deserialize<SteamApiResponse>(rawJson);

        if (json?.Response?.PublishedFileDetails is null)
            return [];

        var results = json.Response.PublishedFileDetails
            .Where(d => d.Result == 1)
            .Select(d =>
            {
                var info = MapToWorkshopItemInfo(d);
                logger.LogDebug("Mapped item {Id}: ConsumerAppId={Consumer}, CreatorAppId={Creator}, ManifestId={Manifest}",
                    d.PublishedFileId, d.ConsumerAppId, d.CreatorAppId, d.HContentFile);
                return info;
            })
            .ToList();

        activity?.SetTag("workshop.results_count", results.Count);
        return results;
        }
        catch (Exception ex)
        {
            var safeMessage = sanitizer.Sanitize(ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, safeMessage);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                ["exception.type"]    = ex.GetType().Name,
                ["exception.message"] = safeMessage
            }));
            throw;
        }
    }

    public async Task<string?> DiscoverCdnServerAsync(CancellationToken ct = default)
    {
        using var activity = KastActivitySources.Steam.StartActivity(
            "kast.steam.api.cdn_discover", ActivityKind.Client);

        try
        {
            var response = await httpClient.GetFromJsonAsync<CdnApiResponse>(
                $"{BaseUrl}/IContentServerDirectoryService/GetServersForSteamPipe/v1/?cell_id=0&max_servers=20",
                ct);

            var server = response?.Response?.Servers?
                .FirstOrDefault(s => s.HttpsSupport is "mandatory" or "optional");

            var host = server?.VHost ?? server?.Host;
            activity?.SetTag("cdn.discovered_host", host ?? "none");
            activity?.SetTag("cdn.servers_returned", response?.Response?.Servers?.Count ?? 0);
            return host;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to discover CDN servers via Web API");
            activity?.SetStatus(ActivityStatusCode.Error, sanitizer.Sanitize(ex.Message));
            return null;
        }
    }

    private static WorkshopItemInfo MapToWorkshopItemInfo(PublishedFileDetail d)
    {
        // consumer_appid is preferred; fall back to creator_appid (e.g. when consumer side is unset)
        var appId = d.ConsumerAppId != 0 ? d.ConsumerAppId : d.CreatorAppId;

        var info = new WorkshopItemInfo
        {
            WorkshopId = long.TryParse(d.PublishedFileId, out var id) ? id : 0,
            Name = d.Title ?? $"Workshop Item {d.PublishedFileId}",
            Description = d.Description,
            ThumbnailUrl = d.PreviewUrl,
            Author = d.Creator,
            SizeBytes = long.TryParse(d.FileSize, out var size) ? size : 0,
            LastUpdated = d.TimeUpdated > 0
                ? DateTimeOffset.FromUnixTimeSeconds(d.TimeUpdated).UtcDateTime
                : DateTime.MinValue,
            Subscriptions = d.Subscriptions,
            ConsumerAppId = appId,
            ManifestId = ulong.TryParse(d.HContentFile, out var mf) ? mf : 0,
            Tags = d.Tags?.Select(t => t.Tag).ToList() ?? []
        };

        Activity.Current?.AddEvent(new ActivityEvent("workshop.item_mapped", tags: new ActivityTagsCollection
        {
            ["workshop.id"]          = info.WorkshopId,
            ["workshop.name"]        = info.Name,
            ["workshop.manifest_id"] = info.ManifestId,
            ["workshop.app_id"]      = appId,
            ["workshop.size_bytes"]  = info.SizeBytes
        }));

        return info;
    }

    // ───── Steam Web API response models ─────

    private record SteamApiResponse(
        [property: JsonPropertyName("response")] PublishedFileDetailsContainer? Response);

    private record PublishedFileDetailsContainer(
        [property: JsonPropertyName("publishedfiledetails")] List<PublishedFileDetail>? PublishedFileDetails);

    private record PublishedFileDetail(
        [property: JsonPropertyName("publishedfileid")] string? PublishedFileId,
        [property: JsonPropertyName("result")] int Result,
        [property: JsonPropertyName("creator")] string? Creator,
        [property: JsonPropertyName("creator_appid")] uint CreatorAppId,
        [property: JsonPropertyName("consumer_appid")] uint ConsumerAppId,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("time_updated")] long TimeUpdated,
        [property: JsonPropertyName("preview_url")] string? PreviewUrl,
        [property: JsonPropertyName("file_size")] string? FileSize,
        [property: JsonPropertyName("file_url")] string? FileUrl,
        [property: JsonPropertyName("hcontent_file")] string? HContentFile,
        [property: JsonPropertyName("subscriptions")] int Subscriptions,
        [property: JsonPropertyName("tags")] List<TagEntry>? Tags);

    private record TagEntry(
        [property: JsonPropertyName("tag")] string Tag);

    // ───── CDN server discovery response models ─────

    private record CdnApiResponse(
        [property: JsonPropertyName("response")] CdnServersContainer? Response);

    private record CdnServersContainer(
        [property: JsonPropertyName("servers")] List<CdnServer>? Servers);

    private record CdnServer(
        [property: JsonPropertyName("host")] string? Host,
        [property: JsonPropertyName("vhost")] string? VHost,
        [property: JsonPropertyName("https_support")] string? HttpsSupport);
}
