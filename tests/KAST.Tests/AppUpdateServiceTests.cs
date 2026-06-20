using System.IO.Compression;
using System.Net;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace KAST.Tests;

public class AppUpdateServiceTests
{
    [Fact]
    public void GetChannels_IncludesCasterNightly()
    {
        var sut = CreateService(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var channel = Assert.Single(sut.GetChannels(), c => c.Id == "caster-nightly");

        Assert.Equal("Caster Nightly", channel.Label);
        Assert.Equal("bluefield-creator", channel.Owner);
        Assert.Equal("KAST", channel.Repository);
        Assert.Equal(AppUpdateReleaseSelection.Tag, channel.ReleaseSelection);
        Assert.Equal("nightly", channel.TagName);
        Assert.True(channel.IsPrerelease);
    }

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
    public async Task CheckForUpdates_CasterNightly_UsesBluefieldNightlyReleaseTag()
    {
        var rid = AppUpdateService.GetCurrentRuntimeIdentifier();
        if (rid is null)
            return;

        var extension = rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".tar.gz";
        Uri? requestedUri = null;
        var json = $$"""
{
  "tag_name": "nightly",
  "name": "Caster Nightly - 1.2.3-nightly.20260602.abcdef0",
  "prerelease": true,
  "draft": false,
  "published_at": "2026-06-02T09:21:24Z",
  "html_url": "https://github.com/bluefield-creator/KAST/releases/tag/nightly",
  "assets": [
    {
      "name": "kast-{{rid}}-nightly{{extension}}",
      "size": 123,
      "browser_download_url": "https://example.test/update"
    }
  ]
}
""";
        var sut = CreateService(request =>
        {
            requestedUri = request.RequestUri;
            return JsonResponse(json);
        });

        var result = await sut.CheckForUpdatesAsync("caster-nightly");

        Assert.Equal("/repos/bluefield-creator/KAST/releases/tags/nightly", requestedUri?.AbsolutePath);
        Assert.True(result.IsChannelAvailable);
        Assert.Equal("caster-nightly", result.Channel.Id);
        Assert.True(result.Channel.IsPrerelease);
        Assert.Equal("nightly", result.ReleaseTag);
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
            Assert.Equal(AppUpdateRestartMode.None, result.RestartMode);
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

    [Fact]
    public void BuildWindowsApplyScript_DirectProcess_RestartsExecutable()
    {
        var script = AppUpdateService.BuildWindowsApplyScript(
            123,
            @"C:\KAST\updates\current",
            @"C:\KAST",
            @"C:\Temp\backup",
            @"C:\KAST\KAST.exe",
            AppUpdateRestartMode.DirectProcess);

        Assert.Contains("start \"\" \"%EXE%\"", script);
        Assert.DoesNotContain("sc.exe stop KAST", script);
        Assert.DoesNotContain("sc.exe start KAST", script);
    }

    [Fact]
    public void BuildWindowsApplyScript_WindowsService_StopsAndStartsService()
    {
        var script = AppUpdateService.BuildWindowsApplyScript(
            123,
            @"C:\KAST\updates\current",
            @"C:\KAST",
            @"C:\Temp\backup",
            @"C:\KAST\KAST.exe",
            AppUpdateRestartMode.WindowsService);

        Assert.Contains("sc.exe stop KAST", script);
        Assert.Contains("sc.exe query KAST", script);
        Assert.Contains("STOPPED", script);
        Assert.Contains("sc.exe start KAST", script);
        Assert.DoesNotContain("start \"\" \"%EXE%\"", script);
    }

    [Fact]
    public void BuildLinuxApplyScript_RestartsExecutableWithNohup()
    {
        var script = AppUpdateService.BuildLinuxApplyScript(
            123,
            "/opt/kast/updates/current",
            "/opt/kast",
            "/tmp/backup",
            "/opt/kast/KAST");

        Assert.Contains("nohup \"$EXE\" >/dev/null 2>&1 &", script);
    }

    [Fact]
    public async Task ApplyStagedUpdate_DockerDeployment_IsNotifyOnly()
    {
        var sut = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            isDocker: () => true,
            startApplyScript: _ => throw new InvalidOperationException("Script should not start."));

        var result = await sut.ApplyStagedUpdateAsync("any-update");

        Assert.False(result.Success);
        Assert.Contains("Docker", result.Message);
    }

    [Fact]
    public async Task ApplyStagedUpdate_DirectProcess_WritesWindowsDirectScriptAndStops()
    {
        using var staged = CreateStagedUpdate();
        string? scriptPath = null;
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var sut = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            lifetime: lifetime,
            isWindows: () => true,
            isWindowsService: () => false,
            startApplyScript: path => scriptPath = path);

        var result = await sut.ApplyStagedUpdateAsync(staged.Id);

        Assert.True(result.Success);
        Assert.NotNull(scriptPath);
        var script = await File.ReadAllTextAsync(scriptPath!);
        Assert.Contains("start \"\" \"%EXE%\"", script);
        Assert.DoesNotContain("sc.exe start KAST", script);
        lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task ApplyStagedUpdate_WindowsService_WritesServiceScriptAndStops()
    {
        using var staged = CreateStagedUpdate();
        string? scriptPath = null;
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var hostService = Substitute.For<IHostServiceManager>();
        hostService.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new HostServiceStatus(
                true,
                true,
                "KAST",
                "KAST Panel",
                HostServiceRunState.Running,
                HostServiceStartupMode.Automatic,
                new HostServiceRecoveryOptions(true, 60, 1),
                Environment.ProcessPath,
                null));
        var sut = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            lifetime: lifetime,
            hostService: hostService,
            isWindows: () => true,
            isWindowsService: () => true,
            startApplyScript: path => scriptPath = path);

        var result = await sut.ApplyStagedUpdateAsync(staged.Id);

        Assert.True(result.Success);
        Assert.NotNull(scriptPath);
        var script = await File.ReadAllTextAsync(scriptPath!);
        Assert.Contains("sc.exe start KAST", script);
        Assert.DoesNotContain("start \"\" \"%EXE%\"", script);
        lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task ApplyStagedUpdate_WindowsServiceUnsupported_ReturnsFailure()
    {
        using var staged = CreateStagedUpdate();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var hostService = Substitute.For<IHostServiceManager>();
        hostService.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new HostServiceStatus(
                false,
                false,
                "KAST",
                "KAST Panel",
                HostServiceRunState.Unsupported,
                HostServiceStartupMode.Manual,
                new HostServiceRecoveryOptions(false, 60, 1),
                null,
                "Unsupported."));
        var sut = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            lifetime: lifetime,
            hostService: hostService,
            isWindows: () => true,
            isWindowsService: () => true,
            startApplyScript: _ => throw new InvalidOperationException("Script should not start."));

        var result = await sut.ApplyStagedUpdateAsync(staged.Id);

        Assert.False(result.Success);
        Assert.Contains("service control is not supported", result.Message);
        lifetime.DidNotReceive().StopApplication();
    }

    [Fact]
    public async Task ApplyStagedUpdate_WindowsServiceNotInstalled_ReturnsFailure()
    {
        using var staged = CreateStagedUpdate();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var hostService = Substitute.For<IHostServiceManager>();
        hostService.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new HostServiceStatus(
                true,
                true,
                "KAST",
                "KAST Panel",
                HostServiceRunState.NotInstalled,
                HostServiceStartupMode.Manual,
                new HostServiceRecoveryOptions(false, 60, 1),
                null,
                "Not installed."));
        var sut = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            lifetime: lifetime,
            hostService: hostService,
            isWindows: () => true,
            isWindowsService: () => true,
            startApplyScript: _ => throw new InvalidOperationException("Script should not start."));

        var result = await sut.ApplyStagedUpdateAsync(staged.Id);

        Assert.False(result.Success);
        Assert.Contains("service is not installed", result.Message);
        lifetime.DidNotReceive().StopApplication();
    }

    private static AppUpdateService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => CreateService(responder, lifetime: null, hostService: null);

    private static AppUpdateService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        IHostApplicationLifetime? lifetime = null,
        IHostServiceManager? hostService = null,
        Func<bool>? isDocker = null,
        Func<bool>? isWindows = null,
        Func<bool>? isWindowsService = null,
        Action<string>? startApplyScript = null)
    {
        var httpClient = new HttpClient(new DelegateHandler(responder));
        return new AppUpdateService(
            httpClient,
            lifetime ?? Substitute.For<IHostApplicationLifetime>(),
            hostService ?? Substitute.For<IHostServiceManager>(),
            Substitute.For<ILogger<AppUpdateService>>(),
            isDocker ?? AppUpdateService.IsDocker,
            isWindows ?? OperatingSystem.IsWindows,
            isWindowsService ?? (() => false),
            startApplyScript ?? (_ => { }));
    }

    private static StagedUpdate CreateStagedUpdate()
    {
        var id = "test-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(AppContext.BaseDirectory, "updates", id);
        Directory.CreateDirectory(Path.Combine(root, "extracted"));
        return new StagedUpdate(id, root);
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

    private sealed class StagedUpdate(string id, string root) : IDisposable
    {
        public string Id { get; } = id;

        public void Dispose()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
