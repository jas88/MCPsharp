using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Services;

public sealed partial class BulkEditService
{
    private Task<IReadOnlyList<string>> ResolveFilePatterns(
        IReadOnlyList<string> patterns,
        BulkEditOptions options,
        CancellationToken cancellationToken)
    {
        var allFiles = new HashSet<string>();

        foreach (var pattern in patterns)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(pattern))
            {
                allFiles.Add(Path.GetFullPath(pattern));
                continue;
            }

            if (Directory.Exists(pattern))
            {
                var files = Directory.GetFiles(pattern, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (options.IncludeHiddenFiles || !IsHiddenFile(file))
                    {
                        allFiles.Add(Path.GetFullPath(file));
                    }
                }
                continue;
            }

            // Use glob pattern matching
            try
            {
                // Rooted patterns are matched relative to the deepest existing directory they start from
                var rootDir = DetermineRootDirectory(pattern);
                var matcher = new Matcher();
                matcher.AddInclude(Path.IsPathRooted(pattern) ? Path.GetRelativePath(rootDir, Path.GetFullPath(pattern)) : pattern);

                if (options.ExcludedFiles != null)
                {
                    foreach (var excludePattern in options.ExcludedFiles)
                    {
                        matcher.AddExclude(excludePattern);
                    }
                }

                var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(rootDir)));

                foreach (var file in result.Files)
                {
                    var fullPath = Path.Combine(rootDir, file.Path);
                    if (File.Exists(fullPath) &&
                        (options.IncludeHiddenFiles || !IsHiddenFile(fullPath)))
                    {
                        allFiles.Add(Path.GetFullPath(fullPath));
                    }
                }
            }
            catch (Exception ex)
            {
                LogResolvePatternFailed(BulkEditLog, ex, pattern);
            }
        }

        // Filter by file size if specified
        if (options.MaxFileSize > 0)
        {
            var filteredFiles = new List<string>();
            foreach (var file in allFiles)
            {
                try
                {
                    var fileInfo = new System.IO.FileInfo(file);
                    if (fileInfo.Length <= options.MaxFileSize)
                    {
                        filteredFiles.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    LogCheckFileSizeFailed(BulkEditLog, ex, file);
                }
            }
            return Task.FromResult<IReadOnlyList<string>>(filteredFiles);
        }

        return Task.FromResult<IReadOnlyList<string>>(allFiles.ToList());
    }

    private static string DetermineRootDirectory(string pattern)
    {
        if (Path.IsPathRooted(pattern))
        {
            var rootedPath = Path.GetFullPath(pattern);
            if (File.Exists(rootedPath) || Directory.Exists(rootedPath))
            {
                return rootedPath;
            }

            // For rooted patterns with wildcards, find the deepest existing directory
            var directory = Path.GetDirectoryName(rootedPath);
            while (directory != null && !Directory.Exists(directory))
            {
                directory = Path.GetDirectoryName(directory);
            }
            return directory ?? Directory.GetCurrentDirectory();
        }

        return Directory.GetCurrentDirectory();
    }

    private static bool IsHiddenFile(string filePath)
    {
        try
        {
            var fileInfo = new System.IO.FileInfo(filePath);
            if (OperatingSystem.IsWindows())
            {
                return (fileInfo.Attributes & FileAttributes.Hidden) != 0;
            }

            // On Unix-like systems, files starting with . are considered hidden
            return Path.GetFileName(filePath).StartsWith('.');
        }
        catch (IOException)
        {
            // Attributes unavailable (file removed or locked): treat as not hidden
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
