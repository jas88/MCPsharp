using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Running analyzers: as in an IDE, each file gets the analyzers of the projects that compile it
/// </summary>
public partial class RoslynAnalyzerService
{
    /// <summary>
    /// The most files of a directory target that are analyzed, in path order; the rest are counted in
    /// <see cref="AnalyzerRunResult.FilesSkipped"/>
    /// </summary>
    public const int MaxDirectoryFiles = 100;

    private const string NoAnalyzersMessage =
        "No analyzers loaded or specified. The analyzers the open project references are loaded automatically; " +
        "load others with load_roslyn_analyzers.";

    private const string NoApplicableAnalyzersMessage =
        "None of the loaded or specified analyzers apply to the target. A project's analyzers run only on the files " +
        "of the projects that reference them, and a file in no project gets only the analyzers loaded with " +
        "load_roslyn_analyzers or from customAnalyzerPaths.";

    // Hidden directories (.git, .vs) hold no project sources, and an unreadable directory is no reason to stop
    private static readonly EnumerationOptions SourceFileEnumeration = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    private static readonly char[] DirectorySeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public async Task<AnalyzerRunResult> RunAnalyzersAsync(
        string targetPath,
        IEnumerable<string>? analyzerIds = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            LogRunningAnalyzersOn(_logger, targetPath);

            // Auto-load analyzers if not already loaded
            await EnsureAnalyzersAutoLoadedAsync(cancellationToken);

            var requested = SelectRequestedAnalyzers(analyzerIds);
            if (requested.IsEmpty)
            {
                return Failed(startTime, NoAnalyzersMessage);
            }

            var (files, filesSkipped) = GetTargetFiles(targetPath);
            if (files.Count == 0)
            {
                return Failed(startTime, $"No C# files to analyze in {targetPath}");
            }

            LogRunningAnalyzerCount(_logger, requested.Length, files.Count);

            var run = new RunState();
            foreach (var file in files)
            {
                await RunAnalyzersOnFileAsync(file, requested, run, cancellationToken);
            }

            if (run.AnalyzersRun.Count == 0)
            {
                return Failed(startTime, NoApplicableAnalyzersMessage);
            }

            var totalIssues = run.Results.Sum(r => r.Issues.Length);
            return new AnalyzerRunResult
            {
                Success = true,
                AnalyzersRun = run.AnalyzersRun.ToImmutableArray(),
                TotalIssuesFound = totalIssues,
                FilesAnalyzed = run.FilesAnalyzed,
                FilesSkipped = filesSkipped,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Results = run.Results.ToImmutableArray(),
                Statistics = new Dictionary<string, object>
                {
                    ["AnalyzersRun"] = run.AnalyzersRun.Count,
                    ["TotalIssuesFound"] = totalIssues,
                    ["FilesAnalyzed"] = run.FilesAnalyzed,
                    ["FilesSkipped"] = filesSkipped,
                    ["Duration"] = DateTime.UtcNow - startTime
                }.ToImmutableDictionary()
            };
        }
        catch (OperationCanceledException)
        {
            return Failed(startTime, "Analysis cancelled");
        }
        catch (Exception ex)
        {
            LogErrorRunningAnalyzers(_logger, ex, targetPath);
            return Failed(startTime, ex.Message);
        }
    }

    /// <inheritdoc />
    public ImmutableArray<IAnalyzer> GetAnalyzersForFile(string filePath) =>
        SelectAnalyzersForFile(GetLoadedAnalyzers(), GetFileAnalyzerScope(filePath)).ToImmutableArray();

    /// <summary>
    /// Run the requested analyzers that apply to <paramref name="file"/> on it
    /// </summary>
    private async Task RunAnalyzersOnFileAsync(
        string file,
        ImmutableArray<IAnalyzer> requested,
        RunState run,
        CancellationToken cancellationToken)
    {
        var scope = GetFileAnalyzerScope(file);
        var fileAnalyzers = SelectAnalyzersForFile(requested, scope).ToList();
        if (fileAnalyzers.Count == 0)
        {
            return;
        }

        run.FilesAnalyzed++;

        // Projects that compile the same file (a linked file, or one project per target framework) can reference
        // different builds of an analyzer, which report the same diagnostic: report it once
        var reported = scope?.ProjectCount > 1 ? new HashSet<IssueIdentity>() : null;
        foreach (var analyzerId in fileAnalyzers.Select(analyzer => analyzer.Id))
        {
            var result = await RunAnalyzerOnFileAsync(analyzerId, file, reported, cancellationToken);
            if (result != null)
            {
                run.Results.Add(result);
            }

            if (run.AnalyzerIds.Add(analyzerId))
            {
                run.AnalyzersRun.Add(analyzerId);
            }
        }
    }

    /// <summary>
    /// Run one analyzer on one file through the analyzer host; null when running it failed unexpectedly
    /// </summary>
    private async Task<AnalysisResult?> RunAnalyzerOnFileAsync(
        string analyzerId,
        string file,
        HashSet<IssueIdentity>? reported,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startTime = DateTime.UtcNow;

            var result = await _analyzerHost.RunAnalyzerAsync(analyzerId, file, options: null, cancellationToken);
            if (!result.Success || result.Findings == null)
            {
                return new AnalysisResult
                {
                    FilePath = file,
                    AnalyzerId = analyzerId,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    Success = false,
                    ErrorMessage = result.ErrorMessage
                };
            }

            var issues = result.Findings
                .Select(finding => ToIssue(finding, analyzerId, file))
                .Where(issue => reported == null || reported.Add(new IssueIdentity(issue)))
                .ToImmutableArray();
            LogAnalyzerFoundIssues(_logger, analyzerId, issues.Length);

            return new AnalysisResult
            {
                FilePath = file,
                AnalyzerId = analyzerId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = true,
                Issues = issues
            };
        }
        catch (OperationCanceledException)
        {
            LogAnalyzerRunCancelled(_logger);
            throw;
        }
        catch (Exception ex)
        {
            LogErrorRunningAnalyzer(_logger, ex, analyzerId);
            return null;
        }
    }

    private static AnalyzerIssue ToIssue(Finding finding, string analyzerId, string file) => new()
    {
        RuleId = finding.RuleId ?? string.Empty,
        AnalyzerId = analyzerId,
        Severity = finding.Severity switch
        {
            FindingSeverity.Info => IssueSeverity.Info,
            FindingSeverity.Warning => IssueSeverity.Warning,
            FindingSeverity.Error => IssueSeverity.Error,
            FindingSeverity.Critical => IssueSeverity.Error,
            _ => IssueSeverity.Info
        },
        Title = finding.Message,
        Description = finding.Message,
        FilePath = finding.FilePath ?? file,
        LineNumber = finding.LineNumber ?? 0,
        ColumnNumber = finding.ColumnNumber ?? 0
    };

    /// <summary>
    /// The loaded analyzers with the requested IDs, or every loaded analyzer when none are requested
    /// </summary>
    private ImmutableArray<IAnalyzer> SelectRequestedAnalyzers(IEnumerable<string>? analyzerIds)
    {
        lock (_lock)
        {
            var ids = analyzerIds?.ToList();
            if (ids is not { Count: > 0 })
            {
                return _loadedAnalyzers.Values.ToImmutableArray();
            }

            var selected = new List<IAnalyzer>();
            foreach (var id in ids.Distinct(StringComparer.Ordinal))
            {
                if (_loadedAnalyzers.TryGetValue(id, out var analyzer))
                {
                    selected.Add(analyzer);
                }
            }

            return selected.ToImmutableArray();
        }
    }

    /// <summary>
    /// The analyzers among <paramref name="analyzers"/> that apply to a file: those of the projects that compile
    /// it, and those that belong to no project. Without auto-load there are no project analyzers (and no scope),
    /// so every analyzer applies.
    /// </summary>
    private static IEnumerable<IAnalyzer> SelectAnalyzersForFile(IEnumerable<IAnalyzer> analyzers, FileAnalyzerScope? scope) =>
        scope == null
            ? analyzers
            : analyzers.Where(analyzer =>
                analyzer is not RoslynAnalyzerAdapter { AnalyzerReferences.Count: > 0 } adapter ||
                adapter.AnalyzerReferences.Any(scope.Includes));

    private FileAnalyzerScope? GetFileAnalyzerScope(string file) => _autoLoadService?.GetFileAnalyzerScope(file);

    /// <summary>
    /// The files to analyze: the target file, or the C# files under a target directory outside build output
    /// (bin, obj), in path order and at most <see cref="MaxDirectoryFiles"/> of them, with the number left out
    /// </summary>
    private static (List<string> Files, int Skipped) GetTargetFiles(string targetPath)
    {
        if (File.Exists(targetPath))
        {
            return ([targetPath], 0);
        }

        if (!Directory.Exists(targetPath))
        {
            return ([], 0);
        }

        var files = Directory.EnumerateFiles(targetPath, "*.cs", SourceFileEnumeration)
            .Where(file => !IsInBuildOutput(Path.GetRelativePath(targetPath, file)))
            .Order(StringComparer.Ordinal)
            .ToList();
        return files.Count <= MaxDirectoryFiles
            ? (files, 0)
            : (files.GetRange(0, MaxDirectoryFiles), files.Count - MaxDirectoryFiles);
    }

    private static bool IsInBuildOutput(string relativePath)
    {
        var segments = relativePath.Split(DirectorySeparators);
        return segments.Take(segments.Length - 1).Any(directory =>
            directory.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            directory.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static AnalyzerRunResult Failed(DateTime startTime, string message) => new()
    {
        Success = false,
        ErrorMessage = message,
        StartTime = startTime,
        EndTime = DateTime.UtcNow
    };

    /// <summary>
    /// What a run has produced so far
    /// </summary>
    private sealed class RunState
    {
        public List<AnalysisResult> Results { get; } = new();

        public List<string> AnalyzersRun { get; } = new();

        public HashSet<string> AnalyzerIds { get; } = new(StringComparer.Ordinal);

        public int FilesAnalyzed { get; set; }
    }

    /// <summary>
    /// An issue's rule, location and message: two issues with the same identity are the same diagnostic
    /// </summary>
    private readonly record struct IssueIdentity(string RuleId, string FilePath, int Line, int Column, string Message)
    {
        public IssueIdentity(AnalyzerIssue issue)
            : this(issue.RuleId, issue.FilePath, issue.LineNumber, issue.ColumnNumber, issue.Description)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Running analyzers on {TargetPath}")]
    private static partial void LogRunningAnalyzersOn(ILogger logger, string targetPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running up to {Count} analyzers on {FileCount} files")]
    private static partial void LogRunningAnalyzerCount(ILogger logger, int count, int fileCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Analyzer {AnalyzerId} found {Count} issues")]
    private static partial void LogAnalyzerFoundIssues(ILogger logger, string analyzerId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analyzer run cancelled")]
    private static partial void LogAnalyzerRunCancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error running analyzer {AnalyzerId}")]
    private static partial void LogErrorRunningAnalyzer(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error running analyzers on {TargetPath}")]
    private static partial void LogErrorRunningAnalyzers(ILogger logger, Exception exception, string targetPath);
}

/// <summary>
/// Result of running analyzers
/// </summary>
public class AnalyzerRunResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ImmutableArray<string> AnalyzersRun { get; init; } = ImmutableArray<string>.Empty;
    public int TotalIssuesFound { get; init; }

    /// <summary>
    /// The files at least one analyzer ran on
    /// </summary>
    public int FilesAnalyzed { get; init; }

    /// <summary>
    /// The files of a directory target beyond <see cref="RoslynAnalyzerService.MaxDirectoryFiles"/>, which were not
    /// analyzed
    /// </summary>
    public int FilesSkipped { get; init; }

    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public ImmutableDictionary<string, object>? Statistics { get; init; }
    public ImmutableArray<AnalysisResult> Results { get; init; } = ImmutableArray<AnalysisResult>.Empty;
}
