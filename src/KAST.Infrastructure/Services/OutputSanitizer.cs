using System.Text.RegularExpressions;
using KAST.Core.Interfaces;

namespace KAST.Infrastructure.Services;

public sealed partial class OutputSanitizer : IOutputSanitizer
{
    private const string UnknownPath = "unknown";
    private readonly Func<string, string?> _resolveFinalPath;
    private readonly IReadOnlyList<NormalizedVirtualPathRoot> _virtualRoots;

    public OutputSanitizer()
        : this(Array.Empty<VirtualPathRoot>())
    {
    }

    public OutputSanitizer(params string[] roots)
        : this(roots.Select(root => new VirtualPathRoot(root, null)).ToArray())
    {
    }

    public OutputSanitizer(params VirtualPathRoot[] roots)
        : this(TryResolveFinalPath, roots)
    {
    }

    public OutputSanitizer(Func<string, string?> resolveFinalPath, params VirtualPathRoot[] roots)
    {
        _resolveFinalPath = resolveFinalPath;
        _virtualRoots = roots
            .Where(root => !string.IsNullOrWhiteSpace(root.PhysicalPath))
            .Select(root =>
            {
                var physicalPath = NormalizePath(root.PhysicalPath);
                return new NormalizedVirtualPathRoot(
                    physicalPath,
                    NormalizeVirtualPath(root.VirtualPath),
                    TryResolvePath(resolveFinalPath, physicalPath));
            })
            .Where(root => !string.IsNullOrWhiteSpace(root.PhysicalPath))
            .DistinctBy(root => root.PhysicalPath, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(root => root.PhysicalPath.Length)
            .ToArray();
    }

    public string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized = value;
        sanitized = FileUriRegex().Replace(sanitized, match => ToDisplayPath(match.Value));
        sanitized = WindowsStackTracePathRegex().Replace(sanitized, match => $" in {ToDisplayPath(match.Groups["path"].Value)}:line ");
        sanitized = WindowsDrivePathRegex().Replace(sanitized, match => ToDisplayPath(match.Value));
        sanitized = UncPathRegex().Replace(sanitized, match => ToDisplayPath(match.Value));
        sanitized = PosixPathRegex().Replace(sanitized, match => ToDisplayPath(match.Value));
        sanitized = HomePathRegex().Replace(sanitized, match => ToDisplayPath(match.Value));

        return sanitized;
    }

    public string ToDisplayPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = NormalizePath(path);
        var segments = GetPathSegments(normalized);
        if (segments.Length == 0)
            return UnknownPath;

        var virtualPath = TryToVirtualPath(normalized);
        if (!string.IsNullOrWhiteSpace(virtualPath))
            return virtualPath;

        return IsRootedPath(path) ? segments[^1] : string.Join('/', segments);
    }

    public string SanitizeException(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;

        return $"{exception.GetType().Name}: {Sanitize(message)}";
    }

    private static string NormalizePath(string value) =>
        Canonicalize(NormalizeSeparators(value.Trim()));

    private static string NormalizeSeparators(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
            value = uri.LocalPath;

        value = value.Replace('\\', '/');
        return value.StartsWith("~/", StringComparison.Ordinal) ? value[2..].Trim('/') : value.Trim('/');
    }

    private static string Canonicalize(string value)
    {
        var segments = new List<string>();
        foreach (var segment in value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment is "." or "~")
                continue;

            if (segment == "..")
            {
                if (segments.Count > 0 && segments[^1] != "..")
                    segments.RemoveAt(segments.Count - 1);
                else
                    segments.Add(segment);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static string[] GetPathSegments(string value) =>
        value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment =>
                !segment.EndsWith(':') &&
                segment is not "." and not "~" and not ".." &&
                !segment.Equals(".KAST_DATA", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private string? TryToVirtualPath(string normalizedPath)
    {
        foreach (var root in _virtualRoots)
        {
            if (!IsUnderRoot(normalizedPath, root.PhysicalPath))
                continue;

            if (!ResolvedTargetIsTrusted(normalizedPath))
                return LastSegmentOrUnknown(normalizedPath);

            if (normalizedPath.Equals(root.PhysicalPath, StringComparison.OrdinalIgnoreCase))
                return root.VirtualPath ?? LastSegmentOrUnknown(normalizedPath);

            var relativePath = BuildCleanPath(normalizedPath[(root.PhysicalPath.Length + 1)..]);
            return root.VirtualPath is null ? relativePath : $"{root.VirtualPath}/{relativePath}";
        }

        return null;
    }

    private bool ResolvedTargetIsTrusted(string normalizedPath)
    {
        var resolvedPath = TryResolvePath(_resolveFinalPath, normalizedPath);
        return resolvedPath is null || IsUnderAnyVirtualRoot(resolvedPath);
    }

    private bool IsUnderAnyVirtualRoot(string normalizedPath) =>
        _virtualRoots.Any(root =>
            IsUnderRoot(normalizedPath, root.PhysicalPath) ||
            root.ResolvedPhysicalPath is not null && IsUnderRoot(normalizedPath, root.ResolvedPhysicalPath));

    private static bool IsUnderRoot(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + '/', StringComparison.OrdinalIgnoreCase);

    private static string? TryResolvePath(Func<string, string?> resolveFinalPath, string normalizedPath)
    {
        try
        {
            var resolvedPath = resolveFinalPath(normalizedPath);
            return string.IsNullOrWhiteSpace(resolvedPath) ? null : NormalizePath(resolvedPath);
        }
        catch
        {
            return null;
        }
    }

    private static string LastSegmentOrUnknown(string value)
    {
        var segments = GetPathSegments(value);
        return segments.Length == 0 ? UnknownPath : segments[^1];
    }

    private static string BuildCleanPath(string value)
    {
        var segments = GetPathSegments(value);
        return segments.Length == 0 ? LastSegmentOrUnknown(value) : string.Join('/', segments);
    }

    private static string? NormalizeVirtualPath(string? virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
            return null;

        var normalized = BuildCleanPath(NormalizePath(virtualPath));
        return normalized == UnknownPath ? null : normalized;
    }

    private static bool IsRootedPath(string path)
    {
        var value = path.Trim();
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile ||
            Regex.IsMatch(value, @"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant) ||
            value.StartsWith("\\\\", StringComparison.Ordinal) ||
            value.StartsWith("/", StringComparison.Ordinal) ||
            value.StartsWith("~/", StringComparison.Ordinal);
    }

    private static string? TryResolveFinalPath(string normalizedPath)
    {
        try
        {
            return ResolvePathAndAncestorLinks(Path.GetFullPath(ToNativePath(normalizedPath)));
        }
        catch
        {
            return null;
        }
    }

    private static string ResolvePathAndAncestorLinks(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
            return fullPath;

        var current = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(current))
            current = root;

        foreach (var segment in Path.GetRelativePath(root, fullPath).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrWhiteSpace(segment) || segment == ".")
                continue;

            current = Path.Combine(current, segment);
            current = TryResolveSingleLink(current) ?? current;
        }

        return Path.GetFullPath(current);
    }

    private static string? TryResolveSingleLink(string path)
    {
        try
        {
            var target = Directory.Exists(path)
                ? new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)
                : File.Exists(path)
                    ? new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)
                    : null;

            if (target is null)
                return null;

            return Path.IsPathFullyQualified(target.FullName)
                ? target.FullName
                : Path.GetFullPath(target.FullName, Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory());
        }
        catch
        {
            return null;
        }
    }

    private static string ToNativePath(string normalizedPath) =>
        normalizedPath.Replace('/', Path.DirectorySeparatorChar);

    [GeneratedRegex(@"file:///[A-Za-z]:/[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileUriRegex();

    [GeneratedRegex(@"\sin\s(?<path>[A-Za-z]:[\\/][^\r\n:]+):line\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsStackTracePathRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])[A-Za-z]:[\\/][^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsDrivePathRegex();

    [GeneratedRegex(@"\\\\[^\\/\s""'<>]+[\\/][^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UncPathRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])/(?:[^/\s""'<>:]+/)+[^/\s""'<>:]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PosixPathRegex();

    [GeneratedRegex(@"~/(?:[^/\s""'<>:]+/)*[^/\s""'<>:]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HomePathRegex();

    private sealed record NormalizedVirtualPathRoot(string PhysicalPath, string? VirtualPath, string? ResolvedPhysicalPath);

    public sealed record VirtualPathRoot(string PhysicalPath, string? VirtualPath);
}
