using KAST.Infrastructure.Services;
using KAST.UI.Services;
using Microsoft.Extensions.Logging;

namespace KAST.Tests;

public sealed class KastFileLoggerProviderTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    [Fact]
    public void Log_InformationWritesSanitizedEntry_AndDebugIsExcluded()
    {
        using var provider = new KastFileLoggerProvider(_logDirectory, new OutputSanitizer());
        var logger = provider.CreateLogger("KAST.Tests.FileLogging");

        logger.LogDebug("Debug-only event");
        logger.LogInformation("Download started for {ModName}", "Test Mod");

        var logPath = Assert.Single(Directory.GetFiles(_logDirectory, "kast-*.log"));
        var entry = File.ReadAllText(logPath);
        Assert.Contains("[Information] KAST.Tests.FileLogging: Download started for Test Mod", entry);
        Assert.DoesNotContain("Debug-only event", entry);
    }

    public void Dispose()
    {
        if (Directory.Exists(_logDirectory))
            Directory.Delete(_logDirectory, recursive: true);
    }
}
