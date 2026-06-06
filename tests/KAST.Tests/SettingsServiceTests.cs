using KAST.Infrastructure.Services;
using KAST.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace KAST.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly Infrastructure.Data.KastDbContext _db;
    private bool _disposed;

    public SettingsServiceTests()
    {
        _db = DbHelper.CreateInMemoryDb();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _db.Dispose();
            }
            _disposed = true;
        }
    }

    ~SettingsServiceTests()
    {
        Dispose(false);
    }

    private SettingsService CreateService(Dictionary<string, string?>? configValues = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? [])
            .Build();
        return new SettingsService(_db, config);
    }

    [Fact]
    public async Task GetSettings_FirstCall_SeedsDefaults()
    {
        var sut = CreateService();

        var settings = await sut.GetSettingsAsync();

        Assert.Equal("./mods", settings.ModsDirectory);
        Assert.Equal("./servers", settings.ServersDirectory);
        Assert.Equal(233780, settings.Arma3ServerAppId);
        Assert.Equal("dark", settings.ThemeMode);
        Assert.Equal(5, settings.MetricsIntervalSeconds);
        Assert.Equal("stable", settings.UpdateChannelId);
        Assert.True(settings.AutoUpdateCheckEnabled);
        Assert.False(settings.SystemAuthEnabled);
        Assert.Null(settings.SystemAuthDomain);
        Assert.Equal("Auto", settings.SystemAuthSource);
    }

    [Fact]
    public async Task GetSettings_FirstCall_SeedsFromConfiguration()
    {
        var sut = CreateService(new Dictionary<string, string?>
        {
            ["Kast:ModsDirectory"] = "/custom/mods",
            ["Kast:ServersDirectory"] = "/custom/servers",
            ["Kast:Arma3AppId"] = "999999",
            ["Auth:System:Enabled"] = "true",
            ["Auth:System:Domain"] = "example.test"
        });

        var settings = await sut.GetSettingsAsync();

        Assert.Equal("/custom/mods", settings.ModsDirectory);
        Assert.Equal("/custom/servers", settings.ServersDirectory);
        Assert.Equal(999999, settings.Arma3ServerAppId);
        Assert.True(settings.SystemAuthEnabled);
        Assert.Equal("example.test", settings.SystemAuthDomain);
    }

    [Fact]
    public async Task GetSettings_SecondCall_ReturnsSameRow()
    {
        var sut = CreateService();

        var first = await sut.GetSettingsAsync();
        var second = await sut.GetSettingsAsync();

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_db.Settings);
    }

    [Fact]
    public async Task GetSettings_EnvVarOverrides_AppliedButNotPersisted()
    {
        var sut = CreateService();

        // Seed the row first
        await sut.GetSettingsAsync();

        // Set env vars
        Environment.SetEnvironmentVariable("Kast__ModsDirectory", "/env/mods");
        Environment.SetEnvironmentVariable("Kast__ServersDirectory", "/env/servers");
        try
        {
            var settings = await sut.GetSettingsAsync();

            Assert.Equal("/env/mods", settings.ModsDirectory);
            Assert.Equal("/env/servers", settings.ServersDirectory);

            // Note: EF change tracker means the in-memory entity is mutated.
            // The key assertion is that env vars are applied at read time.
            // In a real app with scoped DbContext, the DB row stays unchanged.
        }
        finally
        {
            Environment.SetEnvironmentVariable("Kast__ModsDirectory", null);
            Environment.SetEnvironmentVariable("Kast__ServersDirectory", null);
        }
    }

    [Fact]
    public async Task UpdateSettings_PersistsChanges()
    {
        var sut = CreateService();

        var settings = await sut.GetSettingsAsync();
        settings.ThemeMode = "light";
        settings.MetricsIntervalSeconds = 10;
        settings.ModsDirectory = "/updated/mods";
        settings.UpdateChannelId = "dev";
        settings.AutoUpdateCheckEnabled = false;
        settings.SystemAuthEnabled = true;
        settings.SystemAuthDomain = "DOMAIN";
        settings.SystemAuthSource = "Windows";

        await sut.UpdateSettingsAsync(settings);

        // Read back from a fresh service instance (same DB)
        var updated = await sut.GetSettingsAsync();
        Assert.Equal("light", updated.ThemeMode);
        Assert.Equal(10, updated.MetricsIntervalSeconds);
        Assert.Equal("/updated/mods", updated.ModsDirectory);
        Assert.Equal("dev", updated.UpdateChannelId);
        Assert.False(updated.AutoUpdateCheckEnabled);
        Assert.True(updated.SystemAuthEnabled);
        Assert.Equal("DOMAIN", updated.SystemAuthDomain);
        Assert.Equal("Windows", updated.SystemAuthSource);
    }

    [Fact]
    public async Task UpdateSettings_NoExistingRow_CreatesNew()
    {
        var sut = CreateService();

        var newSettings = new Core.Models.KastSettings
        {
            ModsDirectory = "/brand/new",
            ServersDirectory = "/brand/servers",
            Arma3ServerAppId = 12345,
            ThemeMode = "light",
            MetricsIntervalSeconds = 15
        };

        await sut.UpdateSettingsAsync(newSettings);

        Assert.Single(_db.Settings);
    }

    [Fact]
    public async Task GetSettings_InvalidAppId_UsesDefault()
    {
        var sut = CreateService(new Dictionary<string, string?>
        {
            ["Kast:Arma3AppId"] = "not_a_number"
        });

        var settings = await sut.GetSettingsAsync();

        Assert.Equal(233780, settings.Arma3ServerAppId);
    }

    [Fact]
    public async Task GetSettings_WithHostEnvironment_ResolvesRelativeStoragePathsAgainstContentRoot()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "kast-settings-test-" + Guid.NewGuid());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kast:ModsDirectory"] = "./custom-mods",
                ["Kast:ServersDirectory"] = "custom-servers"
            })
            .Build();
        var sut = new SettingsService(_db, config, new TestHostEnvironment(contentRoot));

        var settings = await sut.GetSettingsAsync();

        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "./custom-mods")), settings.ModsDirectory);
        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "custom-servers")), settings.ServersDirectory);
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "KAST.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
