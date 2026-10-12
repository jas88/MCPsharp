using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Method symbol resolution, signature matching and location conversion helpers.
/// </summary>
public partial class CallerAnalysisService
{
    private async Task<IMethodSymbol?> FindMethodSymbol(string methodName, string? containingType, CancellationToken cancellationToken)
    {
        // Try the symbol query approach first
        var symbols = await _symbolQuery.FindSymbolsAsync(methodName, "method");

        if (string.IsNullOrEmpty(containingType))
        {
            var firstSymbol = symbols.FirstOrDefault();
            return await GetMethodSymbolAsync(firstSymbol?.File, firstSymbol?.Line ?? 0, cancellationToken);
        }

        var matchingSymbol = symbols.FirstOrDefault(s =>
            s.ContainerName == containingType ||
            s.ContainerName?.EndsWith(containingType, StringComparison.Ordinal) == true ||
            s.ContainerName?.EndsWith("." + containingType, StringComparison.Ordinal) == true);

        if (matchingSymbol != null)
            return await GetMethodSymbolAsync(matchingSymbol.File, matchingSymbol.Line, cancellationToken);

        // Fallback: search every compilation in the solution directly
        var methodSymbols = _workspace.FindSourceSymbols(methodName, SymbolFilter.Member, cancellationToken)
            .OfType<IMethodSymbol>();

        if (!string.IsNullOrEmpty(containingType))
        {
            // Filter by containing type
            methodSymbols = methodSymbols.Where(m =>
                m.ContainingType?.Name == containingType ||
                m.ContainingType?.ToDisplayString().EndsWith(containingType, StringComparison.Ordinal) == true ||
                m.ContainingType?.ToDisplayString().EndsWith("." + containingType, StringComparison.Ordinal) == true);
        }

        return methodSymbols.FirstOrDefault();
    }

    private async Task<IMethodSymbol?> GetMethodSymbolAsync(string? filePath, int line, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        var document = _workspace.GetDocument(filePath);
        if (document == null)
            return null;

        var symbolInfo = await _symbolQuery.GetSymbolAtLocationAsync(filePath, line, 0);
        if (symbolInfo?.Symbol == null)
            return null;

        return await ConvertToMethodSymbol(symbolInfo.Symbol, filePath, line, 0, cancellationToken);
    }

    private async Task<IMethodSymbol?> ConvertToMethodSymbol(ISymbol symbol, string filePath, int line, int column, CancellationToken cancellationToken)
    {
        if (symbol is IMethodSymbol methodSymbol)
            return methodSymbol;

        // If it's a property or field, try to find its getter/setter
        if (symbol is IPropertySymbol propertySymbol)
        {
            var document = _workspace.GetDocument(filePath);
            if (document != null)
            {
                var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (syntaxTree == null || semanticModel == null)
                    return null;

                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var position = (await syntaxTree.GetTextAsync(cancellationToken)).Lines[line].Start + column;
                var node = root.FindToken(position).Parent;

                // Check if we're in a getter or setter context
                if (node?.FirstAncestorOrSelf<AccessorDeclarationSyntax>() is AccessorDeclarationSyntax accessor)
                {
                    return accessor.Keyword.Text switch
                    {
                        "get" => propertySymbol.GetMethod,
                        "set" => propertySymbol.SetMethod,
                        _ => null
                    };
                }
            }
        }

        return null;
    }

    private static bool MatchesSignature(IMethodSymbol symbol, MethodSignature signature, bool exactMatch)
    {
        if (symbol.Name != signature.Name)
            return false;

        // Check containing type if specified
        if (!string.IsNullOrEmpty(signature.ContainingType))
        {
            var symbolContainingType = symbol.ContainingType?.ToDisplayString();
            if (symbolContainingType != signature.ContainingType)
                return false;
        }

        // Check return type if specified
        if (!string.IsNullOrEmpty(signature.ReturnType))
        {
            var symbolReturnType = symbol.ReturnType?.ToDisplayString();
            if (symbolReturnType != signature.ReturnType)
                return false;
        }

        if (symbol.Parameters.Length != signature.Parameters.Count)
            return false;

        for (int i = 0; i < symbol.Parameters.Length; i++)
        {
            var symbolParam = symbol.Parameters[i];
            var signatureParam = signature.Parameters[i];

            if (exactMatch)
            {
                if (symbolParam.Type.ToDisplayString() != signatureParam.Type)
                    return false;
            }
            else
            {
                // Allow compatible types
                if (!AreTypesCompatible(symbolParam.Type.ToDisplayString(), signatureParam.Type))
                    return false;
            }
        }

        return true;
    }

    private static bool AreTypesCompatible(string type1, string type2)
    {
        // Simple compatibility check - could be enhanced
        return type1 == type2 ||
               (type1.Contains("IEnumerable") && type2.Contains("IEnumerable")) ||
               (type1 == "object") || (type2 == "object");
    }

    private static MethodSignature CreateMethodSignature(ISymbol symbol)
    {
        if (symbol is not IMethodSymbol methodSymbol)
            throw new ArgumentException("Symbol must be a method");

        var parameters = methodSymbol.Parameters.Select(p => new ParameterInfo
        {
            Name = p.Name,
            Type = p.Type.ToDisplayString(),
            IsOptional = p.IsOptional,
            IsOut = p.RefKind == RefKind.Out,
            IsRef = p.RefKind == RefKind.Ref,
            IsParams = p.IsParams,
            DefaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null,
            Position = p.Ordinal
        }).ToList();

        return new MethodSignature
        {
            Name = methodSymbol.Name,
            ReturnType = methodSymbol.ReturnType.ToDisplayString(),
            Parameters = parameters,
            ContainingType = methodSymbol.ContainingType?.ToDisplayString() ?? "",
            Accessibility = methodSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            IsStatic = methodSymbol.IsStatic,
            IsVirtual = methodSymbol.IsVirtual,
            IsAbstract = methodSymbol.IsAbstract,
            IsOverride = methodSymbol.IsOverride,
            IsExtension = methodSymbol.IsExtensionMethod,
            IsAsync = methodSymbol.IsAsync,
            FullyQualifiedName = methodSymbol.ToDisplayString()
        };
    }

    private static bool IsTestFile(string filePath)
    {
        return filePath.Contains("test", StringComparison.OrdinalIgnoreCase) ||
               filePath.Contains("spec", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts ReferenceLocation to SymbolLocation
    /// </summary>
    private static SymbolLocation ConvertToSymbolLocation(Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation referenceLocation)
    {
        var location = referenceLocation.Location;
        var lineSpan = location.GetLineSpan();

        return new SymbolLocation
        {
            Document = referenceLocation.Document,
            FilePath = lineSpan.Path,
            Line = lineSpan.StartLinePosition.Line,
            Column = lineSpan.StartLinePosition.Character,
            Location = location,
            TextSpan = location.SourceSpan
        };
    }
}
