using MCPsharp.Models.Database;
using MCPsharp.Services.Database;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Indexing;

/// <summary>
/// Builds cross-reference graphs by analyzing code relationships.
/// </summary>
public sealed class ReferenceIndexerService
{
    private readonly ProjectDatabase _database;
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<ReferenceIndexerService>? _logger;

    private int _indexedFiles;
    private int _totalFiles;
    private int _referenceCount;

    public int IndexedFiles => _indexedFiles;
    public int TotalFiles => _totalFiles;
    public int ReferenceCount => _referenceCount;
    public double Progress => _totalFiles == 0 ? 0 : (double)_indexedFiles / _totalFiles;

    public ReferenceIndexerService(
        ProjectDatabase database,
        RoslynWorkspace workspace,
        ILogger<ReferenceIndexerService>? logger = null)
    {
        _database = database;
        _workspace = workspace;
        _logger = logger;
    }

    /// <summary>
    /// Indexes all references in the current project/solution.
    /// </summary>
    public async Task IndexReferencesAsync(long projectId, CancellationToken cancellationToken = default)
    {
        if (!_workspace.IsInitialized)
        {
            _logger?.LogWarning("Workspace not initialized, cannot index references");
            return;
        }

        var solution = _workspace.Solution;
        var documents = solution.Projects
            .SelectMany(p => p.Documents)
            .Where(d => d.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        _totalFiles = documents.Count;
        _indexedFiles = 0;
        _referenceCount = 0;

        // Build symbol lookup from database
        var symbolLookup = await BuildSymbolLookupAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Starting reference indexing of {Count} documents with {Symbols} known symbols",
            _totalFiles, symbolLookup.Count);

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await IndexDocumentReferencesAsync(projectId, document, symbolLookup, cancellationToken)
                .ConfigureAwait(false);
            Interlocked.Increment(ref _indexedFiles);
        }

        _logger?.LogInformation("Reference indexing complete: {Files} files, {Refs} references",
            _indexedFiles, _referenceCount);
    }

    /// <summary>
    /// Indexes references in a single document.
    /// </summary>
    public async Task IndexDocumentReferencesAsync(
        long projectId,
        Document document,
        Dictionary<string, long> symbolLookup,
        CancellationToken cancellationToken = default)
    {
        if (document.FilePath == null) return;

        try
        {
            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            if (syntaxTree == null || semanticModel == null) return;

            var root = await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);

            // Get file ID from database
            var relativePath = GetRelativePath(document.FilePath);
            var file = await _database.GetFileAsync(projectId, relativePath, cancellationToken)
                .ConfigureAwait(false);

            if (file == null)
            {
                _logger?.LogDebug("File not indexed yet: {Path}", relativePath);
                return;
            }

            // Clear existing references for this file
            await _database.DeleteReferencesForFileAsync(file.Id, cancellationToken).ConfigureAwait(false);

            var references = ExtractReferences(root, semanticModel, file.Id, symbolLookup);

            if (references.Count > 0)
            {
                await _database.UpsertReferencesBatchAsync(references, cancellationToken).ConfigureAwait(false);
                Interlocked.Add(ref _referenceCount, references.Count);
            }

            _logger?.LogDebug("Indexed references in {Path}: {Count} references", relativePath, references.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to index references in document: {Path}", document.FilePath);
        }
    }

    private List<DbReference> ExtractReferences(
        SyntaxNode root,
        SemanticModel semanticModel,
        long fileId,
        Dictionary<string, long> symbolLookup)
    {
        var references = new List<DbReference>();

        // Find method invocations
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is IMethodSymbol targetMethod)
            {
                var fromSymbol = GetContainingMethodSymbol(invocation, semanticModel);
                if (fromSymbol != null)
                {
                    var reference = CreateReference(fromSymbol, targetMethod, invocation, fileId, "Call", symbolLookup);
                    if (reference != null) references.Add(reference);
                }
            }
        }

        // Find member accesses (property reads)
        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
            if (symbolInfo.Symbol is IPropertySymbol targetProperty)
            {
                var fromSymbol = GetContainingMethodSymbol(memberAccess, semanticModel);
                if (fromSymbol != null)
                {
                    var reference = CreateReference(fromSymbol, targetProperty, memberAccess, fileId, "PropertyAccess", symbolLookup);
                    if (reference != null) references.Add(reference);
                }
            }
        }

        // Find type usages in base lists (inheritance, implements)
        foreach (var baseType in root.DescendantNodes().OfType<BaseTypeSyntax>())
        {
            var typeInfo = semanticModel.GetTypeInfo(baseType.Type);
            if (typeInfo.Type is INamedTypeSymbol targetType)
            {
                var containingType = GetContainingTypeSymbol(baseType, semanticModel);
                if (containingType != null)
                {
                    var kind = targetType.TypeKind == TypeKind.Interface ? "Implementation" : "Inheritance";
                    var reference = CreateReference(containingType, targetType, baseType, fileId, kind, symbolLookup);
                    if (reference != null) references.Add(reference);
                }
            }
        }

        // Find object creation expressions (constructor calls)
        foreach (var objectCreation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(objectCreation);
            if (symbolInfo.Symbol is IMethodSymbol constructor)
            {
                var fromSymbol = GetContainingMethodSymbol(objectCreation, semanticModel);
                if (fromSymbol != null)
                {
                    var reference = CreateReference(fromSymbol, constructor.ContainingType, objectCreation, fileId, "TypeUsage", symbolLookup);
                    if (reference != null) references.Add(reference);
                }
            }
        }

        return references;
    }

    private DbReference? CreateReference(
        ISymbol fromSymbol,
        ISymbol toSymbol,
        SyntaxNode node,
        long fileId,
        string referenceKind,
        Dictionary<string, long> symbolLookup)
    {
        var fromKey = GetSymbolKey(fromSymbol);
        var toKey = GetSymbolKey(toSymbol);

        if (!symbolLookup.TryGetValue(fromKey, out var fromSymbolId) ||
            !symbolLookup.TryGetValue(toKey, out var toSymbolId))
        {
            return null; // Symbol not in our index
        }

        var location = node.GetLocation();
        var lineSpan = location.GetLineSpan();

        return new DbReference
        {
            FromSymbolId = fromSymbolId,
            ToSymbolId = toSymbolId,
            ReferenceKind = referenceKind,
            FileId = fileId,
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1
        };
    }

    private static IMethodSymbol? GetContainingMethodSymbol(SyntaxNode node, SemanticModel semanticModel)
    {
        var methodDecl = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDecl != null)
        {
            return semanticModel.GetDeclaredSymbol(methodDecl);
        }

        var ctorDecl = node.Ancestors().OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (ctorDecl != null)
        {
            return semanticModel.GetDeclaredSymbol(ctorDecl);
        }

        return null;
    }

    private static INamedTypeSymbol? GetContainingTypeSymbol(SyntaxNode node, SemanticModel semanticModel)
    {
        var typeDecl = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl != null)
        {
            return semanticModel.GetDeclaredSymbol(typeDecl);
        }
        return null;
    }

    private static string GetSymbolKey(ISymbol symbol)
    {
        // Create a unique key for looking up symbols
        var containingType = symbol.ContainingType?.Name ?? "";
        var ns = symbol.ContainingNamespace?.IsGlobalNamespace == false
            ? symbol.ContainingNamespace.ToDisplayString()
            : "";

        return $"{ns}::{containingType}::{symbol.Name}::{symbol.Kind}";
    }

    private async Task<Dictionary<string, long>> BuildSymbolLookupAsync(CancellationToken cancellationToken)
    {
        var lookup = new Dictionary<string, long>();

        // Get all symbols from database and build lookup
        var symbols = await _database.SearchSymbolsAsync(limit: 100000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var symbol in symbols)
        {
            var key = $"{symbol.Namespace ?? ""}::{symbol.ContainingType ?? ""}::{symbol.Name}::{symbol.Kind}";
            lookup.TryAdd(key, symbol.Id);
        }

        return lookup;
    }

    private string GetRelativePath(string filePath)
    {
        var solution = _workspace.Solution;
        var solutionDir = solution.FilePath != null
            ? Path.GetDirectoryName(solution.FilePath)
            : null;

        if (solutionDir != null && filePath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(solutionDir, filePath);
        }

        return filePath;
    }
}
