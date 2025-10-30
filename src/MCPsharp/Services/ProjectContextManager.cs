namespace MCPsharp.Services;

using MCPsharp.Models;

/// <summary>
/// Manages the project context including root path tracking and path validation
/// </summary>
public class ProjectContextManager
{
    private ProjectContext? _currentContext;

    /// <summary>
    /// Open a project by setting its root path and scanning for files
    /// </summary>
    /// <param name="path">Path to the project directory</param>
    /// <exception cref="DirectoryNotFoundException">Thrown when the directory does not exist</exception>
    /// <exception cref="ArgumentException">Thrown when path is a file, not a directory</exception>
    public void OpenProject(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (!Directory.Exists(fullPath))
        {
            if (File.Exists(fullPath))
            {
                throw new ArgumentException($"Path is a file, not a directory: {fullPath}");
            }
            throw new DirectoryNotFoundException($"Directory does not exist: {fullPath}");
        }

        // Scan for files in the project
        var knownFiles = new HashSet<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                knownFiles.Add(Path.GetFullPath(file));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Continue with what we can access
        }

        _currentContext = new ProjectContext
        {
            RootPath = fullPath,
            OpenedAt = DateTime.UtcNow,
            FileCount = knownFiles.Count,
            KnownFiles = knownFiles
        };
    }

    /// <summary>
    /// Close the current project
    /// </summary>
    public void CloseProject()
    {
        _currentContext = null;
    }

    /// <summary>
    /// Get information about the current project
    /// </summary>
    /// <returns>Project information dictionary, or null if no project is open</returns>
    public Dictionary<string, object>? GetProjectInfo()
    {
        if (_currentContext == null)
        {
            return null;
        }

        return new Dictionary<string, object>
        {
            ["rootPath"] = _currentContext.RootPath ?? string.Empty,
            ["openedAt"] = _currentContext.OpenedAt?.ToString("O") ?? string.Empty,
            ["fileCount"] = _currentContext.FileCount
        };
    }

    /// <summary>
    /// Check if a path is valid (within the project root)
    /// </summary>
    /// <param name="path">Path to validate</param>
    /// <returns>True if path is within project root, false otherwise</returns>
    public bool IsValidPath(string path)
    {
        if (_currentContext?.RootPath == null)
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(_currentContext.RootPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get the current project context
    /// </summary>
    /// <returns>Current project context, or null if no project is open</returns>
    public ProjectContext? GetProjectContext()
    {
        return _currentContext;
    }
}
