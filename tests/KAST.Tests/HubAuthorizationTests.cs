using KAST.UI.Hubs;
using KAST.UI.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace KAST.Tests;

public class HubAuthorizationTests
{
    [Fact]
    public async Task MonitoringHub_RejectsAnonymousClient()
    {
        await using var app = await HubAuthApp.CreateAsync();
        await using var connection = app.CreateConnection(includeInternalToken: false);

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    [Fact]
    public async Task MonitoringHub_AcceptsInternalTokenClient()
    {
        await using var app = await HubAuthApp.CreateAsync();
        await using var connection = app.CreateConnection(includeInternalToken: true);

        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToHost");

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    private sealed class HubAuthApp : IAsyncDisposable
    {
        private HubAuthApp(WebApplication app)
        {
            App = app;
        }

        private WebApplication App { get; }

        public static async Task<HubAuthApp> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing"
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddSignalR();
            builder.Services.AddSingleton<InternalHubTokenService>();
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie()
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, InternalHubAuthenticationHandler>(
                    InternalHubAuthenticationDefaults.AuthenticationScheme,
                    _ => { });
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("AdminOrInternalHub", policy =>
                {
                    policy.AddAuthenticationSchemes(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        InternalHubAuthenticationDefaults.AuthenticationScheme);
                    policy.RequireAuthenticatedUser();
                });
            });

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapHub<MonitoringHub>("/hubs/monitoring").RequireAuthorization("AdminOrInternalHub");

            await app.StartAsync();
            return new HubAuthApp(app);
        }

        public HubConnection CreateConnection(bool includeInternalToken)
        {
            var token = App.Services.GetRequiredService<InternalHubTokenService>().Token;
            return new HubConnectionBuilder()
                .WithUrl("http://localhost/hubs/monitoring", options =>
                {
                    options.HttpMessageHandlerFactory = _ => App.GetTestServer().CreateHandler();
                    if (includeInternalToken)
                        options.Headers.Add(InternalHubAuthenticationDefaults.HeaderName, token);
                })
                .Build();
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
}
