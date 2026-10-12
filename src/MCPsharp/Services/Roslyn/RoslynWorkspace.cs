using MCPsharp.Services.Caching;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

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
