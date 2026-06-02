using System.Net;
using System.Net.Http.Json;
using KAST.Core.Events;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Services.Content;
using KAST.Infrastructure.Services;
using KAST.UI.Api;
using KAST.UI.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Text.Encodings.Web;

namespace KAST.Tests;

public class AccountAuthEndpointTests
{
    [Fact]
    public async Task BrowserRequest_WithNoAccounts_RedirectsToSetup()
    {
        await using var app = await AuthApp.CreateAsync();

        var response = await app.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/setup/account", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Setup_CreatesAdminAndAuthenticatesApiAccess()
    {
        await using var app = await AuthApp.CreateAsync();

        var setup = await app.Client.PostAsync("/auth/setup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "admin",
            ["password"] = "secret"
        }));
        app.UseCookieFrom(setup);

        var api = await app.Client.GetAsync("/api/mods/");

        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        var mods = await api.Content.ReadFromJsonAsync<List<SteamMod>>();
        Assert.NotNull(mods);
    }

    [Fact]
    public async Task Login_WithInvalidAndValidCredentials_BehavesCorrectly()
    {
        await using var app = await AuthApp.CreateAsync();
        await app.CreateUserAsync("admin", "secret");

        var invalid = await app.Client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "admin",
            ["password"] = "wrong",
            ["returnUrl"] = "/api/mods/"
        }));

        var valid = await app.Client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "admin",
            ["password"] = "secret",
            ["returnUrl"] = "/"
        }));

        Assert.Equal(HttpStatusCode.Redirect, invalid.StatusCode);
        Assert.StartsWith("/login?error=", invalid.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        Assert.Equal("/", valid.Headers.Location?.OriginalString);
        Assert.True(valid.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task Api_RequiresAuthentication()
    {
        await using var app = await AuthApp.CreateAsync();
        await app.CreateUserAsync("admin", "secret");

        var response = await app.Client.GetAsync("/api/mods/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ClearsAuthenticatedAccess()
    {
        await using var app = await AuthApp.CreateAsync();
        await app.CreateUserAsync("admin", "secret");

        var login = await app.Client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "admin",
            ["password"] = "secret"
        }));
        app.UseCookieFrom(login);

        var beforeLogout = await app.Client.GetAsync("/api/mods/");
        var logout = await app.Client.PostAsync("/auth/logout", content: null);
        app.Client.DefaultRequestHeaders.Remove("Cookie");
        var afterLogout = await app.Client.GetAsync("/api/mods/");

        Assert.Equal(HttpStatusCode.OK, beforeLogout.StatusCode);
        Assert.True(logout.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task StaleCookie_WhenUserNoLongerExists_IsRejected()
    {
        await using var app = await AuthApp.CreateAsync();
        await app.CreateUserAsync("admin", "secret");

        var login = await app.Client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "admin",
            ["password"] = "secret"
        }));
        app.UseCookieFrom(login);

        await app.DeleteAllUsersAsync();

        var browserRequest = await app.Client.GetAsync("/");
        var apiRequest = await app.Client.GetAsync("/api/mods/");

        Assert.Equal(HttpStatusCode.Redirect, browserRequest.StatusCode);
        Assert.Equal("/setup/account", browserRequest.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, apiRequest.StatusCode);
    }

    [Fact]
    public async Task OidcOnly_LocalLoginPostRedirectsToOidc()
    {
        await using var app = await AuthApp.CreateAsync(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Oidc",
            ["Auth:Oidc:Authority"] = "https://auth.example.test",
            ["Auth:Oidc:ClientId"] = "kast",
            ["Auth:Oidc:ClientSecret"] = "secret"
        });

        var response = await app.Client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "admin",
            ["password"] = "secret",
            ["returnUrl"] = "/settings"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/auth/oidc/login?returnUrl=", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task OidcLogin_WhenConfigured_ChallengesOidcScheme()
    {
        await using var app = await AuthApp.CreateAsync(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "LocalAndOidc",
            ["Auth:Oidc:Authority"] = "https://auth.example.test",
            ["Auth:Oidc:ClientId"] = "kast",
            ["Auth:Oidc:ClientSecret"] = "secret"
        });

        var response = await app.Client.GetAsync("/auth/oidc/login?returnUrl=%2Fsettings");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/fake-oidc/challenge?returnUrl=%2Fsettings", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Logout_WithOidcCookie_UsesFederatedSignOut()
    {
        await using var app = await AuthApp.CreateAsync(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "LocalAndOidc",
            ["Auth:Oidc:Authority"] = "https://auth.example.test",
            ["Auth:Oidc:ClientId"] = "kast",
            ["Auth:Oidc:ClientSecret"] = "secret"
        });

        var signIn = await app.Client.GetAsync("/test/sign-in-oidc");
        app.UseCookieFrom(signIn);

        var logout = await app.Client.PostAsync("/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/fake-oidc/logout?redirectUri=%2Flogin", logout.Headers.Location?.OriginalString);
    }

    private sealed class AuthApp : IAsyncDisposable
    {
        private AuthApp(WebApplication app, HttpClient client)
        {
            App = app;
            Client = client;
        }

        public WebApplication App { get; }
        public HttpClient Client { get; }

        public static async Task<AuthApp> CreateAsync(Dictionary<string, string?>? configuration = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing"
            });

            builder.WebHost.UseTestServer();
            var config = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Data Source=kast-auth-tests.db",
                ["Auth:Mode"] = "Local",
                ["Auth:Oidc:AllowedGroups:0"] = "KAST Admins"
            };
            if (configuration is not null)
            {
                foreach (var pair in configuration)
                    config[pair.Key] = pair.Value;
            }
            builder.Configuration.AddInMemoryCollection(config);

            var dbName = Guid.NewGuid().ToString();
            builder.Services.AddDbContext<KastDbContext>(options =>
                options.UseInMemoryDatabase(dbName));
            builder.Services.AddScoped<IUserAccountService, UserAccountService>();
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.Events.OnValidatePrincipal = async context =>
                    {
                        var userId = context.Principal?.GetUserId();
                        if (userId is null)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                            return;
                        }

                        var accounts = context.HttpContext.RequestServices.GetRequiredService<IUserAccountService>();
                        var user = await accounts.GetByIdAsync(userId.Value, context.HttpContext.RequestAborted);
                        if (user is null)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        }
                    };
                    options.Events.OnRedirectToLogin = context =>
                    {
                        if (AccountGateMiddlewareExtensions.IsApiOrHubRequest(context.Request))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }

                        context.Response.Redirect(context.RedirectUri);
                        return Task.CompletedTask;
                    };
                })
                .AddScheme<AuthenticationSchemeOptions, TestOidcAuthenticationHandler>(
                    OpenIdConnectDefaults.AuthenticationScheme,
                    _ => { });
            builder.Services.AddAuthorization();

            builder.Services.AddSingleton(BuildModService());
            builder.Services.AddSingleton(Substitute.For<IServerInstanceService>());
            builder.Services.AddSingleton(Substitute.For<IMonitoringService>());
            builder.Services.AddSingleton(Substitute.For<IApiKeyService>());
            builder.Services.AddSingleton(Substitute.For<IContentOrchestrator>());
            builder.Services.AddSingleton<ContentProgressTracker>();
            builder.Services.AddSingleton(Substitute.For<IModPresetService>());
            builder.Services.AddSingleton(Substitute.For<IMissionService>());
            builder.Services.AddSingleton(BuildSettingsService());
            builder.Services.AddSingleton(Substitute.For<IFileSystemService>());
            builder.Services.AddSingleton(BuildBroadcaster());
            builder.Services.AddSingleton(sp => new ModDownloadManager(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IContentOrchestrator>(),
                sp.GetRequiredService<ContentProgressTracker>(),
                NullLogger<ModDownloadManager>.Instance));

            var app = builder.Build();
            app.UseAuthentication();
            app.UseWhen(
                context => !context.Request.Path.StartsWithSegments("/test"),
                branch => branch.UseKastAccountGate());
            app.UseAuthorization();
            app.MapGet("/", () => Results.Ok("ok")).RequireAuthorization();
            app.MapGet("/test/sign-in-oidc", async (HttpContext http, IUserAccountService accounts) =>
            {
                var user = await accounts.ProvisionOidcAdminAsync(new OidcProvisioningRequest(
                    "https://auth.example.test",
                    "user-123",
                    "External Admin",
                    null,
                    ["KAST Admins"]));
                await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AccountClaims.CreatePrincipal(user));
                return Results.Ok();
            });
            app.MapAccountEndpoints();
            app.MapGroup("/api").MapKastApi().RequireAuthorization();

            await app.StartAsync();
            return new AuthApp(app, app.GetTestClient());
        }

        public async Task CreateUserAsync(string username, string password)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
            await accounts.CreateInitialAdminAsync(username, password);
        }

        public async Task DeleteAllUsersAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
            db.Users.RemoveRange(db.Users);
            await db.SaveChangesAsync();
        }

        public void UseCookieFrom(HttpResponseMessage response)
        {
            var setCookie = response.Headers.GetValues("Set-Cookie").First();
            var cookie = setCookie.Split(';', 2)[0];
            Client.DefaultRequestHeaders.Remove("Cookie");
            Client.DefaultRequestHeaders.Add("Cookie", cookie);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }

        private static IModService BuildModService()
        {
            var modService = Substitute.For<IModService>();
            modService.GetAllModsAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<SteamMod>());
            return modService;
        }

        private static ISettingsService BuildSettingsService()
        {
            var settings = Substitute.For<ISettingsService>();
            settings.GetSettingsAsync(Arg.Any<CancellationToken>())
                .Returns(new KastSettings { ModsDirectory = "/tmp/kast-mods" });
            return settings;
        }

        private static IAppEventBroadcaster BuildBroadcaster()
        {
            var events = Substitute.For<IAppEventBroadcaster>();
            events.BroadcastDownloadProgressAsync(Arg.Any<ModDownloadProgressEvent>()).Returns(Task.CompletedTask);
            events.BroadcastModStatusChangedAsync(Arg.Any<ModStatusChangedEvent>()).Returns(Task.CompletedTask);
            events.BroadcastServerStatusChangedAsync(Arg.Any<ServerStatusChangedEvent>()).Returns(Task.CompletedTask);
            events.BroadcastHostMetricsAsync(Arg.Any<HostMetricsUpdatedEvent>()).Returns(Task.CompletedTask);
            events.BroadcastInstanceMetricsAsync(Arg.Any<InstanceMetricsUpdatedEvent>()).Returns(Task.CompletedTask);
            events.BroadcastLogEntryAsync(Arg.Any<LogEntryEvent>()).Returns(Task.CompletedTask);
            return events;
        }
    }

    private sealed class TestOidcAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder), IAuthenticationSignOutHandler
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var returnUrl = Uri.EscapeDataString(properties.RedirectUri ?? "/");
            Response.Redirect($"/fake-oidc/challenge?returnUrl={returnUrl}");
            return Task.CompletedTask;
        }

        public Task SignOutAsync(AuthenticationProperties? properties)
        {
            var redirectUri = Uri.EscapeDataString(properties?.RedirectUri ?? "/login");
            Response.Redirect($"/fake-oidc/logout?redirectUri={redirectUri}");
            return Task.CompletedTask;
        }
    }
}
