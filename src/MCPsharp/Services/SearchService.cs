using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MCPsharp.Models.Search;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

/// <summary>
/// Service for searching text and regex patterns across project files
/// </summary>
public class SearchService : ISearchService
{
    private readonly ProjectContextManager _projectContext;
    private readonly ILogger<SearchService> _logger;

    // Default directories to skip
    private static readonly HashSet<string> DefaultExcludeDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".vscode",
        "packages", "TestResults", ".idea", "out", "dist", "build"
    };

    // File extensions typically considered binary
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".pdb", ".bin", ".obj", ".lib", ".so", ".dylib",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".pdf", ".zip",
        ".tar", ".gz", ".7z", ".rar", ".mp3", ".mp4", ".avi", ".mov"
    };

    // Cache for compiled regex patterns
    private readonly ConcurrentDictionary<string, Regex> _regexCache = new();

    public SearchService(ProjectContextManager projectContext, ILogger<SearchService>? logger = null)
    {
        _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SearchService>.Instance;
    }

    /// <inheritdoc/>
    public async Task<SearchResult> SearchTextAsync(SearchRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Validate request
            if (string.IsNullOrWhiteSpace(request.Pattern))
            {
                return CreateErrorResult("Pattern cannot be empty", sw.ElapsedMilliseconds);
            }

            // Get search root directory
            var searchRoot = GetSearchRoot(request.TargetPath);
            if (!Directory.Exists(searchRoot))
            {
                return CreateErrorResult($"Search path does not exist: {searchRoot}", sw.ElapsedMilliseconds);
            }

            // Get files to search
            var files = GetFilesToSearch(searchRoot, request.IncludePattern, request.ExcludePattern);
            if (files.Count == 0)
            {
                return new SearchResult
                {
                    Success = true,
                    TotalMatches = 0,
                    Returned = 0,
                    Offset = 0,
                    HasMore = false,
                    Matches = new List<SearchMatch>(),
                    FilesSearched = 0,
                    SearchDurationMs = sw.ElapsedMilliseconds
                };
            }

            // Compile or get cached regex
            Regex? regex = null;
            if (request.Regex)
            {
                try
                {
                    regex = GetOrCreateRegex(request.Pattern, request.CaseSensitive);
                }
                catch (ArgumentException ex)
                {
                    return CreateErrorResult($"Invalid regex pattern: {ex.Message}", sw.ElapsedMilliseconds);
                }
            }

            // Search files in parallel
            var allMatches = new ConcurrentBag<SearchMatch>();
            var filesSearched = 0;

            await Parallel.ForEachAsync(files, ct, async (file, token) =>
            {
                if (token.IsCancellationRequested) return;

                try
                {
                    var matches = await SearchFileAsync(file, request.Pattern, regex, request.CaseSensitive,
                                                       request.ContextLines, token);

                    foreach (var match in matches)
                    {
                        allMatches.Add(match);
                    }

                    Interlocked.Increment(ref filesSearched);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error searching file: {FilePath}", file);
                }
            });

            ct.ThrowIfCancellationRequested();

            // Sort matches by file path and line number
            var sortedMatches = allMatches
                .OrderBy(m => m.FilePath)
                .ThenBy(m => m.LineNumber)
                .ToList();

            // Apply pagination
            var totalMatches = sortedMatches.Count;
            var paginatedMatches = sortedMatches
                .Skip(request.Offset)
                .Take(request.MaxResults)
                .ToList();

            sw.Stop();

            return new SearchResult
            {
                Success = true,
                TotalMatches = totalMatches,
                Returned = paginatedMatches.Count,
                Offset = request.Offset,
                HasMore = request.Offset + paginatedMatches.Count < totalMatches,
                Matches = paginatedMatches,
                FilesSearched = filesSearched,
                SearchDurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during text search");
            return CreateErrorResult($"Search failed: {ex.Message}", sw.ElapsedMilliseconds);
        }
    }

    private string GetSearchRoot(string? targetPath)
    {
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            // Use absolute path or resolve relative to project root
            if (Path.IsPathRooted(targetPath))
            {
                return targetPath;
            }

            var projectRoot = _projectContext.GetProjectContext()?.RootPath;
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                return Path.Combine(projectRoot, targetPath);
            }
        }

        // Default to project root
        return _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
    }

    private List<string> GetFilesToSearch(string searchRoot, string? includePattern, string? excludePattern)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);

        // Add include patterns
        if (!string.IsNullOrWhiteSpace(includePattern))
        {
            matcher.AddInclude(includePattern);
        }
        else
        {
            matcher.AddInclude("**/*");
        }

        // Add default exclude patterns
        foreach (var dir in DefaultExcludeDirs)
        {
            matcher.AddExclude($"**/{dir}/**");
        }

        // Add custom exclude pattern
        if (!string.IsNullOrWhiteSpace(excludePattern))
        {
            matcher.AddExclude(excludePattern);
        }

        // Execute matching
        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(searchRoot)));

        var files = result.Files
            .Select(f => Path.Combine(searchRoot, f.Path))
            .Where(f => File.Exists(f) && !IsBinaryFile(f))
            .ToList();

        return files;
    }

    private bool IsBinaryFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (BinaryExtensions.Contains(extension))
        {
            return true;
        }

        // For files without extensions or unknown extensions, check first few bytes
        try
        {
            using var fs = File.OpenRead(filePath);
            var buffer = new byte[512];
            var bytesRead = fs.Read(buffer, 0, buffer.Length);

            // Check for null bytes (common in binary files)
            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            // If we can't read the file, treat it as binary to skip
            return true;
        }
    }

    private Regex GetOrCreateRegex(string pattern, bool caseSensitive)
    {
        var cacheKey = $"{pattern}|{caseSensitive}";

        return _regexCache.GetOrAdd(cacheKey, _ =>
        {
            var options = RegexOptions.Compiled;
            if (!caseSensitive)
            {
                options |= RegexOptions.IgnoreCase;
            }

            // Add timeout to prevent ReDoS attacks
            return new Regex(pattern, options, TimeSpan.FromSeconds(1));
        });
    }

    private async Task<List<SearchMatch>> SearchFileAsync(
        string filePath,
        string pattern,
        Regex? regex,
        bool caseSensitive,
        int contextLines,
        CancellationToken ct)
    {
        var matches = new List<SearchMatch>();

        try
        {
            // Read all lines
            var lines = await File.ReadAllLinesAsync(filePath, ct);

            for (int i = 0; i < lines.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                var line = lines[i];
                Match? regexMatch = null;
                int columnIndex = -1;
                string matchText = string.Empty;

                if (regex != null)
                {
                    // Regex search
                    regexMatch = regex.Match(line);
                    if (regexMatch.Success)
                    {
                        columnIndex = regexMatch.Index;
                        matchText = regexMatch.Value;
                    }
                }
                else
                {
                    // Literal text search
                    var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    columnIndex = line.IndexOf(pattern, comparison);

                    if (columnIndex >= 0)
                    {
                        matchText = line.Substring(columnIndex, pattern.Length);
                    }
                }

                if (columnIndex >= 0)
                {
                    // Extract context
                    var contextBefore = new List<string>();
                    var contextAfter = new List<string>();

                    int startContext = Math.Max(0, i - contextLines);
                    for (int j = startContext; j < i; j++)
                    {
                        contextBefore.Add(lines[j]);
                    }

                    int endContext = Math.Min(lines.Length - 1, i + contextLines);
                    for (int j = i + 1; j <= endContext; j++)
                    {
                        contextAfter.Add(lines[j]);
                    }

                    matches.Add(new SearchMatch
                    {
                        FilePath = filePath,
                        LineNumber = i + 1, // 1-based line numbers
                        ColumnNumber = columnIndex + 1, // 1-based column numbers
                        MatchText = matchText,
                        ContextBefore = contextBefore,
                        ContextAfter = contextAfter,
                        LineContent = line
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading file: {FilePath}", filePath);
        }

        return matches;
    }

    private SearchResult CreateErrorResult(string errorMessage, long durationMs)
    {
        return new SearchResult
        {
            Success = false,
            TotalMatches = 0,
            Returned = 0,
            Offset = 0,
            HasMore = false,
            Matches = new List<SearchMatch>(),
            FilesSearched = 0,
            SearchDurationMs = durationMs,
            ErrorMessage = errorMessage
        };
    }
}
