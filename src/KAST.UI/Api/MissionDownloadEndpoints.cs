using KAST.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace KAST.UI.Api;

public static class MissionDownloadEndpoints
{
    public static IEndpointRouteBuilder MapMissionDownloadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/mission-download/{instanceId:int}/{**filename}", GetMissionDownloadAsync);
        return endpoints;
    }

    private static async Task<IResult> GetMissionDownloadAsync(
        int instanceId,
        string filename,
        HttpContext http,
        IMissionHttpDownloadService downloadService,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filename) || !filename.EndsWith(".pbo", StringComparison.OrdinalIgnoreCase))
            return Results.NotFound();

        var result = await downloadService.GetDownloadAsync(instanceId, filename, ct);
        if (result == null)
            return Results.NotFound();

        var fileInfo = new FileInfo(result.PhysicalPath);
        if (!fileInfo.Exists)
            return Results.NotFound();

        var lastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);

        // Check If-Modified-Since
        if (http.Request.Headers.IfModifiedSince is { Count: > 0 } imsValues
            && DateTimeOffset.TryParse(imsValues.ToString(), out var ifModifiedSince)
            && lastModified <= ifModifiedSince)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        // Check If-None-Match (ETag)
        if (http.Request.Headers.IfNoneMatch is { Count: > 0 } inmValues
            && inmValues.ToString() == result.ETag)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        // Log the download
        var playerName = http.Request.Headers["Player-Name"].FirstOrDefault();
        var playerSteamId = http.Request.Headers["Player-Steamid"].FirstOrDefault();
        var serverAddress = http.Request.Headers["Server-Address"].FirstOrDefault();
        var userAgent = http.Request.Headers.UserAgent.FirstOrDefault();

        var logger = loggerFactory.CreateLogger("KAST.UI.Api.MissionDownloadEndpoints");
        logger.LogInformation(
            "HTTP mission download: Instance={InstanceId}, Mission={Mission}, Player={PlayerName}, SteamId={SteamId}, Server={ServerAddress}, UA={UserAgent}",
            instanceId, filename, playerName, playerSteamId, serverAddress, userAgent);

        http.Response.Headers["X-Hash"] = result.Hash.ToString();
        http.Response.Headers["X-BSize"] = result.SizeBytes.ToString();

        return Results.File(
            result.PhysicalPath,
            "application/octet-stream",
            lastModified: lastModified,
            entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(result.ETag),
            enableRangeProcessing: true);
    }
}
