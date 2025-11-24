using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.MSBuild;
using MCPsharp.Services.Caching;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Core Roslyn workspace for C# code analysis with caching support
/// </summary>
public class RoslynWorkspace : IDisposable
{
    private Workspace? _workspace;
    private ProjectId? _projectId;
    private Compilation? _compilation;
    private readonly Dictionary<string, DocumentId> _documentMap = new();
    private readonly SemanticModelCache _semanticModelCache;
    private readonly CompilationCache _compilationCache;
    private readonly object _disposeLock = new();
    private bool _disposed = false;
    private WorkspaceHealth _health = new();
    private bool _useAdhoc = false; // Track whether we're using adhoc workspace

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
    /// Initialize workspace with a project directory, .csproj file, or .sln file
    /// </summary>
    public async Task InitializeAsync(string projectPath)
    {
        // Initialize health tracking
        _health = new WorkspaceHealth
        {
            IsInitialized = false,
            TotalProjects = 1,
            LoadedProjects = 0
        };

        try
        {
            // Determine what we're dealing with: directory, .csproj, or .sln
            string? csprojPath = null;
            string? slnPath = null;
            string workspaceRoot;

            if (File.Exists(projectPath))
            {
                // It's a file - check if it's .sln or .csproj
                var extension = Path.GetExtension(projectPath).ToLowerInvariant();
                if (extension == ".sln")
                {
                    slnPath = projectPath;
                    workspaceRoot = Path.GetDirectoryName(projectPath) ?? projectPath;
                }
                else if (extension == ".csproj")
                {
                    csprojPath = projectPath;
                    workspaceRoot = Path.GetDirectoryName(projectPath) ?? projectPath;
                }
                else
                {
                    throw new ArgumentException($"Unsupported file type: {extension}. Expected .sln or .csproj");
                }
            }
            else if (Directory.Exists(projectPath))
            {
                // It's a directory - look for .sln or .csproj files
                workspaceRoot = projectPath;

                var slnFiles = Directory.GetFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly);
                if (slnFiles.Length > 0)
                {
                    slnPath = slnFiles[0]; // Prefer .sln if available
                }
                else
                {
                    var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly);
                    if (csprojFiles.Length > 0)
                    {
                        csprojPath = csprojFiles[0];
                    }
                }
            }
            else
            {
                throw new ArgumentException($"Path does not exist: {projectPath}");
            }

            // If we couldn't find a project or solution, fall back to adhoc workspace
            if (slnPath == null && csprojPath == null)
            {
                await InitializeAdhocWorkspaceAsync(workspaceRoot);
                return;
            }

            // Use MSBuildWorkspace to load the actual project/solution with all references
            var msbuildWorkspace = MSBuildWorkspace.Create();

            // Log workspace diagnostics
            msbuildWorkspace.WorkspaceFailed += (sender, e) =>
            {
                Console.Error.WriteLine($"MSBuildWorkspace warning: {e.Diagnostic.Kind} - {e.Diagnostic.Message}");
            };

            Project project;
            if (slnPath != null)
            {
                // Load solution - this loads all projects in the solution
                var solution = await msbuildWorkspace.OpenSolutionAsync(slnPath);

                // Use the first project for now (TODO: support multi-project solutions)
                project = solution.Projects.FirstOrDefault()
                    ?? throw new InvalidOperationException("Solution contains no projects");
            }
            else
            {
                // Load single project
                project = await msbuildWorkspace.OpenProjectAsync(csprojPath!);
            }

            _workspace = msbuildWorkspace;
            _projectId = project.Id;
            _useAdhoc = false;

            // Build document map
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath != null)
                {
                    _documentMap[doc.FilePath] = doc.Id;
                }
            }

            // Collect all .cs files for health tracking
            var allFiles = Directory.EnumerateFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains("/obj/") && !file.Contains("/bin/") &&
                              !file.Contains("\\obj\\") && !file.Contains("\\bin\\"))
                .ToList();

            _health.TotalFiles = allFiles.Count;

            // Track health for files in the project
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

            // Get compilation with caching and analyze errors
            try
            {
                _compilation = await _compilationCache.GetOrCreateAsync(project);

                if (_compilation != null)
                {
                    var diagnostics = _compilation.GetDiagnostics();
                    _health.ErrorCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                    _health.WarningCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
                }

                _health.LoadedProjects = 1;
            }
            catch (Exception ex)
            {
                // Compilation failed - log and track
                Console.Error.WriteLine($"Compilation failed: {ex.Message}");
                _health.LoadedProjects = 0;
            }

            _health.IsInitialized = true;
        }
        catch (Exception ex)
        {
            // MSBuildWorkspace failed, fallback to ad-hoc workspace
            Console.Error.WriteLine($"MSBuildWorkspace initialization failed: {ex.Message}");

            // Determine workspace root for adhoc fallback
            string adhocRoot = projectPath;
            if (File.Exists(projectPath))
            {
                adhocRoot = Path.GetDirectoryName(projectPath) ?? projectPath;
            }

            await InitializeAdhocWorkspaceAsync(adhocRoot);
        }
    }

    /// <summary>
    /// Initialize an ad-hoc workspace (fallback when MSBuildWorkspace fails or no .csproj found)
    /// </summary>
    private async Task InitializeAdhocWorkspaceAsync(string projectPath)
    {
        var adhocWorkspace = new AdhocWorkspace();
        _workspace = adhocWorkspace;
        _useAdhoc = true;

        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "Project",
            "Project",
            LanguageNames.CSharp
        );

        _projectId = adhocWorkspace.AddProject(projectInfo).Id;

        // Add common metadata references for test scenarios
        var project = adhocWorkspace.CurrentSolution.GetProject(_projectId);
        if (project != null)
        {
            // Add basic metadata references that are commonly needed
            var metadataReferences = new[]
            {
                typeof(object).Assembly.Location,
                typeof(Enumerable).Assembly.Location,
                typeof(Task).Assembly.Location,
                typeof(System.Net.Http.HttpClient).Assembly.Location,
                typeof(System.Collections.Generic.List<string>).Assembly.Location
            };

            foreach (var reference in metadataReferences)
            {
                if (File.Exists(reference))
                {
                    project = project.AddMetadataReference(MetadataReference.CreateFromFile(reference));
                }
            }

            adhocWorkspace.TryApplyChanges(project.Solution);
        }

        // Collect all .cs files
        var allFiles = Directory.EnumerateFiles(projectPath, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains("/obj/") && !file.Contains("/bin/") &&
                          !file.Contains("\\obj\\") && !file.Contains("\\bin\\"))
            .ToList();

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

        // Get compilation with caching and analyze errors
        project = adhocWorkspace.CurrentSolution.GetProject(_projectId);
        if (project != null)
        {
            try
            {
                _compilation = await _compilationCache.GetOrCreateAsync(project);

                if (_compilation != null)
                {
                    var diagnostics = _compilation.GetDiagnostics();
                    _health.ErrorCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                    _health.WarningCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
                }

                _health.LoadedProjects = 1;
            }
            catch
            {
                // Compilation failed - health already tracks this
                _health.LoadedProjects = 0;
            }
        }

        _health.IsInitialized = true;
    }

    /// <summary>
    /// Track file health without adding to workspace (for MSBuildWorkspace which already has documents)
    /// </summary>
    private async Task<FileHealth> TrackFileHealthAsync(string filePath)
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
            // Try to parse the file first (Level 1: Syntax-only)
            var text = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(text, path: filePath);
            var root = await syntaxTree.GetRootAsync();

            // Check for syntax errors
            var syntaxDiagnostics = syntaxTree.GetDiagnostics();
            fileHealth.SyntaxErrors = syntaxDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            fileHealth.IsParseable = fileHealth.SyntaxErrors == 0;
        }
        catch (Exception ex)
        {
            fileHealth.IsParseable = false;
            fileHealth.ParseError = ex.Message;
        }

        return fileHealth;
    }

    /// <summary>
    /// Add or update a document in the workspace from file
    /// </summary>
    public async Task AddDocumentAsync(string filePath)
    {
        if (_workspace == null || _projectId == null)
        {
            throw new InvalidOperationException("Workspace not initialized");
        }

        var text = await File.ReadAllTextAsync(filePath);
        var sourceText = SourceText.From(text);

        if (_documentMap.TryGetValue(filePath, out var existingDocId))
        {
            // Update existing document
            var document = _workspace.CurrentSolution.GetDocument(existingDocId);
            if (document != null)
            {
                var newDocument = document.WithText(sourceText);
                _workspace.TryApplyChanges(newDocument.Project.Solution);

                // Invalidate semantic model cache for the updated document
                _semanticModelCache.Invalidate(existingDocId);
            }
        }
        else if (_useAdhoc)
        {
            // Only add new documents if using adhoc workspace
            // MSBuildWorkspace manages its own documents based on .csproj
            var adhocWorkspace = (AdhocWorkspace)_workspace;
            var documentInfo = DocumentInfo.Create(
                DocumentId.CreateNewId(_projectId),
                Path.GetFileName(filePath),
                loader: TextLoader.From(TextAndVersion.Create(sourceText, VersionStamp.Default)),
                filePath: filePath
            );

            var doc = adhocWorkspace.AddDocument(documentInfo);
            _documentMap[filePath] = doc.Id;
        }

        // Refresh compilation with caching
        var project = _workspace.CurrentSolution.GetProject(_projectId);
        if (project != null)
        {
            _compilation = await _compilationCache.GetOrCreateAsync(project);
        }
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
            // Try to parse the file first (Level 1: Syntax-only)
            var text = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(text, path: filePath);
            var root = await syntaxTree.GetRootAsync();

            // Check for syntax errors
            var syntaxDiagnostics = syntaxTree.GetDiagnostics();
            fileHealth.SyntaxErrors = syntaxDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            fileHealth.IsParseable = fileHealth.SyntaxErrors == 0;

            // Add to workspace even if there are syntax errors (best effort)
            await AddDocumentAsync(filePath);
        }
        catch (Exception ex)
        {
            fileHealth.IsParseable = false;
            fileHealth.ParseError = ex.Message;
        }

        return fileHealth;
    }

    /// <summary>
    /// Add a document to the workspace with in-memory content
    /// </summary>
    public async Task<DocumentId> AddInMemoryDocumentAsync(string filePath, string content)
    {
        if (_workspace == null || _projectId == null)
        {
            throw new InvalidOperationException("Workspace not initialized");
        }

        if (!_useAdhoc)
        {
            throw new InvalidOperationException("AddInMemoryDocumentAsync is only supported with adhoc workspace");
        }

        var adhocWorkspace = (AdhocWorkspace)_workspace;
        var sourceText = SourceText.From(content);

        var documentInfo = DocumentInfo.Create(
            DocumentId.CreateNewId(_projectId),
            Path.GetFileName(filePath),
            loader: TextLoader.From(TextAndVersion.Create(sourceText, VersionStamp.Default)),
            filePath: filePath
        );

        var doc = adhocWorkspace.AddDocument(documentInfo);
        _documentMap[filePath] = doc.Id;

        // Refresh compilation with caching
        var project = adhocWorkspace.CurrentSolution.GetProject(_projectId);
        if (project != null)
        {
            _compilation = await _compilationCache.GetOrCreateAsync(project);
        }

        return doc.Id;
    }

    /// <summary>
    /// Initialize workspace for testing with in-memory documents
    /// </summary>
    public async Task InitializeTestWorkspaceAsync()
    {
        var adhocWorkspace = new AdhocWorkspace();
        _workspace = adhocWorkspace;
        _useAdhoc = true;

        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "TestProject",
            "TestProject",
            LanguageNames.CSharp
        );

        _projectId = adhocWorkspace.AddProject(projectInfo).Id;

        // Add reference assemblies for common types
        var project = adhocWorkspace.CurrentSolution.GetProject(_projectId);
        if (project != null)
        {
            // Add basic metadata references
            var metadataReferences = new[]
            {
                typeof(object).Assembly.Location,
                typeof(Enumerable).Assembly.Location,
                typeof(Task).Assembly.Location
            };

            foreach (var reference in metadataReferences)
            {
                if (File.Exists(reference))
                {
                    project = project.AddMetadataReference(MetadataReference.CreateFromFile(reference));
                }
            }

            adhocWorkspace.TryApplyChanges(project.Solution);
            _compilation = await _compilationCache.GetOrCreateAsync(project);
        }
    }

    /// <summary>
    /// Update document content incrementally
    /// </summary>
    public async Task<bool> UpdateDocumentAsync(string filePath, string newContent, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        
        if (_workspace == null || _projectId == null || !_documentMap.TryGetValue(filePath, out var docId))
        {
            return false;
        }

        var oldDocument = _workspace.CurrentSolution.GetDocument(docId);
        if (oldDocument == null)
        {
            return false;
        }

        // Create new document with updated text
        var sourceText = SourceText.From(newContent);
        var newDocument = oldDocument.WithText(sourceText);

        // Apply changes - Roslyn handles incremental compilation
        if (_workspace.TryApplyChanges(newDocument.Project.Solution))
        {
            // Update compilation cache
            var project = _workspace.CurrentSolution.GetProject(_projectId);
            if (project != null)
            {
                _compilation = await _compilationCache.GetOrCreateAsync(project, ct);
            }

            // Invalidate semantic model cache only for this document
            _semanticModelCache.Invalidate(docId);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Get the current compilation
    /// </summary>
    public virtual Compilation? GetCompilation() =>
        _compilation;

    /// <summary>
    /// Get a document by file path
    /// </summary>
    public Document? GetDocument(string filePath)
    {
        if (_workspace == null || !_documentMap.TryGetValue(filePath, out var docId))
        {
            return null;
        }

        return _workspace.CurrentSolution.GetDocument(docId);
    }

    /// <summary>
    /// Get all documents in the workspace
    /// </summary>
    public IEnumerable<Document> GetAllDocuments()
    {
        if (_workspace == null || _projectId == null)
        {
            return Enumerable.Empty<Document>();
        }

        var project = _workspace.CurrentSolution.GetProject(_projectId);
        return project?.Documents ?? Enumerable.Empty<Document>();
    }

    /// <summary>
    /// Check if workspace is initialized
    /// </summary>
    public bool IsInitialized =>
        _workspace != null && _compilation != null;

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
    /// Get semantic model for a syntax tree (legacy method)
    /// </summary>
    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    public async Task<SemanticModel?> GetSemanticModelAsync(
        SyntaxTree syntaxTree,
        CancellationToken ct = default)
    {
        if (_compilation == null)
        {
            return null;
        }

        // Check if the syntax tree belongs to this compilation
        if (!_compilation.ContainsSyntaxTree(syntaxTree))
        {
            return null;
        }

        return _compilation.GetSemanticModel(syntaxTree);
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
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RoslynWorkspace));
        }
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
                    // Dispose managed resources
                    _semanticModelCache?.Dispose();
                    _compilationCache?.Dispose();
                    _workspace?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}