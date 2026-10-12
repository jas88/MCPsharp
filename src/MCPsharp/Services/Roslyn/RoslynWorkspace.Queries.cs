using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Solution-aware query API. All enumeration methods operate over the "primary" project of each
/// project file, so a multi-targeted project (loaded once per target framework) contributes its
/// documents, syntax trees and source symbols exactly once.
/// </summary>
public partial class RoslynWorkspace
{
    /// <summary>
    /// The primary projects of the solution (one per project file; the first target framework wins).
    /// </summary>
    public IReadOnlyList<Project> GetProjects()
    {
        if (_workspace == null)
        {
            return Array.Empty<Project>();
        }

        var solution = _workspace.CurrentSolution;
        return _state.PrimaryProjectIds
            .Select(solution.GetProject)
            .OfType<Project>()
            .ToList();
    }

    /// <summary>
    /// Compilations of every primary project, in solution order.
    /// </summary>
    public virtual IReadOnlyList<Compilation> GetCompilations()
    {
        var state = _state;
        return state.PrimaryProjectIds
            .Select(id => state.Compilations.GetValueOrDefault(id))
            .OfType<Compilation>()
            .ToList();
    }

    /// <summary>
    /// Compilation for a specific project (any target framework), or null if unavailable.
    /// </summary>
    public Compilation? GetCompilation(ProjectId projectId) =>
        _state.Compilations.GetValueOrDefault(projectId);

    /// <summary>
    /// The compilation that owns the given syntax tree, or null when the tree is not part of the solution.
    /// </summary>
    public Compilation? GetCompilationForTree(SyntaxTree syntaxTree) =>
        _state.TreeOwners.GetValueOrDefault(syntaxTree);

    /// <summary>
    /// The compilation of the primary project containing the given file.
    /// </summary>
    public Compilation? GetCompilationForFile(string filePath)
    {
        var document = GetDocument(filePath);
        return document == null ? null : GetCompilation(document.Project.Id);
    }

    /// <summary>
    /// The primary project containing the given file.
    /// </summary>
    public Project? GetProjectForFile(string filePath) => GetDocument(filePath)?.Project;

    /// <summary>
    /// Semantic model for a syntax tree, created from the compilation that owns the tree.
    /// Returns null when the tree is not part of any compilation in the solution.
    /// </summary>
    public SemanticModel? GetSemanticModel(SyntaxTree syntaxTree) =>
        GetCompilationForTree(syntaxTree)?.GetSemanticModel(syntaxTree);

    /// <summary>
    /// Syntax trees of every primary compilation, de-duplicated by file path
    /// (so linked files and multi-targeted projects yield each file once).
    /// </summary>
    public IReadOnlyList<SyntaxTree> GetSourceTrees()
    {
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        return GetCompilations()
            .SelectMany(compilation => compilation.SyntaxTrees)
            .Where(tree => string.IsNullOrEmpty(tree.FilePath) || seenPaths.Add(tree.FilePath))
            .ToList();
    }

    /// <summary>
    /// Source-declared symbols matching a name predicate across every primary compilation.
    /// </summary>
    public IReadOnlyList<ISymbol> FindSourceSymbols(
        Func<string, bool> predicate,
        SymbolFilter filter = SymbolFilter.TypeAndMember,
        CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return GetCompilations()
            .SelectMany(compilation => compilation.GetSymbolsWithName(predicate, filter, cancellationToken))
            .Where(symbol => seen.Add(GetSymbolIdentity(symbol)))
            .ToList();
    }

    /// <summary>
    /// Source-declared symbols with an exact name across every primary compilation.
    /// </summary>
    public IReadOnlyList<ISymbol> FindSourceSymbols(
        string name,
        SymbolFilter filter = SymbolFilter.TypeAndMember,
        CancellationToken cancellationToken = default) =>
        FindSourceSymbols(n => n == name, filter, cancellationToken);

    /// <summary>
    /// Resolve a type by metadata name, preferring a compilation whose own source declares it.
    /// </summary>
    public INamedTypeSymbol? GetTypeByMetadataName(string fullyQualifiedMetadataName)
    {
        INamedTypeSymbol? fallback = null;
        foreach (var compilation in GetCompilations())
        {
            var type = compilation.GetTypeByMetadataName(fullyQualifiedMetadataName);
            if (type == null)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
            {
                return type;
            }

            fallback ??= type;
        }

        return fallback;
    }

    /// <summary>
    /// Get the document for a file path, preferring the primary project when the file
    /// belongs to several projects (multi-targeting or linked files).
    /// </summary>
    public Document? GetDocument(string filePath)
    {
        if (_workspace == null)
        {
            return null;
        }

        var solution = _workspace.CurrentSolution;
        var ids = FindDocumentIds(solution, filePath);
        if (ids.Count == 0)
        {
            return null;
        }

        var primary = _state.PrimaryProjectIds;
        var preferred = ids.FirstOrDefault(id => primary.Contains(id.ProjectId)) ?? ids[0];
        return solution.GetDocument(preferred);
    }

    /// <summary>
    /// Every document instance for a file path, one per project that includes it.
    /// </summary>
    public IReadOnlyList<Document> GetDocuments(string filePath)
    {
        if (_workspace == null)
        {
            return Array.Empty<Document>();
        }

        var solution = _workspace.CurrentSolution;
        return FindDocumentIds(solution, filePath)
            .Select(solution.GetDocument)
            .OfType<Document>()
            .ToList();
    }

    /// <summary>
    /// All documents across every primary project, de-duplicated by file path.
    /// </summary>
    public IReadOnlyList<Document> GetAllDocuments()
    {
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        return GetProjects()
            .SelectMany(project => project.Documents)
            .Where(document => document.FilePath == null || seenPaths.Add(document.FilePath))
            .ToList();
    }

    private static IReadOnlyList<DocumentId> FindDocumentIds(Solution solution, string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return Array.Empty<DocumentId>();
        }

        var ids = solution.GetDocumentIdsWithFilePath(filePath);
        if (ids.IsEmpty)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(filePath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Array.Empty<DocumentId>();
            }

            if (!string.Equals(fullPath, filePath, StringComparison.Ordinal))
            {
                ids = solution.GetDocumentIdsWithFilePath(fullPath);
            }
        }

        return ids;
    }

    private static string GetSymbolIdentity(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        var locationKey = location == null
            ? string.Empty
            : $"{location.SourceTree?.FilePath}:{location.SourceSpan.Start}";
        return $"{symbol.Kind}|{symbol.ToDisplayString()}|{locationKey}";
    }

    /// <summary>
    /// Immutable snapshot of the compiled state of the solution.
    /// </summary>
    private sealed class CompilationState
    {
        public static readonly CompilationState Empty = new(
            new Dictionary<ProjectId, Compilation>(),
            Array.Empty<ProjectId>(),
            new Dictionary<SyntaxTree, Compilation>());

        private CompilationState(
            IReadOnlyDictionary<ProjectId, Compilation> compilations,
            IReadOnlyList<ProjectId> primaryProjectIds,
            IReadOnlyDictionary<SyntaxTree, Compilation> treeOwners)
        {
            Compilations = compilations;
            PrimaryProjectIds = primaryProjectIds;
            TreeOwners = treeOwners;
        }

        /// <summary>Compilations for every project (all target frameworks).</summary>
        public IReadOnlyDictionary<ProjectId, Compilation> Compilations { get; }

        /// <summary>One project per project file, in solution order.</summary>
        public IReadOnlyList<ProjectId> PrimaryProjectIds { get; }

        /// <summary>Maps each syntax tree to the compilation it belongs to.</summary>
        public IReadOnlyDictionary<SyntaxTree, Compilation> TreeOwners { get; }

        public static CompilationState Create(Solution solution, IReadOnlyDictionary<ProjectId, Compilation> compilations)
        {
            var primary = new List<ProjectId>();
            var seenProjectFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in solution.Projects)
            {
                if (project.FilePath == null || seenProjectFiles.Add(project.FilePath))
                {
                    primary.Add(project.Id);
                }
            }

            var treeOwners = new Dictionary<SyntaxTree, Compilation>();
            foreach (var compilation in compilations.Values)
            {
                foreach (var tree in compilation.SyntaxTrees)
                {
                    treeOwners.TryAdd(tree, compilation);
                }
            }

            return new CompilationState(compilations, primary, treeOwners);
        }
    }
}
