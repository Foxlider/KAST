using Microsoft.AspNetCore.Components;

namespace KAST.UI.Services;

public sealed class KastInternalUrlProvider
{
    private const string MonitoringHubPath = "hubs/monitoring";

    private readonly IConfiguration _configuration;
    private readonly NavigationManager _navigationManager;

    public KastInternalUrlProvider(
        IConfiguration configuration,
        NavigationManager navigationManager)
    {
        _configuration = configuration;
        _navigationManager = navigationManager;
    }

    public Uri GetMonitoringHubUri()
    {
        var internalBaseUrl = _configuration["KAST:InternalBaseUrl"];

        if (!string.IsNullOrWhiteSpace(internalBaseUrl))
        {
            return new Uri(
                new Uri(internalBaseUrl.TrimEnd('/') + "/"),
                MonitoringHubPath);
        }

        return _navigationManager.ToAbsoluteUri("/" + MonitoringHubPath);
    }
}
