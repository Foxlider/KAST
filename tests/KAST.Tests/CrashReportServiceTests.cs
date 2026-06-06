using KAST.Infrastructure.Services;
using KAST.UI.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace KAST.Tests;

public class CrashReportServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "kast-crash-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RecordCrash_WritesCrashReportWithLogExcerpt()
    {
        var store = new KastLogStore();
        store.Add(new AppLogEntry(DateTime.Now, LogLevel.Error, "KAST.Tests", "recent error"));
        var sut = CreateService(store);

        sut.RecordCrash(new InvalidOperationException("boom"), "UnhandledException", isTerminating: true);

        var reports = sut.GetRecentReports();
        var report = Assert.Single(reports);
        Assert.Equal("UnhandledException", report.Kind);
        Assert.Contains("boom", report.Message);
        Assert.True(File.Exists(ToRealPath(report.Path)));
        Assert.Contains("recent error", File.ReadAllText(ToRealPath(report.Path)));
    }

    [Fact]
    public void RecordProcessStart_WithExistingMarker_WritesUncleanShutdownReport()
    {
        var sut = CreateService(new KastLogStore());

        sut.RecordProcessStart();
        sut.RecordProcessStart();

        var report = Assert.Single(sut.GetRecentReports(), r => r.Kind == "UncleanShutdown");
        Assert.Contains("previous process ended", report.Message);
    }

    private CrashReportService CreateService(KastLogStore store)
    {
        Directory.CreateDirectory(_tempRoot);
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(_tempRoot);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kast:CrashReportsDirectory"] = "crashes"
            })
            .Build();
        var sanitizer = new OutputSanitizer(new OutputSanitizer.VirtualPathRoot(_tempRoot, "TEST"));
        return new CrashReportService(environment, configuration, store, sanitizer);
    }

    private string ToRealPath(string displayPath)
        => displayPath.Replace("TEST", _tempRoot).Replace('/', Path.DirectorySeparatorChar);

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
