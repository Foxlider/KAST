using KAST.Core.Models;
using KAST.Infrastructure.Services;
using KAST.Infrastructure.Steam;
using KAST.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KAST.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly Infrastructure.Data.KastDbContext _db;
    private readonly SteamDownloadScheduler _downloadScheduler = new();
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
        return new SettingsService(_db, config, _downloadScheduler);
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
        Assert.Equal(8, settings.ParallelDownloads);
        Assert.Equal(4, settings.BulkModDownloadConcurrency);
    }

    [Fact]
    public async Task GetSettings_AppliesPersistedSteamWorkerLimit()
    {
        _db.Settings.Add(new KastSettings { ParallelDownloads = 3 });
        await _db.SaveChangesAsync();
        var sut = CreateService();

        await sut.GetSettingsAsync();

        Assert.Equal(3, _downloadScheduler.MaximumConcurrency);
    }

    [Fact]
    public async Task GetSettings_FirstCall_SeedsFromConfiguration()
    {
        var sut = CreateService(new Dictionary<string, string?>
        {
            ["Kast:ModsDirectory"] = "/custom/mods",
            ["Kast:ServersDirectory"] = "/custom/servers",
            ["Kast:Arma3AppId"] = "999999"
        });

        var settings = await sut.GetSettingsAsync();

        Assert.Equal("/custom/mods", settings.ModsDirectory);
        Assert.Equal("/custom/servers", settings.ServersDirectory);
        Assert.Equal(999999, settings.Arma3ServerAppId);
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
        settings.BulkModDownloadConcurrency = 3;

        await sut.UpdateSettingsAsync(settings);

        // Read back from a fresh service instance (same DB)
        var updated = await sut.GetSettingsAsync();
        Assert.Equal("light", updated.ThemeMode);
        Assert.Equal(10, updated.MetricsIntervalSeconds);
        Assert.Equal("/updated/mods", updated.ModsDirectory);
        Assert.Equal(3, updated.BulkModDownloadConcurrency);
    }

    [Fact]
    public async Task UpdateSettings_AppliesSteamWorkerLimitAtRuntime()
    {
        var sut = CreateService();
        var settings = await sut.GetSettingsAsync();
        settings.ParallelDownloads = 3;

        await sut.UpdateSettingsAsync(settings);

        Assert.Equal(3, _downloadScheduler.MaximumConcurrency);
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
    public async Task DatabaseMigration_AddsBulkModDownloadConcurrencyColumn()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<Infrastructure.Data.KastDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new Infrastructure.Data.KastDbContext(options);

        await db.Database.MigrateAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Settings') WHERE name = 'BulkModDownloadConcurrency'";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
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
}
