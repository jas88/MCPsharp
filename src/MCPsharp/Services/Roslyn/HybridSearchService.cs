using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Search strategy for hybrid search with automatic fallback
/// </summary>
public enum SearchStrategy
{
    /// <summary>
    /// Automatically select best available strategy based on workspace health
    /// </summary>
    Auto,

    /// <summary>
    /// Level 2: Semantic search using full Roslyn compilation (requires clean build)
    /// </summary>
    Semantic,

    /// <summary>
    /// Level 1: Syntax-only search using per-file parsing (requires parseable files)
    /// </summary>
    Syntax,

    /// <summary>
    /// Level 0: Text-based regex search (always available)
    /// </summary>
    Text
}

/// <summary>
/// Result of a hybrid search operation with metadata about the method used
/// </summary>
public class SearchResult
{
    public string Query { get; set; } = "";
    public List<HybridSearchResult> Symbols { get; set; } = new();
    public string Method { get; set; } = "unknown";
    public string Confidence { get; set; } = "unknown";
    public List<string> Warnings { get; set; } = new();
    public bool Success { get; set; }
}

/// <summary>
/// Location of a symbol found during hybrid search
/// </summary>
public class HybridSearchResult
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string Kind { get; set; } = "";
    public string? ContainingType { get; set; }
    public string? Namespace { get; set; }
}

/// <summary>
/// Hybrid search service that automatically falls back from semantic → syntax → text
/// based on workspace health and availability
/// </summary>
public class HybridSearchService
{
    private readonly RoslynWorkspace _workspace;
    private readonly SymbolQueryService? _symbolQuery;
    private readonly ILogger<HybridSearchService> _logger;

    public HybridSearchService(
        RoslynWorkspace workspace,
        SymbolQueryService? symbolQuery,
        ILogger<HybridSearchService> logger)
    {
        _workspace = workspace;
        _symbolQuery = symbolQuery;
        _logger = logger;
    }

    /// <summary>
    /// Find a symbol using the best available search method
    /// </summary>
    public async Task<SearchResult> FindSymbolAsync(
        string name,
        SearchStrategy strategy = SearchStrategy.Auto)
    {
        var result = new SearchResult
        {
            Query = name,
            Success = false
        };

        var health = _workspace.GetHealth();

        // Determine effective strategy based on workspace health
        var effectiveStrategy = strategy;
        if (strategy == SearchStrategy.Auto)
        {
            effectiveStrategy = DetermineOptimalStrategy(health);
        }

        // Try semantic search first if requested and available
        if ((effectiveStrategy == SearchStrategy.Semantic || effectiveStrategy == SearchStrategy.Auto) &&
            health.CanDoSemanticOperations && _symbolQuery != null)
        {
            try
            {
                result.Symbols = await SearchSemanticAsync(name);
                result.Method = "semantic";
                result.Confidence = "high";
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Semantic search failed: {ex.Message}");
                result.Warnings.Add($"Semantic search failed: {ex.Message}");
            }
        }

        // Fallback to syntax-only search
        if ((effectiveStrategy == SearchStrategy.Syntax || effectiveStrategy == SearchStrategy.Auto || 
             effectiveStrategy == SearchStrategy.Semantic) &&
            health.CanDoSyntaxOperations)
        {
            try
            {
                result.Symbols = await SearchSyntaxAsync(name);
                result.Method = "syntax";
                result.Confidence = "medium";
                result.Success = true;
                result.Warnings.Add("Using syntax-only search. Some cross-file references may be missed.");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Syntax search failed: {ex.Message}");
                result.Warnings.Add($"Syntax search failed: {ex.Message}");
            }
        }

        // Final fallback to text search
        try
        {
            result.Symbols = await SearchTextAsync(name);
            result.Method = "text";
            result.Confidence = "low";
            result.Success = true;
            result.Warnings.Add("Using text-based search. Results may include false positives.");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"All search methods failed: {ex.Message}");
            result.Warnings.Add($"Text search failed: {ex.Message}");
            result.Success = false;
            return result;
        }
    }

    /// <summary>
    /// Determine the optimal search strategy based on workspace health
    /// </summary>
    private SearchStrategy DetermineOptimalStrategy(WorkspaceHealth health)
    {
        if (health.CanDoSemanticOperations)
        {
            return SearchStrategy.Semantic;
        }
        else if (health.CanDoSyntaxOperations)
        {
            return SearchStrategy.Syntax;
        }
        else
        {
            return SearchStrategy.Text;
        }
    }

    /// <summary>
    /// Level 2: Semantic search using full Roslyn compilation
    /// </summary>
    private async Task<List<HybridSearchResult>> SearchSemanticAsync(string name)
    {
        if (_symbolQuery == null)
        {
            throw new InvalidOperationException("Symbol query service not available");
        }

        var symbols = await _symbolQuery.FindSymbolsAsync(name);

        return symbols.Select(s => new HybridSearchResult
        {
            Name = s.Name,
            FilePath = s.File,
            Line = s.Line,
            Column = s.Column,
            Kind = s.Kind,
            ContainingType = null, // SymbolResult doesn't track containing type
            Namespace = s.ContainerName
        }).ToList();
    }

    /// <summary>
    /// Level 1: Syntax-only search using CSharpSyntaxTree.ParseText per file
    /// </summary>
    private async Task<List<HybridSearchResult>> SearchSyntaxAsync(string name)
    {
        var symbols = new List<HybridSearchResult>();
        var health = _workspace.GetHealth();

        // Only search in parseable files
        var parseableFiles = health.FileStatus
            .Where(kvp => kvp.Value.IsParseable)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var file in parseableFiles)
        {
            try
            {
                // Parse without semantic model - doesn't require compilation!
                var text = await File.ReadAllTextAsync(file);
                var tree = CSharpSyntaxTree.ParseText(text, path: file);
                var root = await tree.GetRootAsync();

                // Find type declarations (class, interface, record, struct, enum)
                var typeDeclarations = root.DescendantNodes()
                    .OfType<BaseTypeDeclarationSyntax>()
                    .Where(d => d.Identifier.Text == name);

                foreach (var decl in typeDeclarations)
                {
                    var location = decl.GetLocation();
                    var lineSpan = location.GetLineSpan();

                    symbols.Add(new HybridSearchResult
                    {
                        Name = name,
                        FilePath = file,
                        Line = lineSpan.StartLinePosition.Line + 1,
                        Column = lineSpan.StartLinePosition.Character + 1,
                        Kind = GetDeclarationKind(decl),
                        ContainingType = GetContainingType(decl),
                        Namespace = GetNamespace(decl)
                    });
                }

                // Find method declarations
                var methodDeclarations = root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Identifier.Text == name);

                foreach (var method in methodDeclarations)
                {
                    var location = method.GetLocation();
                    var lineSpan = location.GetLineSpan();

                    symbols.Add(new HybridSearchResult
                    {
                        Name = name,
                        FilePath = file,
                        Line = lineSpan.StartLinePosition.Line + 1,
                        Column = lineSpan.StartLinePosition.Character + 1,
                        Kind = "method",
                        ContainingType = GetContainingType(method),
                        Namespace = GetNamespace(method)
                    });
                }

                // Find property declarations
                var propertyDeclarations = root.DescendantNodes()
                    .OfType<PropertyDeclarationSyntax>()
                    .Where(p => p.Identifier.Text == name);

                foreach (var property in propertyDeclarations)
                {
                    var location = property.GetLocation();
                    var lineSpan = location.GetLineSpan();

                    symbols.Add(new HybridSearchResult
                    {
                        Name = name,
                        FilePath = file,
                        Line = lineSpan.StartLinePosition.Line + 1,
                        Column = lineSpan.StartLinePosition.Character + 1,
                        Kind = "property",
                        ContainingType = GetContainingType(property),
                        Namespace = GetNamespace(property)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Failed to parse {file}: {ex.Message}");
                // Continue with other files - partial results better than no results
            }
        }

        return symbols;
    }

    /// <summary>
    /// Level 0: Text-based regex search (always available)
    /// </summary>
    private async Task<List<HybridSearchResult>> SearchTextAsync(string name)
    {
        var symbols = new List<HybridSearchResult>();
        var health = _workspace.GetHealth();

        // Regex patterns for common declarations
        var patterns = new[]
        {
            // Class, interface, record, struct, enum declarations
            $@"(?:public|private|protected|internal|file)?\s*(?:static|sealed|abstract)?\s*(?:partial)?\s*(?:class|interface|record|struct|enum)\s+{Regex.Escape(name)}\b",
            // Method declarations
            $@"(?:public|private|protected|internal)?\s*(?:static|virtual|override|async)?\s*\w+\s+{Regex.Escape(name)}\s*\(",
            // Property declarations
            $@"(?:public|private|protected|internal)?\s*(?:static|virtual|override)?\s*\w+\s+{Regex.Escape(name)}\s*\{{",
            // Field declarations
            $@"(?:public|private|protected|internal)?\s*(?:static|readonly)?\s*\w+\s+{Regex.Escape(name)}\s*[;=]"
        };

        // Search all files, not just parseable ones
        var allFiles = health.FileStatus.Keys.Where(f => f.EndsWith(".cs")).ToList();

        foreach (var file in allFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file);
                var lines = content.Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (var pattern in patterns)
                    {
                        var match = Regex.Match(lines[i], pattern);
                        if (match.Success)
                        {
                            var kind = InferKind(lines[i]);

                            symbols.Add(new HybridSearchResult
                            {
                                Name = name,
                                FilePath = file,
                                Line = i + 1,
                                Column = match.Index + 1,
                                Kind = kind
                            });

                            break; // Only add one match per line
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Failed to read {file}: {ex.Message}");
                // Continue with other files
            }
        }

        return symbols;
    }

    private string GetDeclarationKind(BaseTypeDeclarationSyntax decl)
    {
        return decl switch
        {
            ClassDeclarationSyntax => "class",
            InterfaceDeclarationSyntax => "interface",
            RecordDeclarationSyntax => "record",
            StructDeclarationSyntax => "struct",
            EnumDeclarationSyntax => "enum",
            _ => "type"
        };
    }

    private string? GetContainingType(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is ClassDeclarationSyntax classDecl)
                return classDecl.Identifier.Text;
            if (parent is InterfaceDeclarationSyntax interfaceDecl)
                return interfaceDecl.Identifier.Text;
            if (parent is RecordDeclarationSyntax recordDecl)
                return recordDecl.Identifier.Text;
            if (parent is StructDeclarationSyntax structDecl)
                return structDecl.Identifier.Text;
                
            parent = parent.Parent;
        }
        return null;
    }

    private string? GetNamespace(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is NamespaceDeclarationSyntax namespaceDecl)
                return namespaceDecl.Name.ToString();
            if (parent is FileScopedNamespaceDeclarationSyntax fileScopedNs)
                return fileScopedNs.Name.ToString();
                
            parent = parent.Parent;
        }
        return null;
    }

    private string InferKind(string line)
    {
        if (line.Contains(" class ")) return "class";
        if (line.Contains(" interface ")) return "interface";
        if (line.Contains(" record ")) return "record";
        if (line.Contains(" struct ")) return "struct";
        if (line.Contains(" enum ")) return "enum";
        if (line.Contains("(")) return "method";
        if (line.Contains("{")) return "property";
        if (line.Contains(";") || line.Contains("=")) return "field";
        return "unknown";
    }
}
