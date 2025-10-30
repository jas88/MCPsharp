using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Core Roslyn workspace for C# code analysis
/// </summary>
public class RoslynWorkspace
{
    private AdhocWorkspace? _workspace;
    private ProjectId? _projectId;
    private Compilation? _compilation;
    private readonly Dictionary<string, DocumentId> _documentMap = new();

    /// <summary>
    /// Initialize workspace with a project directory
    /// </summary>
    public async Task InitializeAsync(string projectPath)
    {
        _workspace = new AdhocWorkspace();

        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "Project",
            "Project",
            LanguageNames.CSharp
        );

        _projectId = _workspace.AddProject(projectInfo).Id;

        // Add all .cs files
        foreach (var file in Directory.EnumerateFiles(projectPath, "*.cs", SearchOption.AllDirectories))
        {
            // Skip obj and bin directories
            if (file.Contains("/obj/") || file.Contains("/bin/") ||
                file.Contains("\\obj\\") || file.Contains("\\bin\\"))
            {
                continue;
            }

            await AddDocumentAsync(file);
        }

        // Get compilation
        var project = _workspace.CurrentSolution.GetProject(_projectId);
        if (project != null)
        {
            _compilation = await project.GetCompilationAsync();
        }
    }

    /// <summary>
    /// Add or update a document in the workspace
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
            }
        }
        else
        {
            // Add new document
            var documentInfo = DocumentInfo.Create(
                DocumentId.CreateNewId(_projectId),
                Path.GetFileName(filePath),
                loader: TextLoader.From(TextAndVersion.Create(sourceText, VersionStamp.Default)),
                filePath: filePath
            );

            var doc = _workspace.AddDocument(documentInfo);
            _documentMap[filePath] = doc.Id;
        }

        // Refresh compilation
        var project = _workspace.CurrentSolution.GetProject(_projectId);
        if (project != null)
        {
            _compilation = await project.GetCompilationAsync();
        }
    }

    /// <summary>
    /// Get the current compilation
    /// </summary>
    public Compilation? GetCompilation() => _compilation;

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
    public bool IsInitialized => _workspace != null && _compilation != null;

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
    /// Get semantic model for a syntax tree
    /// </summary>
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
}
