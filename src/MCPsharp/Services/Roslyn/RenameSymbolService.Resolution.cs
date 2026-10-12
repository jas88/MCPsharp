using MCPsharp.Services.Roslyn.SymbolSearch;
using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Locating the symbol to rename and checking it matches the request.
/// </summary>
public partial class RenameSymbolService
{
    /// <summary>
    /// Find symbol using the new unified symbol finder service.
    /// This replaces the old ad-hoc search logic with a clean strategy pattern.
    /// </summary>
    private async Task<ISymbol?> FindSymbolAsync(RenameRequest request, CancellationToken ct)
    {
        // Convert RenameRequest to SymbolSearchRequest
        var searchRequest = new SymbolSearchRequest
        {
            Name = request.OldName,
            SymbolKind = request.SymbolKind,
            ContainingType = request.ContainingType,
            FilePath = request.FilePath,
            Line = request.Line,
            Column = request.Column
        };

        // Use the new unified finder service
        var result = await _symbolFinder.FindSymbolAsync(searchRequest, ct);

        var foundSymbol = result.Symbol;
        if (foundSymbol != null)
        {
            // Additional validation specific to rename operations
            if (!ValidateSymbolMatch(foundSymbol, request))
            {
                LogSymbolFailedValidation(_logger, foundSymbol.Name);
                return null;
            }

            LogFoundSymbol(_logger, foundSymbol.Name, foundSymbol.Kind, result.UsedStrategy);

            if (result.IsAmbiguous)
            {
                LogAmbiguousCandidates(_logger, result.Candidates.Count, request.OldName);
            }

            return foundSymbol;
        }

        LogSymbolNotFound(_logger, request.OldName, request.SymbolKind);
        return null;
    }

    /// <summary>
    /// Count occurrences of a substring in a string
    /// </summary>
    private static int CountOccurrences(string text, string substring)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(substring))
            return 0;

        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    private async Task<ISymbol?> ResolveSymbolInCurrentCompilationAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken ct)
    {
        if (symbol == null || solution == null)
        {
            return null;
        }

        try
        {
            // Get the symbol's primary source location
            var sourceLocation = symbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (sourceLocation == null)
            {
                LogNoSourceLocation(_logger);
                return null;
            }

            // Find the document containing this symbol
            var filePath = sourceLocation.GetLineSpan().Path;
            var document = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == filePath);

            if (document == null)
            {
                LogDocumentNotFound(_logger, filePath);
                return null;
            }

            // Get the current semantic model and syntax tree
            var semanticModel = await document.GetSemanticModelAsync(ct);
            var syntaxRoot = await document.GetSyntaxRootAsync(ct);

            if (semanticModel == null || syntaxRoot == null)
            {
                LogNoSemanticModelOrRoot(_logger);
                return null;
            }

            // Find the syntax node at the symbol's location
            // We need to find the declaration node, not just any node at the location
            var node = syntaxRoot.FindNode(sourceLocation.SourceSpan);
            if (node == null)
            {
                LogSyntaxNodeNotFound(_logger);
                return null;
            }

            // For declarations, we need to find the parent declaration node
            // (e.g., ClassDeclarationSyntax, MethodDeclarationSyntax, etc.)
            var declarationNode = node.AncestorsAndSelf().FirstOrDefault(n =>
                semanticModel.GetDeclaredSymbol(n, ct) != null);

            if (declarationNode != null)
            {
                var declaredSymbol = semanticModel.GetDeclaredSymbol(declarationNode, ct);
                if (declaredSymbol != null)
                {
                    LogReResolvedViaDeclaration(_logger, declaredSymbol.Name, declaredSymbol.Kind);
                    return declaredSymbol;
                }
            }

            // If not a declaration, try to get the symbol info (for references like variables, parameters)
            var resolved = semanticModel.GetSymbolInfo(node, ct).Symbol;
            if (resolved != null)
            {
                LogReResolvedViaSymbolInfo(_logger, resolved.Name, resolved.Kind);
                return resolved;
            }

            LogCouldNotReResolve(_logger);
            return null;
        }
        catch (Exception ex)
        {
            LogReResolveError(_logger, ex);
            return null;
        }
    }

    private bool ValidateSymbolMatch(ISymbol symbol, RenameRequest request)
    {
        if (symbol == null || request == null)
        {
            return false;
        }

        // Check name match (case-insensitive for robustness)
        if (string.IsNullOrWhiteSpace(symbol.Name) ||
            !string.Equals(symbol.Name, request.OldName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check symbol kind
        if (request.SymbolKind != SymbolKind.Any && !MatchesSymbolKind(symbol, request.SymbolKind))
        {
            return false;
        }

        // Check containing type
        if (!string.IsNullOrEmpty(request.ContainingType))
        {
            var containingTypeName = symbol.ContainingType?.Name;
            if (string.IsNullOrEmpty(containingTypeName) ||
                !string.Equals(containingTypeName, request.ContainingType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Ensure symbol is in source code (not metadata)
        if (!symbol.Locations.Any(l => l != null && l.IsInSource))
        {
            LogSymbolNotInSource(_logger, symbol.Name);
            return false;
        }

        // Additional validation for specific symbol types
        if (!IsSymbolAccessibleForRename(symbol))
        {
            LogSymbolNotAccessible(_logger, symbol.Name);
            return false;
        }

        // Additional validation for constructor handling
        if (request.SymbolKind == SymbolKind.Class && symbol is IMethodSymbol methodSymbol)
        {
            // If we're looking for a class but found a constructor, check if it's the constructor for that class
            if (methodSymbol.MethodKind == MethodKind.Constructor && methodSymbol.ContainingType != null)
            {
                return string.Equals(methodSymbol.ContainingType.Name, request.OldName, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        // Special handling for type parameters - check if the name matches the pattern
        if (request.SymbolKind == SymbolKind.TypeParameter && symbol is not ITypeParameterSymbol)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesSymbolKind(ISymbol symbol, SymbolKind requestedKind)
    {
        if (symbol == null)
            return false;

        return requestedKind switch
        {
            SymbolKind.Class => symbol is INamedTypeSymbol { TypeKind: TypeKind.Class } ||
                                // Handle constructors when looking for their class
                                (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } methodSymbol &&
                                 methodSymbol.ContainingType != null &&
                                 methodSymbol.ContainingType.TypeKind == TypeKind.Class),

            SymbolKind.Interface => symbol is INamedTypeSymbol { TypeKind: TypeKind.Interface },

            SymbolKind.Method => symbol is IMethodSymbol methodSymbol &&
                                methodSymbol.MethodKind != MethodKind.Constructor &&
                                methodSymbol.MethodKind != MethodKind.PropertyGet &&
                                methodSymbol.MethodKind != MethodKind.PropertySet,

            SymbolKind.Property => symbol is IPropertySymbol ||
                                  // Handle property accessors when looking for the property
                                  (symbol is IMethodSymbol methodSymbol &&
                                   (methodSymbol.MethodKind == MethodKind.PropertyGet ||
                                    methodSymbol.MethodKind == MethodKind.PropertySet) &&
                                   methodSymbol.AssociatedSymbol is IPropertySymbol),

            SymbolKind.Field => symbol is IFieldSymbol,

            SymbolKind.Parameter => symbol is IParameterSymbol,

            SymbolKind.Namespace => symbol is INamespaceSymbol,

            SymbolKind.Local => symbol is ILocalSymbol,

            SymbolKind.TypeParameter => symbol is ITypeParameterSymbol,

            _ => true // Any
        };
    }

    // NOTE: SelectBestCandidate, NormalizeSymbol, and CalculateCandidateScore removed
    // These methods are now handled by SymbolSelector in the SymbolFinderService.
    // The logic has been preserved and improved with better separation of concerns.
}
