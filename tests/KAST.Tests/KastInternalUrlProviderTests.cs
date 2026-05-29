using KAST.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;

namespace KAST.Tests;

public class KastInternalUrlProviderTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5000", "http://127.0.0.1:5000/hubs/monitoring")]
    [InlineData("http://127.0.0.1:5000/", "http://127.0.0.1:5000/hubs/monitoring")]
    public void GetMonitoringHubUri_UsesConfiguredInternalBaseUrl(string baseUrl, string expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KAST:InternalBaseUrl"] = baseUrl
            })
            .Build();
        var navigationManager = new TestNavigationManager("https://panel.example/", "https://panel.example/servers");
        var provider = new KastInternalUrlProvider(configuration, navigationManager);

        var uri = provider.GetMonitoringHubUri();

        Assert.Equal(expected, uri.ToString());
    }

    [Fact]
    public void GetMonitoringHubUri_FallsBackToNavigationManagerWhenInternalBaseUrlIsMissing()
    {
        var configuration = new ConfigurationBuilder().Build();
        var navigationManager = new TestNavigationManager("https://panel.example/", "https://panel.example/servers");
        var provider = new KastInternalUrlProvider(configuration, navigationManager);

        var uri = provider.GetMonitoringHubUri();

        Assert.Equal("https://panel.example/hubs/monitoring", uri.ToString());
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri, string uri)
            => Initialize(baseUri, uri);

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
        }
    }
}
