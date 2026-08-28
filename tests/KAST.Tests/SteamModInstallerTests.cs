using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services.Content;
using KAST.Infrastructure.Services;
using NSubstitute;

namespace KAST.Tests;

public class SteamModInstallerTests
{
    [Fact]
    public void PlanSteps_ReturnsDownloadThenSize()
    {
        var sut = CreateSut();

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
        var steamAuthentication = Substitute.For<ISteamAuthenticationService>();
        steamAuthentication.IsConnected.Returns(false);
        steamAuthentication.LoginAnonymousAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var sut = CreateSut(steamAuthentication);

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
        await steamAuthentication.Received(1).LoginAnonymousAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallAsync_WhenConnected_DownloadsAndCompletesSteps()
    {
        var steamAuthentication = Substitute.For<ISteamAuthenticationService>();
        steamAuthentication.IsConnected.Returns(true);
        var workshop = Substitute.For<ISteamWorkshopDownloadService>();
        workshop.DownloadWorkshopItemAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<IProgress<double>>(), ct:Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<IProgress<double>>().Report(100);
                return Task.FromResult(0UL);
            });

        var fs = Substitute.For<IFileSystemService>();
        fs.GetDirectorySize("/tmp/mod").Returns(8192L);

        var sut = CreateSut(steamAuthentication, workshop, fs);

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
        await workshop.Received(1).DownloadWorkshopItemAsync(333310405, "/tmp/mod", Arg.Any<IProgress<double>>(), ct:Arg.Any<CancellationToken>());
        fs.Received(1).GetDirectorySize("/tmp/mod");
    }

    [Fact]
    public async Task InstallAsync_WhenLoginSucceeds_ContinuesToDownload()
    {
        var steamAuthentication = Substitute.For<ISteamAuthenticationService>();
        steamAuthentication.IsConnected.Returns(false, true, true);
        steamAuthentication.LoginAnonymousAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        var workshop = Substitute.For<ISteamWorkshopDownloadService>();
        workshop.DownloadWorkshopItemAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<IProgress<double>>(), ct:Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<IProgress<double>>().Report(100);
                return Task.FromResult(0UL);
            });

        var fs = Substitute.For<IFileSystemService>();
        fs.GetDirectorySize("/tmp/mod").Returns(1234L);

        var sut = CreateSut(steamAuthentication, workshop, fs);
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

        await steamAuthentication.Received(1).LoginAnonymousAsync(Arg.Any<CancellationToken>());
        await workshop.Received(1).DownloadWorkshopItemAsync(333310405, "/tmp/mod", Arg.Any<IProgress<double>>(), ct:Arg.Any<CancellationToken>());
        Assert.All(state.Steps, s => Assert.Equal(ContentStepStatus.Completed, s.Status));
    }

    [Fact]
    public void Validate_ReturnsFailureWhenMissing()
    {
        var sut = CreateSut();

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
            var fs = Substitute.For<IFileSystemService>();
            fs.GetDirectorySize(path).Returns(5555L);
            var sut = CreateSut(fileSystem: fs);

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

    private static SteamModInstaller CreateSut(
        ISteamAuthenticationService? steamAuthentication = null,
        ISteamWorkshopDownloadService? workshop = null,
        IFileSystemService? fileSystem = null) =>
        new(
            steamAuthentication ?? Substitute.For<ISteamAuthenticationService>(),
            workshop ?? Substitute.For<ISteamWorkshopDownloadService>(),
            fileSystem ?? Substitute.For<IFileSystemService>(),
            new OutputSanitizer());
}
