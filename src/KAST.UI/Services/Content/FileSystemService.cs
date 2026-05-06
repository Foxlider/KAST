using System.IO.Compression;
using System.Runtime.InteropServices;
using KAST.Core.Interfaces;

namespace KAST.UI.Services.Content;

public class FileSystemService : IFileSystemService
{
    public long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        return new DirectoryInfo(path)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }

    public async Task ExtractZipAsync(string zipPath, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationPath);

        using var archive = ZipFile.OpenRead(zipPath);
        int total = archive.Entries.Count;
        int done = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var destFile = Path.Combine(destinationPath, entry.FullName);

            // Prevent zip slip
            var fullDest = Path.GetFullPath(destFile);
            var fullBase = Path.GetFullPath(destinationPath + Path.DirectorySeparatorChar);
            if (!fullDest.StartsWith(fullBase, StringComparison.Ordinal))
                throw new InvalidOperationException($"Zip entry '{entry.FullName}' would extract outside the target directory.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullDest);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullDest)!);
                entry.ExtractToFile(fullDest, overwrite: true);
            }

            done++;
            progress?.Report((double)done / total * 100);
        }

        await Task.CompletedTask;
    }

    public void CreateOrUpdateSymlink(string linkPath, string targetPath)
    {
        if (Path.Exists(linkPath))
        {
            if (Directory.ResolveLinkTarget(linkPath, false) != null)
                Directory.Delete(linkPath);
            else
                return;
        }

        Directory.CreateSymbolicLink(linkPath, targetPath);
    }

    public void SetExecutable(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (!File.Exists(filePath)) return;

        File.SetUnixFileMode(filePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}
