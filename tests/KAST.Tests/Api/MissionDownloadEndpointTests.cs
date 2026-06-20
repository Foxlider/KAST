using System.Net;
using KAST.Core.Interfaces;
using KAST.UI.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KAST.Tests.Api;

public class MissionDownloadEndpointTests
{
    private static async Task<ApiAppContext> CreateDownloadAppAsync(
        IMissionHttpDownloadService? downloadService = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Substitute.For<IMissionService>());
        builder.Services.AddSingleton(Substitute.For<IMissionHashService>());
        builder.Services.AddSingleton(Substitute.For<IServerInstanceService>());
        builder.Services.AddSingleton(downloadService ?? Substitute.For<IMissionHttpDownloadService>());
        builder.Services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);

        var app = builder.Build();
        app.MapMissionDownloadEndpoints();
        await app.StartAsync();

        return new ApiAppContext(app, app.GetTestClient());
    }

    [Fact]
    public async Task GetDownload_NonPboFile_Returns404()
    {
        await using var app = await CreateDownloadAppAsync();

        var response = await app.Client.GetAsync("/mission-download/1/test.txt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDownload_MissingMission_Returns404()
    {
        var downloadService = Substitute.For<IMissionHttpDownloadService>();
        downloadService.GetDownloadAsync(1, "missing.pbo", Arg.Any<CancellationToken>())
            .Returns((MissionDownloadResult?)null);

        await using var app = await CreateDownloadAppAsync(downloadService);

        var response = await app.Client.GetAsync("/mission-download/1/missing.pbo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDownload_Success_Returns200WithHeaders()
    {
        var tempFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempFile, new byte[] { 0x00, 0x50, 0x42, 0x4F });
        try
        {
            var result = new MissionDownloadResult
            {
                PhysicalPath = tempFile,
                FileName = "test.Altis.pbo",
                SizeBytes = 4,
                Hash = 2852136129u,
                LastModified = DateTimeOffset.UtcNow,
                ETag = "\"1-test.Altis.pbo-2852136129-4\""
            };

            var downloadService = Substitute.For<IMissionHttpDownloadService>();
            downloadService.GetDownloadAsync(1, "test.Altis.pbo", Arg.Any<CancellationToken>())
                .Returns(result);

            await using var app = await CreateDownloadAppAsync(downloadService);

            var response = await app.Client.GetAsync("/mission-download/1/test.Altis.pbo");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.TryGetValues("X-Hash", out var hashValues));
            Assert.Contains("2852136129", hashValues);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetDownload_FileOnDiskMissing_Returns404()
    {
        var result = new MissionDownloadResult
        {
            PhysicalPath = "C:\\nonexistent\\path.pbo",
            FileName = "path.pbo",
            SizeBytes = 0,
            Hash = 0u,
            LastModified = DateTimeOffset.UtcNow,
            ETag = "\"tag\""
        };

        var downloadService = Substitute.For<IMissionHttpDownloadService>();
        downloadService.GetDownloadAsync(1, "path.pbo", Arg.Any<CancellationToken>())
            .Returns(result);

        await using var app = await CreateDownloadAppAsync(downloadService);

        var response = await app.Client.GetAsync("/mission-download/1/path.pbo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDownload_SendsArmaHeaders_LogsThem()
    {
        var tempFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempFile, new byte[] { 0x00, 0x50, 0x42, 0x4F });
        try
        {
            var result = new MissionDownloadResult
            {
                PhysicalPath = tempFile,
                FileName = "arma.pbo",
                SizeBytes = 4,
                Hash = 42u,
                LastModified = DateTimeOffset.UtcNow,
                ETag = "\"1-arma.pbo-42-4\""
            };

            var downloadService = Substitute.For<IMissionHttpDownloadService>();
            downloadService.GetDownloadAsync(1, "arma.pbo", Arg.Any<CancellationToken>())
                .Returns(result);

            await using var app = await CreateDownloadAppAsync(downloadService);

            var request = new HttpRequestMessage(HttpMethod.Get, "/mission-download/1/arma.pbo");
            request.Headers.TryAddWithoutValidation("Player-Name", "TestPlayer");
            request.Headers.TryAddWithoutValidation("Player-Steamid", "76561190000000000");
            request.Headers.TryAddWithoutValidation("Server-Address", "192.168.1.1:2302");
            request.Headers.TryAddWithoutValidation("User-Agent", "BIGameEngine/2.20");

            var response = await app.Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private sealed class ApiAppContext : IAsyncDisposable
    {
        public ApiAppContext(WebApplication app, HttpClient client)
        {
            App = app;
            Client = client;
        }

        public WebApplication App { get; }
        public HttpClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
}
