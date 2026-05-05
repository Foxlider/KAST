namespace KAST.Core.Interfaces;

/// <summary>
/// Shared filesystem operations used by content installers:
/// size calculation, zip extraction, symlinks, chmod.
/// </summary>
public interface IFileSystemService
{
    /// <summary>Recursively calculates the total size of a directory in bytes.</summary>
    long GetDirectorySize(string path);

    /// <summary>Extracts a ZIP archive into <paramref name="destinationPath"/>.</summary>
    Task ExtractZipAsync(string zipPath, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Creates a directory symlink. Recreates if the symlink already exists.</summary>
    void CreateOrUpdateSymlink(string linkPath, string targetPath);

    /// <summary>Sets the Unix executable bit on a file (no-op on Windows).</summary>
    void SetExecutable(string filePath);
}
