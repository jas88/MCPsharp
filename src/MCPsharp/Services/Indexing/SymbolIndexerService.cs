using System.Security.Cryptography;
using System.Text;
using MCPsharp.Models.Database;
using MCPsharp.Services.Database;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Indexing;

/// <summary>
/// Extracts symbols from Roslyn compilations and stores them in the cache database.
/// </summary>
public sealed class SymbolIndexerService
{
    private readonly ProjectDatabase _database;
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<SymbolIndexerService>? _logger;

    private int _indexedFiles;
    private int _totalFiles;
    private int _symbolCount;

    public int IndexedFiles => _indexedFiles;
    public int TotalFiles => _totalFiles;
    public int SymbolCount => _symbolCount;
    public double Progress => _totalFiles == 0 ? 0 : (double)_indexedFiles / _totalFiles;

    public SymbolIndexerService(
        ProjectDatabase database,
        RoslynWorkspace workspace,
        ILogger<SymbolIndexerService>? logger = null)
    {
        _database = database;
        _workspace = workspace;
        _logger = logger;
    }

    /// <summary>
    /// Indexes all documents in the current project/solution.
    /// </summary>
    public async Task IndexProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        if (!_workspace.IsInitialized)
        {
            _logger?.LogWarning("Workspace not initialized, cannot index");
            return;
        }

        var solution = _workspace.Solution;
        var documents = solution.Projects
            .SelectMany(p => p.Documents)
            .Where(d => d.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        _totalFiles = documents.Count;
        _indexedFiles = 0;
        _symbolCount = 0;

        _logger?.LogInformation("Starting indexing of {Count} documents", _totalFiles);

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await IndexDocumentAsync(projectId, document, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _indexedFiles);
        }

        _logger?.LogInformation("Indexing complete: {Files} files, {Symbols} symbols", _indexedFiles, _symbolCount);
    }

    /// <summary>
    /// Indexes a single document.
    /// </summary>
    public async Task IndexDocumentAsync(long projectId, Document document, CancellationToken cancellationToken = default)
    {
        if (document.FilePath == null) return;

        try
        {
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var content = text.ToString();
            var contentHash = ComputeHash(content);
            var relativePath = GetRelativePath(document.FilePath);

            // Upsert file record
            var fileId = await _database.UpsertFileAsync(
                projectId,
                relativePath,
                contentHash,
                Encoding.UTF8.GetByteCount(content),
                "csharp",
                cancellationToken).ConfigureAwait(false);

            // Clear existing symbols for this file
            await _database.DeleteSymbolsForFileAsync(fileId, cancellationToken).ConfigureAwait(false);

            // Get syntax tree and semantic model
            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            if (syntaxTree == null || semanticModel == null) return;

            var root = await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            var symbols = ExtractSymbols(root, semanticModel, fileId);

            if (symbols.Count > 0)
            {
                await _database.UpsertSymbolsBatchAsync(symbols, cancellationToken).ConfigureAwait(false);
                Interlocked.Add(ref _symbolCount, symbols.Count);
            }

            _logger?.LogDebug("Indexed {Path}: {Count} symbols", relativePath, symbols.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to index document: {Path}", document.FilePath);
        }
    }

    /// <summary>
    /// Indexes multiple documents in parallel.
    /// </summary>
    public async Task IndexDocumentsAsync(
        long projectId,
        IEnumerable<Document> documents,
        int maxParallelism = 4,
        CancellationToken cancellationToken = default)
    {
        var docList = documents.ToList();
        _totalFiles = docList.Count;
        _indexedFiles = 0;

        await Parallel.ForEachAsync(
            docList,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = cancellationToken
            },
            async (document, ct) =>
            {
                await IndexDocumentAsync(projectId, document, ct).ConfigureAwait(false);
                Interlocked.Increment(ref _indexedFiles);
            }).ConfigureAwait(false);
    }

    private List<DbSymbol> ExtractSymbols(SyntaxNode root, SemanticModel semanticModel, long fileId)
    {
        var symbols = new List<DbSymbol>();

        // Extract type declarations (classes, interfaces, structs, enums, records)
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(typeDecl);
            if (symbol == null) continue;

            var kind = typeDecl switch
            {
                ClassDeclarationSyntax => "Class",
                InterfaceDeclarationSyntax => "Interface",
                StructDeclarationSyntax => "Struct",
                RecordDeclarationSyntax => "Record",
                _ => "Type"
            };

            symbols.Add(CreateDbSymbol(typeDecl, symbol, kind, fileId));
        }

        // Extract enum declarations
        foreach (var enumDecl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(enumDecl);
            if (symbol == null) continue;
            symbols.Add(CreateDbSymbol(enumDecl, symbol, "Enum", fileId));
        }

        // Extract method declarations
        foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
            if (symbol == null) continue;
            symbols.Add(CreateDbSymbol(methodDecl, symbol, "Method", fileId, GetMethodSignature(symbol)));
        }

        // Extract constructor declarations
        foreach (var ctorDecl in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(ctorDecl);
            if (symbol == null) continue;
            symbols.Add(CreateDbSymbol(ctorDecl, symbol, "Constructor", fileId, GetMethodSignature(symbol)));
        }

        // Extract property declarations
        foreach (var propDecl in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(propDecl);
            if (symbol == null) continue;
            symbols.Add(CreateDbSymbol(propDecl, symbol, "Property", fileId, symbol.Type.ToDisplayString()));
        }

        // Extract field declarations
        foreach (var fieldDecl in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in fieldDecl.Declaration.Variables)
            {
                var symbol = semanticModel.GetDeclaredSymbol(variable);
                if (symbol is not IFieldSymbol fieldSymbol) continue;
                symbols.Add(CreateDbSymbol(variable, fieldSymbol, "Field", fileId, fieldSymbol.Type.ToDisplayString()));
            }
        }

        // Extract event declarations
        foreach (var eventDecl in root.DescendantNodes().OfType<EventDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(eventDecl);
            if (symbol == null) continue;
            symbols.Add(CreateDbSymbol(eventDecl, symbol, "Event", fileId));
        }

        // Extract delegate declarations
        foreach (var delegateDecl in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(delegateDecl);
            if (symbol == null) continue;
            symbols.Add(CreateDbSymbol(delegateDecl, symbol, "Delegate", fileId));
        }

        return symbols;
    }

    private static DbSymbol CreateDbSymbol(SyntaxNode node, ISymbol symbol, string kind, long fileId, string? signature = null)
    {
        var location = node.GetLocation();
        var lineSpan = location.GetLineSpan();

        return new DbSymbol
        {
            FileId = fileId,
            Name = symbol.Name,
            Kind = kind,
            Namespace = symbol.ContainingNamespace?.IsGlobalNamespace == false
                ? symbol.ContainingNamespace.ToDisplayString()
                : null,
            ContainingType = symbol.ContainingType?.Name,
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            EndColumn = lineSpan.EndLinePosition.Character + 1,
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            Signature = signature
        };
    }

    private static string GetMethodSignature(IMethodSymbol method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"));
        var returnType = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString();
        return $"{returnType} {method.Name}({parameters})";
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string GetRelativePath(string filePath)
    {
        // Try to get relative path from workspace root
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
