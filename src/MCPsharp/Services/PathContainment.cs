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
/// case-sensitive elsewhere. Symbolic links are not resolved.
/// </remarks>
public static class PathContainment
{
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

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

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
}
