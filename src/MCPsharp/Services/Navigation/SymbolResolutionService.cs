using MCPsharp.Models.Navigation;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Service responsible for resolving symbols from positions in source code
/// </summary>
public partial class SymbolResolutionService : ISymbolResolutionService
{
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<SymbolResolutionService> _logger;

    public SymbolResolutionService(
        RoslynWorkspace workspace,
        ILogger<SymbolResolutionService>? logger = null)
    {
        _workspace = workspace;
        _logger = logger ?? NullLogger<SymbolResolutionService>.Instance;
    }

    /// <summary>
    /// Resolve a symbol at a specific position in a file
    /// </summary>
    public async Task<ResolvedSymbol?> ResolveSymbolAtPositionAsync(
        string filePath,
        int line,
        int column,
        SymbolResolutionOptions? options = null)
    {
        options ??= new SymbolResolutionOptions();

        try
        {
            // Get document and semantic model
            var document = _workspace.GetDocument(filePath);
            if (document == null)
            {
                LogDocumentNotFound(_logger, filePath);
                return null;
            }

            var syntaxTree = await document.GetSyntaxTreeAsync();
            var semanticModel = await document.GetSemanticModelAsync();
            if (syntaxTree == null || semanticModel == null)
            {
                LogNoSemanticModel(_logger, filePath);
                return null;
            }

            // Find position in the text
            var text = await syntaxTree.GetTextAsync();
            var position = GetTextPosition(text, line, column);
            if (position == -1)
            {
                LogInvalidPosition(_logger, line, column, filePath);
                return null;
            }

            // Find token at position
            var root = await syntaxTree.GetRootAsync();
            var token = root.FindToken(position);

            // Try multiple resolution strategies
            var (symbol, confidence, alternatives) = await ResolveSymbolWithStrategiesAsync(
                token, position, semanticModel, options);

            if (symbol == null)
            {
                LogNoSymbolAtPosition(_logger, line, column, filePath);
                return null;
            }

            // Get declaration location
            var declarationLocation = GetDeclarationLocation(symbol, options);
            if (declarationLocation == null)
            {
                LogNoSourceLocation(_logger, symbol.Name);
                return null;
            }

            // Get all locations for partial symbols
            var allLocations = GetAllSymbolLocations(symbol);

            return new ResolvedSymbol
            {
                Symbol = symbol,
                DeclarationLocation = declarationLocation,
                Confidence = confidence,
                AlternativeSymbols = alternatives,
                AllLocations = allLocations
            };
        }
        catch (Exception ex)
        {
            LogResolveError(_logger, ex, filePath, line, column);
            return null;
        }
    }

    /// <summary>
    /// Resolve a symbol by name with optional context
    /// </summary>
    public Task<ISymbol?> ResolveSymbolByNameAsync(
        string symbolName,
        string? containingType = null,
        string? containingNamespace = null)
    {
        if (!_workspace.IsInitialized)
        {
            return Task.FromResult<ISymbol?>(null);
        }

        // Find all symbols with the given name in every project of the solution
        var symbols = _workspace.FindSourceSymbols(symbolName, SymbolFilter.All);

        // Return the first symbol matching the optional context filters
        var match = symbols.FirstOrDefault(symbol =>
            (containingType == null ||
             symbol.ContainingType?.Name == containingType ||
             symbol.ContainingType?.ToDisplayString() == containingType) &&
            (containingNamespace == null ||
             symbol.ContainingNamespace?.ToDisplayString() == containingNamespace));

        return Task.FromResult(match);
    }

    private async Task<(ISymbol? symbol, SymbolConfidence confidence, List<ISymbol>? alternatives)>
        ResolveSymbolWithStrategiesAsync(
            SyntaxToken token,
            int position,
            SemanticModel semanticModel,
            SymbolResolutionOptions options)
    {
        ISymbol? symbol = null;
        var confidence = SymbolConfidence.Exact;
        var alternatives = new List<ISymbol>();

        // Strategy 1: Try to get symbol directly from the token's parent
        var node = token.Parent;
        if (node != null)
        {
            // Check if it's a declaration
            symbol = semanticModel.GetDeclaredSymbol(node);

            // If not a declaration, try to get symbol info
            if (symbol == null)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(node);

                if (symbolInfo.Symbol != null)
                {
                    symbol = symbolInfo.Symbol;
                }
                else if (symbolInfo.CandidateSymbols.Length > 0)
                {
                    // Multiple candidates - ambiguous
                    symbol = SelectBestCandidate(symbolInfo.CandidateSymbols, options);
                    alternatives.AddRange(symbolInfo.CandidateSymbols
                        .Where(s => !SymbolEqualityComparer.Default.Equals(s, symbol)));
                    confidence = symbolInfo.CandidateSymbols.Length == 1
                        ? SymbolConfidence.Exact
                        : SymbolConfidence.Ambiguous;
                }
            }
        }

        // Strategy 2: Check if it's a type reference
        if (symbol == null && node is TypeSyntax typeSyntax)
        {
            var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
            if (typeInfo.Type != null)
            {
                symbol = typeInfo.Type;
            }
        }

        // Strategy 3: Check if it's an identifier that's part of a member access
        if (symbol == null && token.IsKind(SyntaxKind.IdentifierToken))
        {
            // Walk up the tree to find a meaningful node
            var current = token.Parent;
            while (current != null && symbol == null)
            {
                switch (current)
                {
                    case MemberAccessExpressionSyntax memberAccess:
                        var memberSymbolInfo = semanticModel.GetSymbolInfo(memberAccess);
                        symbol = memberSymbolInfo.Symbol ?? memberSymbolInfo.CandidateSymbols.FirstOrDefault();
                        break;

                    case InvocationExpressionSyntax invocation:
                        var invocationSymbolInfo = semanticModel.GetSymbolInfo(invocation);
                        symbol = invocationSymbolInfo.Symbol ?? invocationSymbolInfo.CandidateSymbols.FirstOrDefault();
                        break;

                    case IdentifierNameSyntax identifier:
                        var identifierSymbolInfo = semanticModel.GetSymbolInfo(identifier);
                        symbol = identifierSymbolInfo.Symbol ?? identifierSymbolInfo.CandidateSymbols.FirstOrDefault();
                        break;
                }

                current = current.Parent;
            }

            if (symbol != null)
            {
                confidence = SymbolConfidence.High;
            }
        }

        // Strategy 4: Try to find symbol at position using Roslyn's FindSymbols
        if (symbol == null && _workspace.IsInitialized)
        {
            var solution = _workspace.Solution;
            var document = solution.GetDocument(semanticModel.SyntaxTree);
            if (document != null)
            {
                var symbolAtPosition = await SymbolFinder.FindSymbolAtPositionAsync(
                    document, position, CancellationToken.None);

                if (symbolAtPosition != null)
                {
                    symbol = symbolAtPosition;
                    confidence = SymbolConfidence.Medium;
                }
            }
        }

        // Get original definition if requested
        if (symbol != null && options.ResolveToOriginalDefinition)
        {
            symbol = symbol.OriginalDefinition;
        }

        return (symbol, confidence, alternatives.Count > 0 ? alternatives : null);
    }

    private static ISymbol SelectBestCandidate(
        IEnumerable<ISymbol> candidates,
        SymbolResolutionOptions options)
    {
        var candidateList = candidates.ToList();

        // If only one candidate, return it
        if (candidateList.Count == 1)
        {
            return candidateList[0];
        }

        // Prefer implementations over interfaces if requested
        if (options.PreferImplementationOverInterface)
        {
            var implementations = candidateList
                .Where(s => s.Kind != Microsoft.CodeAnalysis.SymbolKind.NamedType ||
                           ((INamedTypeSymbol)s).TypeKind != TypeKind.Interface)
                .ToList();

            if (implementations.Count == 1)
            {
                return implementations[0];
            }
        }

        // Prefer symbols in the current project (not from GAC/framework assemblies)
        var currentProjectSymbols = candidateList
            .Where(s => s.Locations.Any(l => l.IsInSource))
            .ToList();

        if (currentProjectSymbols.Count == 1)
        {
            return currentProjectSymbols[0];
        }

        // Prefer the most derived type
        if (candidateList.All(s => s is INamedTypeSymbol))
        {
            var types = candidateList.Cast<INamedTypeSymbol>().ToList();
            var mostDerived = types.FirstOrDefault(t =>
                types.All(other => !t.AllInterfaces.Contains(other, SymbolEqualityComparer.Default) &&
                                   !SymbolEqualityComparer.Default.Equals(t.BaseType, other)));

            if (mostDerived != null)
            {
                return mostDerived;
            }
        }

        // Default to first candidate
        return candidateList[0];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Document not found: {FilePath}")]
    private static partial void LogDocumentNotFound(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not get syntax tree or semantic model for: {FilePath}")]
    private static partial void LogNoSemanticModel(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid position: line {Line}, column {Column} in {FilePath}")]
    private static partial void LogInvalidPosition(ILogger logger, int line, int column, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No symbol found at position {Line}:{Column} in {FilePath}")]
    private static partial void LogNoSymbolAtPosition(ILogger logger, int line, int column, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Symbol {Symbol} has no source location")]
    private static partial void LogNoSourceLocation(ILogger logger, string symbol);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error resolving symbol at {FilePath}:{Line}:{Column}")]
    private static partial void LogResolveError(ILogger logger, Exception exception, string filePath, int line, int column);
}

/// <summary>
/// Interface for symbol resolution service
/// </summary>
public interface ISymbolResolutionService
{
    Task<ResolvedSymbol?> ResolveSymbolAtPositionAsync(
        string filePath,
        int line,
        int column,
        SymbolResolutionOptions? options = null);

    Task<ISymbol?> ResolveSymbolByNameAsync(
        string symbolName,
        string? containingType = null,
        string? containingNamespace = null);
}
