using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.MSBuild;
using MCPsharp.Services.Caching;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Core Roslyn workspace for C# code analysis with caching support.
/// The workspace's <see cref="Solution"/> is the source of truth: every project in a loaded
/// solution is compiled and exposed through the solution-aware query API in
/// <c>RoslynWorkspace.Queries.cs</c>. Multi-targeted projects (one Roslyn project per TFM)
/// are collapsed to a single "primary" project per project file for enumeration purposes.
/// </summary>
public partial class RoslynWorkspace : IDisposable
{
    private const int MaxErrorSamples = 10;

    private Workspace? _workspace;
    private WorkspaceEventRegistration? _workspaceFailedRegistration;
    private ProjectId? _adhocProjectId;
    private CompilationState _state = CompilationState.Empty;
    private readonly SemanticModelCache _semanticModelCache;
    private readonly CompilationCache _compilationCache;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _disposeLock = new();
    private bool _disposed;
    private WorkspaceHealth _health = new();

    public RoslynWorkspace()
    {
        _semanticModelCache = new SemanticModelCache();
        _compilationCache = new CompilationCache();
    }

    /// <summary>
    /// Get the current workspace health status
    /// </summary>
    public WorkspaceHealth GetHealth() => _health;

    /// <summary>
    /// Full path passed to the last <see cref="InitializeAsync"/> that completed, or null when the
    /// workspace was not loaded from a project path (never initialised, or a test workspace).
    /// </summary>
    public string? LoadedProjectPath { get; private set; }

    /// <summary>
    /// True when the workspace is initialised and was loaded by <see cref="InitializeAsync"/> from
    /// <paramref name="projectPath"/> (compared as normalised full paths).
    /// </summary>
    public bool IsLoadedFrom(string projectPath) =>
        IsInitialized &&
        LoadedProjectPath != null &&
        !string.IsNullOrEmpty(projectPath) &&
        string.Equals(LoadedProjectPath, NormalizeProjectPath(projectPath), PathContainment.Comparison);

    private static string NormalizeProjectPath(string projectPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));

    /// <summary>
    /// Initialize workspace with a project directory, .csproj file, or .sln/.slnx file
    /// </summary>
    public async Task InitializeAsync(string projectPath)
    {
        LoadedProjectPath = null;
        await InitializeCoreAsync(projectPath);
        LoadedProjectPath = NormalizeProjectPath(projectPath);
    }

    private async Task InitializeCoreAsync(string projectPath)
    {
        _health = new WorkspaceHealth { IsInitialized = false };

        try
        {
            var (slnPath, csprojPath, workspaceRoot) = ResolveProjectPath(projectPath);

            // If we couldn't find a project or solution, fall back to adhoc workspace
            if (slnPath == null && csprojPath == null)
            {
                await InitializeAdhocWorkspaceAsync(workspaceRoot);
                return;
            }

            // Use MSBuildWorkspace to load the actual project/solution with all references
            var msbuildWorkspace = MSBuildWorkspace.Create();

            _workspaceFailedRegistration?.Dispose();
            _workspaceFailedRegistration = msbuildWorkspace.RegisterWorkspaceFailedHandler(e =>
            {
                Console.Error.WriteLine($"MSBuildWorkspace warning: {e.Diagnostic.Kind} - {e.Diagnostic.Message}");
            });

            if (slnPath != null)
            {
                var solution = await msbuildWorkspace.OpenSolutionAsync(slnPath);
                if (!solution.ProjectIds.Any())
                {
                    throw new InvalidOperationException("Solution contains no projects");
                }
            }
            else
            {
                await msbuildWorkspace.OpenProjectAsync(csprojPath!);
            }

            _workspace?.Dispose();
            _workspace = msbuildWorkspace;
            _adhocProjectId = null;
            _state = CompilationState.Empty;
            _semanticModelCache.InvalidateAll();

            await TrackWorkspaceFileHealthAsync(workspaceRoot);
            // Must run before the first compilation: load failures are only raised on first load
            _health.AnalyzerLoadFailures = CollectAnalyzerLoadFailures(msbuildWorkspace.CurrentSolution);
            await RefreshAllCompilationsAndHealthAsync();
            _health.IsInitialized = true;
        }
        catch (Exception ex)
        {
            // MSBuildWorkspace failed, fallback to ad-hoc workspace
            await Console.Error.WriteLineAsync($"MSBuildWorkspace initialization failed: {ex}");

            string adhocRoot = projectPath;
            if (File.Exists(projectPath))
            {
                adhocRoot = Path.GetDirectoryName(projectPath) ?? projectPath;
            }

            await InitializeAdhocWorkspaceAsync(adhocRoot);
        }
    }

    private static (string? SlnPath, string? CsprojPath, string WorkspaceRoot) ResolveProjectPath(string projectPath)
    {
        if (File.Exists(projectPath))
        {
            var root = Path.GetDirectoryName(projectPath) ?? projectPath;
            var extension = Path.GetExtension(projectPath).ToLowerInvariant();
            return extension switch
            {
                ".sln" or ".slnx" => (projectPath, null, root),
                ".csproj" => (null, projectPath, root),
                _ => throw new ArgumentException($"Unsupported file type: {extension}. Expected .sln, .slnx or .csproj")
            };
        }

        if (!Directory.Exists(projectPath))
        {
            throw new ArgumentException($"Path does not exist: {projectPath}");
        }

        // Prefer a solution if available, then a project file
        var slnFile = Directory.GetFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(projectPath, "*.slnx", SearchOption.TopDirectoryOnly))
            .OrderBy(f => f, StringComparer.Ordinal)
            .FirstOrDefault();
        if (slnFile != null)
        {
            return (slnFile, null, projectPath);
        }

        var csprojFile = Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal)
            .FirstOrDefault();
        return (null, csprojFile, projectPath);
    }

    /// <summary>
    /// Initialize an ad-hoc workspace (fallback when MSBuildWorkspace fails or no .csproj found)
    /// </summary>
    private async Task InitializeAdhocWorkspaceAsync(string projectPath)
    {
        CreateAdhocWorkspace("Project", new[]
        {
            typeof(object).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            typeof(Task).Assembly.Location,
            typeof(System.Net.Http.HttpClient).Assembly.Location,
            typeof(System.Collections.Generic.List<string>).Assembly.Location
        });

        _health = new WorkspaceHealth { IsInitialized = false };

        var allFiles = EnumerateSourceFiles(projectPath);
        _health.TotalFiles = allFiles.Count;

        // Add all .cs files and track health
        int parseableCount = 0;
        foreach (var file in allFiles)
        {
            var fileHealth = await AddDocumentAndTrackHealthAsync(file);
            _health.FileStatus[file] = fileHealth;
            if (fileHealth.IsParseable)
            {
                parseableCount++;
            }
        }

        _health.ParseableFiles = parseableCount;

        await RefreshAllCompilationsAndHealthAsync();
        _health.IsInitialized = true;
    }

    private void CreateAdhocWorkspace(string projectName, IEnumerable<string> referencePaths)
    {
        LoadedProjectPath = null;
        var adhocWorkspace = new AdhocWorkspace();
        _workspace?.Dispose();
        _workspace = adhocWorkspace;
        _state = CompilationState.Empty;
        _semanticModelCache.InvalidateAll();

        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            projectName,
            projectName,
            LanguageNames.CSharp);

        var project = adhocWorkspace.AddProject(projectInfo);
        _adhocProjectId = project.Id;

        foreach (var reference in referencePaths.Where(File.Exists).Distinct(StringComparer.Ordinal))
        {
            project = project.AddMetadataReference(MetadataReference.CreateFromFile(reference));
        }

        adhocWorkspace.TryApplyChanges(project.Solution);
    }

    private static List<string> EnumerateSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains("/obj/") && !file.Contains("/bin/") &&
                           !file.Contains("\\obj\\") && !file.Contains("\\bin\\"))
            .ToList();

    private async Task TrackWorkspaceFileHealthAsync(string workspaceRoot)
    {
        var allFiles = EnumerateSourceFiles(workspaceRoot);
        _health.TotalFiles = allFiles.Count;

        int parseableCount = 0;
        foreach (var file in allFiles)
        {
            var fileHealth = await TrackFileHealthAsync(file);
            _health.FileStatus[file] = fileHealth;
            if (fileHealth.IsParseable)
            {
                parseableCount++;
            }
        }

        _health.ParseableFiles = parseableCount;
    }

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

    /// <summary>
    /// Add or update a document in the workspace from file.
    /// Existing documents are updated in every project that contains them (all target frameworks);
    /// new documents are only added for adhoc workspaces, since MSBuildWorkspace documents come from the project files.
    /// </summary>
    public async Task AddDocumentAsync(string filePath)
    {
        if (_workspace == null)
        {
            throw new InvalidOperationException("Workspace not initialized");
        }

        var text = await File.ReadAllTextAsync(filePath);
        var sourceText = SourceText.From(text);

        var existingIds = FindDocumentIds(_workspace.CurrentSolution, filePath);
        if (existingIds.Count > 0)
        {
            await ApplyDocumentTextAsync(existingIds, sourceText, CancellationToken.None);
        }
        else if (_adhocProjectId != null)
        {
            AddAdhocDocument(filePath, sourceText);
            await RefreshCompilationsAsync(new[] { _adhocProjectId });
        }
    }

    private DocumentId AddAdhocDocument(string filePath, SourceText sourceText)
    {
        var adhocWorkspace = (AdhocWorkspace)_workspace!;
        var documentInfo = DocumentInfo.Create(
            DocumentId.CreateNewId(_adhocProjectId!),
            Path.GetFileName(filePath),
            loader: TextLoader.From(TextAndVersion.Create(sourceText, VersionStamp.Default)),
            filePath: filePath);

        return adhocWorkspace.AddDocument(documentInfo).Id;
    }

    /// <summary>
    /// Add a document and track its health status (parseability, errors)
    /// </summary>
    private async Task<FileHealth> AddDocumentAndTrackHealthAsync(string filePath)
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
            var sourceText = SourceText.From(text);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: filePath);

            var syntaxDiagnostics = syntaxTree.GetDiagnostics();
            fileHealth.SyntaxErrors = syntaxDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            fileHealth.IsParseable = fileHealth.SyntaxErrors == 0;

            // Add to workspace even if there are syntax errors (best effort); compilation is refreshed by the caller
            AddAdhocDocument(filePath, sourceText);
        }
        catch (Exception ex)
        {
            fileHealth.IsParseable = false;
            fileHealth.ParseError = ex.Message;
            await ReportFileAnalysisFailureAsync(filePath, ex);
        }

        return fileHealth;
    }

    /// <summary>
    /// Add a document to the workspace with in-memory content (adhoc workspaces only)
    /// </summary>
    public async Task<DocumentId> AddInMemoryDocumentAsync(string filePath, string content)
    {
        if (_workspace == null)
        {
            throw new InvalidOperationException("Workspace not initialized");
        }

        if (_adhocProjectId == null)
        {
            throw new InvalidOperationException("AddInMemoryDocumentAsync is only supported with adhoc workspace");
        }

        var docId = AddAdhocDocument(filePath, SourceText.From(content));
        await RefreshCompilationsAsync(new[] { _adhocProjectId });
        return docId;
    }

    /// <summary>
    /// Initialize workspace for testing with in-memory documents
    /// </summary>
    public async Task InitializeTestWorkspaceAsync()
    {
        CreateAdhocWorkspace("TestProject", new[]
        {
            typeof(object).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            typeof(Task).Assembly.Location
        });

        await RefreshCompilationsAsync(null);
    }

    /// <summary>
    /// Update document content incrementally. The text is applied to every project containing
    /// the file (all target frameworks of a multi-targeted project, or linked files).
    /// </summary>
    public async Task<bool> UpdateDocumentAsync(string filePath, string newContent, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_workspace == null)
        {
            return false;
        }

        var docIds = FindDocumentIds(_workspace.CurrentSolution, filePath);
        if (docIds.Count == 0)
        {
            return false;
        }

        return await ApplyDocumentTextAsync(docIds, SourceText.From(newContent), ct);
    }

    private async Task<bool> ApplyDocumentTextAsync(IReadOnlyList<DocumentId> docIds, SourceText text, CancellationToken ct)
    {
        var newSolution = _workspace!.CurrentSolution;
        foreach (var docId in docIds)
        {
            newSolution = newSolution.WithDocumentText(docId, text);
        }

        if (!_workspace.TryApplyChanges(newSolution))
        {
            return false;
        }

        foreach (var docId in docIds)
        {
            _semanticModelCache.Invalidate(docId);
        }

        await RefreshCompilationsAsync(docIds.Select(id => id.ProjectId).Distinct(), ct);
        return true;
    }

    /// <summary>
    /// Re-synchronise cached compilations after the solution was changed directly through
    /// <see cref="Workspace.TryApplyChanges(Solution)"/> (e.g. by a rename).
    /// </summary>
    public async Task RefreshAfterSolutionChangeAsync(IEnumerable<ProjectId> changedProjects, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        _semanticModelCache.InvalidateAll();
        await RefreshCompilationsAsync(changedProjects, ct);
    }

    /// <summary>
    /// Check if workspace is initialized
    /// </summary>
    public bool IsInitialized =>
        _workspace != null && _state.Compilations.Count > 0;

    /// <summary>
    /// Get the current solution
    /// </summary>
    public Solution Solution
    {
        get
        {
            if (_workspace == null)
            {
                throw new InvalidOperationException("Workspace not initialized");
            }
            return _workspace.CurrentSolution;
        }
    }

    /// <summary>
    /// Get semantic model for a document (cached)
    /// </summary>
    public async Task<SemanticModel?> GetSemanticModelAsync(
        Document document,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return await _semanticModelCache.GetOrCreateAsync(document, ct);
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public (CacheStatistics semanticStats, CacheStatistics compilationStats) GetCacheStatistics()
    {
        ThrowIfDisposed();
        return (_semanticModelCache.GetStatistics(), _compilationCache.GetStatistics());
    }

    /// <summary>
    /// Clean up cache entries
    /// </summary>
    public int CleanupCaches()
    {
        ThrowIfDisposed();
        var semanticCleanup = _semanticModelCache.Cleanup();
        var compilationCleanup = _compilationCache.Cleanup();
        return semanticCleanup + compilationCleanup;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        lock (_disposeLock)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _semanticModelCache?.Dispose();
                    _compilationCache?.Dispose();
                    _workspaceFailedRegistration?.Dispose();
                    _workspace?.Dispose();
                    _refreshLock.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
