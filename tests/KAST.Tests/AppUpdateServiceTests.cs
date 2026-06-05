using System.IO.Compression;
using System.Net;
using KAST.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace KAST.Tests;

public class AppUpdateServiceTests
{
    [Fact]
    public void SelectAsset_Nightly_RequiresExactRollingAssetName()
    {
        var assets = new[]
        {
            new GitHubReleaseAsset("kast-win-x64-nightly.zip", 123, "https://example.test/win.zip"),
            new GitHubReleaseAsset("win-x64.zip", 456, "https://example.test/stable.zip")
        };

        var selected = AppUpdateService.SelectAsset(assets, "win-x64", isNightly: true);

        Assert.NotNull(selected);
        Assert.Equal("kast-win-x64-nightly.zip", selected!.Name);
    }

    [Theory]
    [InlineData("kast-win-x64-v1.2.3.zip")]
    [InlineData("win-x64.zip")]
    public void SelectAsset_Stable_AllowsRidContainingArchiveNames(string assetName)
    {
        var assets = new[]
        {
            new GitHubReleaseAsset(assetName, 123, "https://example.test/update.zip")
        };

        var selected = AppUpdateService.SelectAsset(assets, "win-x64", isNightly: false);

        Assert.NotNull(selected);
        Assert.Equal(assetName, selected!.Name);
    }

    [Fact]
    public async Task CheckForUpdates_MissingStableRelease_ReturnsUnavailable()
    {
        var sut = CreateService(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await sut.CheckForUpdatesAsync("stable");

        Assert.False(result.IsChannelAvailable);
        Assert.False(result.IsUpdateAvailable);
        Assert.Contains("does not have", result.Message);
    }

    [Fact]
    public async Task CheckForUpdates_DockerDeployment_IsNotifyOnly()
    {
        var rid = AppUpdateService.GetCurrentRuntimeIdentifier();
        if (rid is null)
            return;

        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
        try
        {
            var extension = rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".tar.gz";
            var json = $$"""
{
  "tag_name": "nightly",
  "name": "Nightly - 1.2.3-nightly.20260602.abcdef0",
  "prerelease": true,
  "draft": false,
  "published_at": "2026-06-02T09:21:24Z",
  "html_url": "https://github.com/Foxlider/KAST/releases/tag/nightly",
  "assets": [
    {
      "name": "kast-{{rid}}-nightly{{extension}}",
      "size": 123,
      "browser_download_url": "https://example.test/update"
    }
  ]
}
""";
            var sut = CreateService(_ => JsonResponse(json));

            var result = await sut.CheckForUpdatesAsync("dev");

            Assert.True(result.IsDocker);
            Assert.False(result.IsNativeSupported);
            Assert.True(result.IsChannelAvailable);
            Assert.Contains("Docker", result.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", null);
        }
    }

    [Fact]
    public void ExtractArchive_RejectsZipPathTraversal()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "kast-update-test-" + Guid.NewGuid());
        var archivePath = Path.Combine(tempRoot, "bad.zip");
        var extractPath = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(extractPath);

        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../evil.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("bad");
            }

            Assert.Throws<InvalidOperationException>(() => AppUpdateService.ExtractArchive(archivePath, extractPath));
            Assert.False(File.Exists(Path.Combine(tempRoot, "evil.txt")));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static AppUpdateService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new DelegateHandler(responder));
        return new AppUpdateService(
            httpClient,
            Substitute.For<IHostApplicationLifetime>(),
            Substitute.For<ILogger<AppUpdateService>>());
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
