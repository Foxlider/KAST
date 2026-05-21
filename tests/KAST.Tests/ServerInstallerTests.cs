using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services.Content;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace KAST.Tests;

public class ServerInstallerTests
{
    [Fact]
    public void PlanSteps_WhenInstanceMissing_ReturnsBaseOnly()
    {
        var steam = Substitute.For<ISteamService>();
        var fs = Substitute.For<IFileSystemService>();
        var logger = Substitute.For<ILogger<ServerInstaller>>();
        var sut = new ServerInstaller(steam, fs, Substitute.For<IHttpClientFactory>(), logger);

        var steps = sut.PlanSteps(new ContentInstallRequest
        {
            Type = ContentType.Server,
            DestinationPath = "/tmp/server",
            ServerInstanceId = 1
        });

        Assert.Single(steps);
        Assert.Equal("Arma 3 Dedicated Server", steps[0].Name);
    }

    [Fact]
    public void PlanSteps_OnlyIncludesEnabledDlcs()
    {
        var steam = Substitute.For<ISteamService>();
        var fs = Substitute.For<IFileSystemService>();
        var logger = Substitute.For<ILogger<ServerInstaller>>();
        var sut = new ServerInstaller(steam, fs, Substitute.For<IHttpClientFactory>(), logger);

        var instance = new ServerInstance
        {
            Name = "Test",
            ContactDlc = true,
            GmDlc = true,
            PfDlc = false
        };

        var steps = sut.PlanSteps(new ContentInstallRequest
        {
            Type = ContentType.Server,
            DestinationPath = "/tmp/server",
            Instance = instance,
            ServerInstanceId = 1
        });

        // On Windows an extra DirectX step is appended by PlanSteps.
        int expectedCount = 3 + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 1 : 0);
        Assert.Equal(expectedCount, steps.Count); // base + 2 dlcs (+ DirectX on Windows)
        Assert.Equal("Arma 3 Dedicated Server", steps[0].Name);
        Assert.Contains(steps, s => s.Name == "Contact");
        Assert.Contains(steps, s => s.Name == "Global Mobilization");
        Assert.DoesNotContain(steps, s => s.Name == "S.O.G. Prairie Fire");
    }

    [Fact]
    public async Task InstallAsync_WhenSteamCannotConnect_Throws()
    {
        var steam = Substitute.For<ISteamService>();
        steam.IsConnected.Returns(false);
        steam.LoginAnonymousAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var fs = Substitute.For<IFileSystemService>();
        var logger = Substitute.For<ILogger<ServerInstaller>>();
        var sut = new ServerInstaller(steam, fs, Substitute.For<IHttpClientFactory>(), logger);

        var instance = new ServerInstance { Id = 10, Name = "Srv" };
        var request = new ContentInstallRequest
        {
            Type = ContentType.Server,
            DestinationPath = "/tmp/server",
            ServerInstanceId = 10,
            Instance = instance,
            MaxParallelDownloads = 2
        };

        var state = new ContentInstallState
        {
            Key = "server:10",
            Type = ContentType.Server,
            Label = "Srv",
            Steps = sut.PlanSteps(request).ToList()
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InstallAsync(request, state, CancellationToken.None));
    }

    [Fact]
    public async Task InstallAsync_WhenInstanceMissing_Throws()
    {
        var steam = Substitute.For<ISteamService>();
        var fs = Substitute.For<IFileSystemService>();
        var logger = Substitute.For<ILogger<ServerInstaller>>();
        var sut = new ServerInstaller(steam, fs, Substitute.For<IHttpClientFactory>(), logger);

        var request = new ContentInstallRequest
        {
            Type = ContentType.Server,
            DestinationPath = "/tmp/server",
            ServerInstanceId = 12,
            Instance = null,
            MaxParallelDownloads = 4
        };

        var state = new ContentInstallState
        {
            Key = "server:12",
            Type = ContentType.Server,
            Label = "Srv",
            Steps = [new ContentStep { Name = "Step" }]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InstallAsync(request, state, CancellationToken.None));
    }

    [Fact]
    public async Task InstallAsync_Connected_DownloadsBaseAndEnabledDlcs()
    {
        var steam = Substitute.For<ISteamService>();
        steam.IsConnected.Returns(true);
        steam.IsAuthenticated.Returns(false);
        steam.DownloadAppAsync(
                Arg.Any<uint>(), Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<IProgress<string>>(),
                Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<uint[]?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<IProgress<double>>().Report(100);
                return Task.CompletedTask;
            });

        var fs = Substitute.For<IFileSystemService>();
        var logger = Substitute.For<ILogger<ServerInstaller>>();
        var sut = new ServerInstaller(steam, fs, Substitute.For<IHttpClientFactory>(), logger);

        var instance = new ServerInstance
        {
            Id = 12,
            Name = "Srv",
            ContactDlc = true,
            GmDlc = true
        };

        var request = new ContentInstallRequest
        {
            Type = ContentType.Server,
            DestinationPath = "/tmp/server",
            ServerInstanceId = 12,
            Instance = instance,
            MaxParallelDownloads = 4
        };

        var state = new ContentInstallState
        {
            Key = "server:12",
            Type = ContentType.Server,
            Label = "Srv",
            Steps = sut.PlanSteps(request).ToList()
        };

        await sut.InstallAsync(request, state, CancellationToken.None);

        // On Windows the DirectX step may be skipped (already installed) rather than completed.
        Assert.All(state.Steps, s => Assert.True(
            s.Status is ContentStepStatus.Completed or ContentStepStatus.Skipped,
            $"Step '{s.Name}' ended with unexpected status {s.Status}"));
        await steam.Received(3).DownloadAppAsync(
            ServerInstaller.Arma3ServerAppId,
            "/tmp/server",
            Arg.Any<IProgress<double>>(),
            Arg.Any<IProgress<string>>(),
            Arg.Any<bool>(),
            Arg.Any<string>(),
            Arg.Any<uint[]?>(),
            4,
            Arg.Any<CancellationToken>());
        fs.Received(1).SetExecutable(Arg.Is<string>(s => s.Contains("arma3server_x64")));
    }

    [Fact]
    public void Validate_WhenInstallMissing_ReturnsSingleFailure()
    {
        var steam = Substitute.For<ISteamService>();
        var fs = Substitute.For<IFileSystemService>();
        var logger = Substitute.For<ILogger<ServerInstaller>>();
        var sut = new ServerInstaller(steam, fs, Substitute.For<IHttpClientFactory>(), logger);

        var results = sut.Validate(new ContentInstallRequest
        {
            Type = ContentType.Server,
            DestinationPath = "/missing/kast/server"
        });

        Assert.Single(results);
        Assert.False(results[0].OK);
    }

    [Fact]
    public void Validate_WhenInstallPresent_ReturnsCoreChecks()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kast-server-validate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "arma3server_x64.exe" : "arma3server_x64";
            var exePath = Path.Combine(root, exeName);
            File.WriteAllText(exePath, "bin");
            Directory.CreateDirectory(Path.Combine(root, "addons"));
            Directory.CreateDirectory(Path.Combine(root, "dta"));
            Directory.CreateDirectory(Path.Combine(root, "keys"));
            Directory.CreateDirectory(Path.Combine(root, "gm"));

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(exePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            var steam = Substitute.For<ISteamService>();
            var fs = Substitute.For<IFileSystemService>();
            var logger = Substitute.For<ILogger<ServerInstaller>>();
            var sut = new ServerInstaller(steam, fs, Substitute.For<IHttpClientFactory>(), logger);

            var results = sut.Validate(new ContentInstallRequest
            {
                Type = ContentType.Server,
                DestinationPath = root,
                Instance = new ServerInstance { GmDlc = true }
            });

            Assert.Contains(results, r => r.Label == "Install directory" && r.OK);
            Assert.Contains(results, r => r.Label == "Server executable" && r.OK);
            Assert.Contains(results, r => r.Label == "Global Mobilization (gm/)" && r.OK);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); }
                catch
                {
                    // Best-effort cleanup for temp test data.
                }
            }
        }
    }

    [Fact]
    public void IsDirectXInstalled_IsTrueOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.True(ServerInstaller.IsDirectXInstalled());
    }
}
