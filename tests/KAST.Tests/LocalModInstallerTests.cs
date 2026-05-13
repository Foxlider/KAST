using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services.Content;
using NSubstitute;

namespace KAST.Tests;

public class LocalModInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kast-local-installer-tests-{Guid.NewGuid():N}");

    public LocalModInstallerTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PlanSteps_Zip_IncludesExtractAndSize()
    {
        var fs = Substitute.For<IFileSystemService>();
        var sut = new LocalModInstaller(fs);

        var steps = sut.PlanSteps(new ContentInstallRequest
        {
            Type = ContentType.LocalMod,
            DestinationPath = "/tmp/mod",
            SourcePath = "/tmp/mod.zip"
        });

        Assert.Equal(2, steps.Count);
        Assert.Equal("Extract archive", steps[0].Name);
        Assert.Equal("Calculate size", steps[1].Name);
    }

    [Fact]
    public void PlanSteps_Folder_IncludesOnlySize()
    {
        var fs = Substitute.For<IFileSystemService>();
        var sut = new LocalModInstaller(fs);

        var steps = sut.PlanSteps(new ContentInstallRequest
        {
            Type = ContentType.LocalMod,
            DestinationPath = "/tmp/mod",
            SourcePath = "/tmp/mod-folder"
        });

        Assert.Single(steps);
        Assert.Equal("Calculate size", steps[0].Name);
    }

    [Fact]
    public async Task InstallAsync_ZipPath_ExtractsAndCompletesAllSteps()
    {
        var fs = Substitute.For<IFileSystemService>();
        fs.GetDirectorySize(Arg.Any<string>()).Returns(1024L);
        fs.ExtractZipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<IProgress<double>>().Report(100);
                return Task.CompletedTask;
            });

        var sut = new LocalModInstaller(fs);

        var request = new ContentInstallRequest
        {
            Type = ContentType.LocalMod,
            DestinationPath = "/tmp/mod",
            SourcePath = "/tmp/mod.zip"
        };

        var state = new ContentInstallState
        {
            Key = "mod:1",
            Type = ContentType.LocalMod,
            Label = "Mod 1",
            Steps = sut.PlanSteps(request).ToList()
        };

        await sut.InstallAsync(request, state, CancellationToken.None);

        Assert.All(state.Steps, s => Assert.Equal(ContentStepStatus.Completed, s.Status));
        await fs.Received(1).ExtractZipAsync("/tmp/mod.zip", "/tmp/mod", Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>());
        fs.Received(1).GetDirectorySize("/tmp/mod");
    }

    [Fact]
    public async Task InstallAsync_FolderPath_CompletesSizeStep()
    {
        var fs = Substitute.For<IFileSystemService>();
        fs.GetDirectorySize(Arg.Any<string>()).Returns(2048L);

        var sut = new LocalModInstaller(fs);

        var request = new ContentInstallRequest
        {
            Type = ContentType.LocalMod,
            DestinationPath = "/tmp/mod-folder",
            SourcePath = "/tmp/mod-folder"
        };

        var state = new ContentInstallState
        {
            Key = "mod:2",
            Type = ContentType.LocalMod,
            Label = "Mod 2",
            Steps = sut.PlanSteps(request).ToList()
        };

        await sut.InstallAsync(request, state, CancellationToken.None);

        Assert.Single(state.Steps);
        Assert.Equal(ContentStepStatus.Completed, state.Steps[0].Status);
        await fs.DidNotReceiveWithAnyArgs().ExtractZipAsync(default!, default!, default, default);
        fs.Received(1).GetDirectorySize("/tmp/mod-folder");
    }

    [Fact]
    public void Validate_ReturnsExpectedChecks_WhenDirectoryExists()
    {
        var fs = Substitute.For<IFileSystemService>();
        fs.GetDirectorySize(Arg.Any<string>()).Returns(4096L);

        var sut = new LocalModInstaller(fs);
        var path = Path.Combine(_root, "mod-dir");
        Directory.CreateDirectory(path);

        var results = sut.Validate(new ContentInstallRequest
        {
            Type = ContentType.LocalMod,
            DestinationPath = path
        });

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.OK));
    }

    [Fact]
    public void Validate_WhenDirectoryMissing_ReturnsSingleFailure()
    {
        var fs = Substitute.For<IFileSystemService>();
        var sut = new LocalModInstaller(fs);

        var results = sut.Validate(new ContentInstallRequest
        {
            Type = ContentType.LocalMod,
            DestinationPath = Path.Combine(_root, "missing")
        });

        Assert.Single(results);
        Assert.False(results[0].OK);
    }
}
