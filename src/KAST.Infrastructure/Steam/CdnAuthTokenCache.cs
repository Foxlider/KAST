using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SteamKit2;
using SteamKit2.CDN;

namespace KAST.Infrastructure.Steam;

internal sealed class CdnAuthTokenCache(SteamContent steamContent, ILogger logger)
{
    private sealed record CachedToken(string Token, DateTime ExpiresUtc);

    private readonly ConcurrentDictionary<(uint DepotId, string Host), CachedToken> _tokens = new();

    public async Task<string?> GetAsync(uint appId, uint depotId, Server server, CancellationToken ct)
    {
        var host = server.Host;
        if (string.IsNullOrEmpty(host))
            return null;

        var key = (depotId, host);
        if (_tokens.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTime.UtcNow.AddMinutes(1))
            return cached.Token;

        try
        {
            var result = await steamContent.GetCDNAuthToken(appId, depotId, host).WaitAsync(ct);
            if (result.Result != EResult.OK || string.IsNullOrEmpty(result.Token))
                return null;

            _tokens[key] = new CachedToken(result.Token, result.Expiration);
            return result.Token;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "No CDN auth token for depot {DepotId} on {Host}", depotId, host);
            return null; // many CDN hosts don't need one
        }
    }

    public void Invalidate(uint depotId, string? host)
    {
        if (!string.IsNullOrEmpty(host))
            _tokens.TryRemove((depotId, host), out _);
    }

    public void Clear() => _tokens.Clear();
}