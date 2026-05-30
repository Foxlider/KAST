using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services.Content;
using NSubstitute;

namespace KAST.Tests;

public class SteamModInstallerTests
{
    [Fact]
    public void PlanSteps_ReturnsDownloadThenSize()
    {
        var steam = Substitute.For<ISteamService>();
        var fs = Substitute.For<IFileSystemService>();
        var sut = new SteamModInstaller(steam, fs);

        var steps = sut.PlanSteps(new ContentInstallRequest
        {
            Type = ContentType.SteamMod,
            DestinationPath = "/tmp/mod",
            WorkshopId = 333310405
        });

        Assert.Equal(2, steps.Count);
        Assert.Equal("Download from Workshop", steps[0].Name);
        Assert.Equal("Calculate size", steps[1].Name);
    }

    [Fact]
    public async Task InstallAsync_WhenNotConnectedAndLoginFails_Throws()
    {
        var steam = Substitute.For<ISteamService>();
        steam.IsConnected.Returns(false);
        steam.LoginAnonymousAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var fs = Substitute.For<IFileSystemService>();
        var sut = new SteamModInstaller(steam, fs);

        var request = new ContentInstallRequest
        {
            Type = ContentType.SteamMod,
            DestinationPath = "/tmp/mod",
            WorkshopId = 333310405
        };

        var state = new ContentInstallState
        {
            Key = "mod:1",
            Type = ContentType.SteamMod,
            Label = "Mod 1",
            Steps = sut.PlanSteps(request).ToList()
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InstallAsync(request, state, CancellationToken.None));
        await steam.Received(1).LoginAnonymousAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallAsync_WhenConnected_DownloadsAndCompletesSteps()
    {
        var steam = Substitute.For<ISteamService>();
        steam.IsConnected.Returns(true);
        steam.DownloadWorkshopItemAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<IProgress<DownloadFileProgress>>(), Arg.Any<IProgress<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<IProgress<double>>().Report(100);
                return Task.FromResult(0UL);
            });

        var fs = Substitute.For<IFileSystemService>();
        fs.GetDirectorySize("/tmp/mod").Returns(8192L);

        var sut = new SteamModInstaller(steam, fs);

        var request = new ContentInstallRequest
        {
            Type = ContentType.SteamMod,
            DestinationPath = "/tmp/mod",
            WorkshopId = 333310405
        };

        var state = new ContentInstallState
        {
            Key = "mod:2",
            Type = ContentType.SteamMod,
            Label = "Mod 2",
            Steps = sut.PlanSteps(request).ToList()
        };

        await sut.InstallAsync(request, state, CancellationToken.None);

        Assert.All(state.Steps, s => Assert.Equal(ContentStepStatus.Completed, s.Status));
        await steam.Received(1).DownloadWorkshopItemAsync(333310405, "/tmp/mod", Arg.Any<IProgress<double>>(), Arg.Any<IProgress<DownloadFileProgress>>(), Arg.Any<IProgress<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        fs.Received(1).GetDirectorySize("/tmp/mod");
    }

    [Fact]
    public async Task InstallAsync_WhenLoginSucceeds_ContinuesToDownload()
    {
        var steam = Substitute.For<ISteamService>();
        steam.IsConnected.Returns(false, true, true);
        steam.LoginAnonymousAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        steam.DownloadWorkshopItemAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<IProgress<DownloadFileProgress>>(), Arg.Any<IProgress<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<IProgress<double>>().Report(100);
                return Task.FromResult(0UL);
            });

        var fs = Substitute.For<IFileSystemService>();
        fs.GetDirectorySize("/tmp/mod").Returns(1234L);

        var sut = new SteamModInstaller(steam, fs);
        var request = new ContentInstallRequest
        {
            Type = ContentType.SteamMod,
            DestinationPath = "/tmp/mod",
            WorkshopId = 333310405
        };

        var state = new ContentInstallState
        {
            Key = "mod:3",
            Type = ContentType.SteamMod,
            Label = "Mod 3",
            Steps = sut.PlanSteps(request).ToList()
        };

        await sut.InstallAsync(request, state, CancellationToken.None);

        await steam.Received(1).LoginAnonymousAsync(Arg.Any<CancellationToken>());
        await steam.Received(1).DownloadWorkshopItemAsync(333310405, "/tmp/mod", Arg.Any<IProgress<double>>(), Arg.Any<IProgress<DownloadFileProgress>>(), Arg.Any<IProgress<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.All(state.Steps, s => Assert.Equal(ContentStepStatus.Completed, s.Status));
    }

    [Fact]
    public void Validate_ReturnsFailureWhenMissing()
    {
        var steam = Substitute.For<ISteamService>();
        var fs = Substitute.For<IFileSystemService>();
        var sut = new SteamModInstaller(steam, fs);

        var results = sut.Validate(new ContentInstallRequest
        {
            Type = ContentType.SteamMod,
            DestinationPath = "/definitely-missing-kast-path"
        });

        Assert.Single(results);
        Assert.False(results[0].OK);
    }

    [Fact]
    public void Validate_WhenDirectoryExists_ReturnsSizeCheck()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kast-steam-validate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            var steam = Substitute.For<ISteamService>();
            var fs = Substitute.For<IFileSystemService>();
            fs.GetDirectorySize(path).Returns(5555L);
            var sut = new SteamModInstaller(steam, fs);

            var results = sut.Validate(new ContentInstallRequest
            {
                Type = ContentType.SteamMod,
                DestinationPath = path
            });

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.OK));
        }
        finally
        {
            if (Directory.Exists(path))
            {
                try { Directory.Delete(path, recursive: true); }
                catch
                {
                    // Best-effort cleanup for temp test data.
                }
            }
        }
    }
}
