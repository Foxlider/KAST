
using System.Runtime.CompilerServices;

namespace KAST.Core.Services
{
    public enum FileSystemItemType
    {
        Folder,
        File
    }

    public sealed record FileSystemItem
    {
        public string Name { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public FileSystemItemType ItemType { get; init; }
        // For folders: indicates whether it contains files or subfolders.
        public bool HasChildren { get; init; }
    }

    public class FileSystemService
    {
        private readonly string _rootPath;
        public string RootPath => _rootPath;

        public FileSystemService(string rootPath)
        {
            _rootPath = rootPath;
        }

        public async IAsyncEnumerable<FileSystemItem> GetFolders(string relativePath = "", [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (relativePath == "/")
                relativePath = "";
            var fullPath = Path.Combine(_rootPath, relativePath ?? string.Empty);
            if (!Directory.Exists(fullPath))
                yield break;

            // Enumerate directories
            foreach (var dir in Directory.EnumerateDirectories(fullPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var folderName = Path.GetFileName(dir) ?? string.Empty;
                var itemRelativePath = Path.Combine(relativePath ?? string.Empty, folderName);
                bool hasChildren = Directory.EnumerateFileSystemEntries(dir).Any();

                // Make method observe asynchronous context
                await Task.Yield();

                yield return new FileSystemItem
                {
                    Name = folderName,
                    RelativePath = itemRelativePath,
                    ItemType = FileSystemItemType.Folder,
                    HasChildren = hasChildren
                };
            }

            // Enumerate files
            foreach (var file in Directory.EnumerateFiles(fullPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(file) ?? string.Empty;
                var itemRelativePath = Path.Combine(relativePath ?? string.Empty, fileName);

                await Task.Yield();

                yield return new FileSystemItem
                {
                    Name = fileName,
                    RelativePath = itemRelativePath,
                    ItemType = FileSystemItemType.File,
                    HasChildren = false
                };
            }
        }

        /// <summary>
        /// Creates a new folder under the specified parent relative path.
        /// </summary>
        /// <remarks>If the specified folder already exists, no action is taken. The method ensures that
        /// the full path is safely constructed by combining the root path, the parent relative path, and the folder
        /// name.</remarks>
        /// <param name="parentRelativePath">The relative path of the parent folder. Use "/" or an empty string to specify the root folder.</param>
        /// <param name="folderName">The name of the folder to create. Must not be null, empty, or consist only of whitespace.</param>
        /// <returns>A task that represents the asynchronous operation. The task completes immediately upon folder creation.</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="folderName"/> is null, empty, or consists only of whitespace.</exception>
        public Task CreateFolder(string parentRelativePath, string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                throw new ArgumentException("folderName must not be empty", nameof(folderName));

            if (parentRelativePath == "/")
                parentRelativePath = "";

            var safeParent = parentRelativePath ?? string.Empty;
            var fullPath = Path.Combine(_rootPath, safeParent, folderName);

            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Renames the folder specified by the given relative path to the new name within the same parent.
        /// </summary>
        /// <param name="relativePath">Relative path (from root) of the folder to rename. Cannot be root or empty.</param>
        /// <param name="newName">New folder name (single path segment).</param>
        /// <returns>A task representing the operation.</returns>
        /// <exception cref="InvalidOperationException">If relativePath is invalid or root.</exception>
        /// <exception cref="ArgumentException">If newName is invalid.</exception>
        public Task RenameFolder(string relativePath, string newName)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath == "/")
                throw new InvalidOperationException("Cannot rename root or empty path");

            if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Invalid new folder name", nameof(newName));

            // Compute old and new full paths
            var oldFull = Path.Combine(_rootPath, relativePath ?? string.Empty);
            var parentRelative = Path.GetDirectoryName(relativePath) ?? string.Empty;
            var newFull = Path.Combine(_rootPath, parentRelative, newName);

            if (!Directory.Exists(oldFull))
                throw new InvalidOperationException("Source folder does not exist");

            if (Directory.Exists(newFull))
                throw new InvalidOperationException("Target folder already exists");

            Directory.Move(oldFull, newFull);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Deletes the folder specified by the given relative path, including all its contents.
        /// </summary>
        /// <remarks>This method deletes the folder and all its contents recursively. Ensure that the
        /// specified path is correct and does not point to critical data, as the operation cannot be undone.</remarks>
        /// <param name="relativePath">The relative path of the folder to delete. Must not be the root path ("/") or empty.</param>
        /// <returns>A task that represents the asynchronous delete operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown if <paramref name="relativePath"/> is null, empty, consists only of whitespace, or is the root path
        /// ("/").</exception>
        public Task DeleteFolder(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath == "/")
                throw new InvalidOperationException("Cannot delete root or empty path");

            var fullPath = Path.Combine(_rootPath, relativePath ?? string.Empty);

            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, true);

            return Task.CompletedTask;
        }
    }
}
