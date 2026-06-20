using System.IO.Compression;
using System.Runtime.InteropServices;
using KAST.Infrastructure.Services.Content;

namespace KAST.Tests;

public class FileSystemServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kast-fs-tests-{Guid.NewGuid():N}");

    public FileSystemServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); }
            catch
            {
                // Best-effort cleanup for temp test data.
            }
        }
    }

    [Fact]
    public void GetDirectorySize_ReturnsZeroForMissingAndSumsNestedFiles()
    {
        var sut = new FileSystemService();
        var missing = Path.Combine(_root, "missing");
        Assert.Equal(0, sut.GetDirectorySize(missing));

        var dir = Path.Combine(_root, "data", "nested");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(_root, "data", "a.txt"), "abcd"); // 4
        File.WriteAllBytes(Path.Combine(dir, "b.bin"), new byte[9]);

        Assert.Equal(13, sut.GetDirectorySize(Path.Combine(_root, "data")));
    }

    [Fact]
    public async Task ExtractZipAsync_ExtractsFilesAndReportsProgress()
    {
        var sut = new FileSystemService();
        var zipPath = Path.Combine(_root, "mod.zip");
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(src, "folder"));
        File.WriteAllText(Path.Combine(src, "folder", "config.cpp"), "class CfgPatches {};");
        File.WriteAllText(Path.Combine(src, "readme.txt"), "hello");

        ZipFile.CreateFromDirectory(src, zipPath);

        var dest = Path.Combine(_root, "out");
        double last = 0;
        var progress = new Progress<double>(p => last = p);

        await sut.ExtractZipAsync(zipPath, dest, progress, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(dest, "folder", "config.cpp")));
        Assert.True(File.Exists(Path.Combine(dest, "readme.txt")));
        Assert.True(last > 0);
    }

    [Fact]
    public async Task ExtractZipAsync_PreventsZipSlip()
    {
        var sut = new FileSystemService();
        var zipPath = Path.Combine(_root, "evil.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escape.txt");
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync("pwn");
        }

        var dest = Path.Combine(_root, "safe");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExtractZipAsync(zipPath, dest, progress: null, CancellationToken.None));

        Assert.Contains("outside the target directory", ex.Message);
    }

    [Fact]
    public void CreateOrUpdateSymlink_DoesNotReplaceRealDirectory()
    {
        var sut = new FileSystemService();
        var target = Path.Combine(_root, "target");
        var linkPath = Path.Combine(_root, "link");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(linkPath); // real directory, not symlink

        sut.CreateOrUpdateSymlink(linkPath, target);

        Assert.True(Directory.Exists(linkPath));
        Assert.Null(Directory.ResolveLinkTarget(linkPath, returnFinalTarget: false));
    }

    [Fact]
    public void CreateOrUpdateSymlink_ReplacesExistingSymlink()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var probeLink = Path.Combine(_root, ".probe-link");
            try
            {
                Directory.CreateSymbolicLink(probeLink, _root);
                Directory.Delete(probeLink);
            }
            catch (IOException)
            {
                return;
            }
        }

        var sut = new FileSystemService();
        var oldTarget = Path.Combine(_root, "old-target");
        var newTarget = Path.Combine(_root, "new-target");
        var linkPath = Path.Combine(_root, "mod-link");

        Directory.CreateDirectory(oldTarget);
        Directory.CreateDirectory(newTarget);
        Directory.CreateSymbolicLink(linkPath, oldTarget);

        sut.CreateOrUpdateSymlink(linkPath, newTarget);

        var resolved = Directory.ResolveLinkTarget(linkPath, returnFinalTarget: true);
        Assert.NotNull(resolved);
        Assert.Equal(Path.GetFullPath(newTarget), Path.GetFullPath(resolved!.FullName));
    }

    [Fact]
    public void SetExecutable_SetsExecuteBitOnUnix()
    {
        var sut = new FileSystemService();
        var file = Path.Combine(_root, "server.bin");
        File.WriteAllText(file, "echo");

        sut.SetExecutable(file);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.True(File.Exists(file));
            return;
        }

        var mode = File.GetUnixFileMode(file);
        Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void SetExecutable_MissingFile_DoesNothing()
    {
        var sut = new FileSystemService();
        var missing = Path.Combine(_root, "missing.bin");

        var ex = Record.Exception(() => sut.SetExecutable(missing));
        Assert.Null(ex);
    }
}
