using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Refreshing compilations and tracking compilation/file health.
/// </summary>
public partial class RoslynWorkspace
{
    /// <summary>
    /// Compile every project in the solution and recompute project/diagnostic health.
    /// Diagnostics are counted once per primary project so multi-targeted projects are not double counted.
    /// </summary>
    private async Task RefreshAllCompilationsAndHealthAsync(CancellationToken ct = default)
    {
        await RefreshCompilationsAsync(null, ct);

        var state = _state;
        _health.TotalProjects = state.PrimaryProjectIds.Count;
        _health.LoadedProjects = state.PrimaryProjectIds.Count(state.Compilations.ContainsKey);
        _health.ErrorCount = 0;
        _health.WarningCount = 0;
        var errorSamples = new List<string>();

        foreach (var compilation in GetCompilations())
        {
            var diagnostics = compilation.GetDiagnostics(ct);
            // Warnings promoted by TreatWarningsAsErrors don't break compilation, and diagnostic suppressors
            // (which a real build runs but GetDiagnostics does not) may silence them, so count them as warnings
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && !d.IsWarningAsError).ToList();
            _health.ErrorCount += errors.Count;
            _health.WarningCount += diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning || d.IsWarningAsError);
            // Generator failures are warnings but explain the errors that follow, so list them first,
            // then one error per distinct ID, so a flood of one error doesn't hide the others
            errorSamples.AddRange(diagnostics
                .Where(d => d.Id is "CS8784" or "CS8785")
                .Concat(errors.DistinctBy(d => d.Id, StringComparer.Ordinal))
                .Select(d => $"{compilation.AssemblyName}: {d.Id} {d.GetMessage(CultureInfo.InvariantCulture)} at {d.Location.GetLineSpan()}")
                .Distinct(StringComparer.Ordinal)
                .Take(MaxErrorSamples - errorSamples.Count));
        }

        _health.ErrorSamples = errorSamples;
    }

    /// <summary>
    /// Load every analyzer and source generator referenced by the solution, returning a description
    /// of each one that fails (e.g. because it was built against a newer Roslyn than this process uses).
    /// </summary>
    private static List<string> CollectAnalyzerLoadFailures(Solution solution)
    {
        var failures = new List<string>();
        var references = solution.Projects
            .SelectMany(p => p.AnalyzerReferences)
            .OfType<AnalyzerFileReference>()
            .DistinctBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase);

        foreach (var reference in references)
        {
            void OnLoadFailed(object? sender, AnalyzerLoadFailureEventArgs e) =>
                failures.Add($"{reference.FullPath}: {e.ErrorCode} {e.Message}");

            reference.AnalyzerLoadFailed += OnLoadFailed;
            try
            {
                _ = reference.GetAnalyzers(LanguageNames.CSharp);
                _ = reference.GetGenerators(LanguageNames.CSharp);
            }
            finally
            {
                reference.AnalyzerLoadFailed -= OnLoadFailed;
            }
        }

        return failures;
    }

    /// <summary>
    /// Recompute compilations for the given projects and every project that transitively depends on them.
    /// Passing null refreshes every project in the solution.
    /// </summary>
    private async Task RefreshCompilationsAsync(IEnumerable<ProjectId>? changedProjects, CancellationToken ct = default)
    {
        if (_workspace == null)
        {
            return;
        }

        await _refreshLock.WaitAsync(ct);
        try
        {
            var solution = _workspace.CurrentSolution;
            var previous = _state;

            HashSet<ProjectId> toRefresh;
            if (changedProjects == null)
            {
                toRefresh = solution.ProjectIds.ToHashSet();
            }
            else
            {
                var graph = solution.GetProjectDependencyGraph();
                toRefresh = new HashSet<ProjectId>();
                foreach (var id in changedProjects)
                {
                    toRefresh.Add(id);
                    toRefresh.UnionWith(graph.GetProjectsThatTransitivelyDependOnThisProject(id));
                }
            }

            var compilations = new Dictionary<ProjectId, Compilation>();
            foreach (var project in solution.Projects)
            {
                if (!toRefresh.Contains(project.Id) &&
                    previous.Compilations.TryGetValue(project.Id, out var existing))
                {
                    compilations[project.Id] = existing;
                    continue;
                }

                try
                {
                    var compilation = await _compilationCache.GetOrCreateAsync(project, ct);
                    if (compilation != null)
                    {
                        compilations[project.Id] = compilation;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await Console.Error.WriteLineAsync($"Compilation failed for project {project.Name}: {ex}");
                }
            }

            _state = CompilationState.Create(solution, compilations);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Track file health without adding to workspace (for MSBuildWorkspace which already has documents)
    /// </summary>
    private static async Task<FileHealth> TrackFileHealthAsync(string filePath)
    {
        var fileHealth = new FileHealth
        {
            FilePath = filePath,
            LastAnalyzed = DateTimeOffset.UtcNow,
            IsParseable = false,
            SyntaxErrors = 0,
            SemanticErrors = 0
        };

        try
        {
            var text = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(text, path: filePath);

            var syntaxDiagnostics = syntaxTree.GetDiagnostics();
            fileHealth.SyntaxErrors = syntaxDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            fileHealth.IsParseable = fileHealth.SyntaxErrors == 0;
        }
        catch (Exception ex)
        {
            fileHealth.IsParseable = false;
            fileHealth.ParseError = ex.Message;
            await ReportFileAnalysisFailureAsync(filePath, ex);
        }

        return fileHealth;
    }

    private static Task ReportFileAnalysisFailureAsync(string filePath, Exception exception)
    {
        return Console.Error.WriteLineAsync($"Failed to analyze file {filePath}: {exception}");
    }
}
