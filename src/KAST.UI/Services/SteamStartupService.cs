using KAST.Core.Interfaces;
using KAST.Infrastructure.Steam;

namespace KAST.UI.Services;

public sealed class SteamStartupService(
    IServiceProvider services,
    ILogger<SteamStartupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var steam = services.GetRequiredService<ISteamService>();
        try
        {
            var cache = SteamClientService.PeekTokenCache();
            if (cache is { } c)
            {
                logger.LogInformation("Restoring Steam session for {Username}", c.Username);
                var ok = await steam.LoginWithTokenAsync(c.Username, c.RefreshToken, stoppingToken);
                if (ok)
                {
                    logger.LogInformation("Steam session restored: {Username}", c.Username);
                    return;
                }

                logger.LogWarning("Cached Steam token invalid; falling back to anonymous");
            }

            var anon = await steam.LoginAnonymousAsync(stoppingToken);
            logger.LogInformation("Steam connected: {Result}", anon ? "anonymous" : "failed");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Steam startup connection failed");
        }
    }
}
