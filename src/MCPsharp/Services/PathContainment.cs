namespace MCPsharp.Services;

/// <summary>
/// Directory containment checks for paths supplied by tool callers.
/// </summary>
/// <remarks>
/// A plain string prefix test is not a containment test: <c>/root2/file</c> starts with <c>/root</c>
/// and <c>/root/../etc/passwd</c> starts with <c>/root/</c>. These helpers normalise both paths with
/// <see cref="Path.GetFullPath(string)"/> (resolving <c>.</c> and <c>..</c> segments) and only accept
/// the root itself or a path continuing past the root with a directory separator. Comparison follows
/// the platform's default file system case sensitivity: case-insensitive on Windows and macOS,
/// case-sensitive elsewhere.
/// <para>
/// Symbolic links and junctions are resolved for every existing component of both the root and the
/// candidate before comparing, so a link inside the root pointing elsewhere does not count as contained.
/// Components that do not exist yet (for example a file about to be created) are appended unresolved to
/// the resolved deepest existing ancestor.
/// </para>
/// </remarks>
public static class PathContainment
{
    /// <summary>
    /// Upper bound on the links followed while resolving one path, guarding against link cycles.
    /// </summary>
    private const int MaxLinkResolutions = 40;

    private static readonly char[] DirectorySeparators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    /// <summary>
    /// String comparison matching the default file system case sensitivity of the current platform.
    /// </summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    /// Returns true when <paramref name="path"/> is <paramref name="rootPath"/> or lies beneath it.
    /// Relative paths are resolved against the current directory.
    /// </summary>
    public static bool IsWithin(string rootPath, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var root = Path.TrimEndingDirectorySeparator(ResolveLinks(Path.GetFullPath(rootPath)));
        var candidate = Path.TrimEndingDirectorySeparator(ResolveLinks(Path.GetFullPath(path)));

        if (string.Equals(candidate, root, Comparison))
        {
            return true;
        }

        // A file system root such as "/" or "C:\" keeps its trailing separator after trimming
        var rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, Comparison);
    }

    /// <summary>
    /// Resolve <paramref name="path"/> (absolute, or relative to <paramref name="rootPath"/>) to a full path
    /// and report whether it lies within <paramref name="rootPath"/>.
    /// </summary>
    public static bool TryResolveWithin(string rootPath, string path, out string fullPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        if (string.IsNullOrEmpty(path))
        {
            fullPath = string.Empty;
            return false;
        }

        fullPath = Path.GetFullPath(path, Path.GetFullPath(rootPath));
        return IsWithin(rootPath, fullPath);
    }

    /// <summary>
    /// Resolve symbolic links and junctions in the existing components of a full path.
    /// Components from the first missing one onwards are kept as given.
    /// </summary>
    /// <param name="fullPath">An absolute, normalised path (see <see cref="Path.GetFullPath(string)"/>).</param>
    /// <returns>The path with every existing link component replaced by its final target.</returns>
    public static string ResolveLinks(string fullPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(fullPath);
        var remainingLinks = MaxLinkResolutions;
        return ResolveLinks(fullPath, ref remainingLinks);
    }

    private static string ResolveLinks(string fullPath, ref int remainingLinks)
    {
        var fileSystemRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(fileSystemRoot))
        {
            return fullPath;
        }

        var segments = fullPath[fileSystemRoot.Length..].Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries);
        var current = fileSystemRoot;
        for (var i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);

            string? target;
            try
            {
                var info = new FileInfo(current);
                var attributes = info.Attributes;
                if ((int)attributes == -1)
                {
                    // Deepest existing ancestor reached: the rest cannot contain links
                    return AppendSegments(current, segments, i + 1);
                }

                if (!attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable component or link cycle: keep the remainder as given
                return AppendSegments(current, segments, i + 1);
            }

            if (target == null)
            {
                // A reparse point that is not a link (for example a cloud placeholder)
                continue;
            }

            if (--remainingLinks < 0)
            {
                return AppendSegments(current, segments, i + 1);
            }

            // The target's own ancestors may be links too, so resolve the rewritten path from the start
            return ResolveLinks(Path.GetFullPath(AppendSegments(target, segments, i + 1)), ref remainingLinks);
        }

        return current;
    }

    private static string AppendSegments(string basePath, string[] segments, int start) =>
        start >= segments.Length ? basePath : Path.Combine(basePath, Path.Combine(segments[start..]));
}
