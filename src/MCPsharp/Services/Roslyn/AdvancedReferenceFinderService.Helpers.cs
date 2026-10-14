using MCPsharp.Models.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Method/type enumeration, signature matching and string similarity helpers.
/// </summary>
public partial class AdvancedReferenceFinderService
{
    private async Task<List<MethodSignature>> GetAllMethodsAsync(CancellationToken cancellationToken)
    {
        var methods = new List<MethodSignature>();
        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            var semanticModel = _workspace.GetSemanticModel(syntaxTree);
            if (semanticModel == null)
                continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var methodDeclarations = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(methodDecl, cancellationToken);
                if (symbol is IMethodSymbol methodSymbol)
                {
                    methods.Add(CreateMethodSignature(methodSymbol));
                }
            }
        }

        return methods;
    }

    private async Task<List<string>> GetAllTypesAsync(CancellationToken cancellationToken)
    {
        var types = new List<string>();
        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            var semanticModel = _workspace.GetSemanticModel(syntaxTree);
            if (semanticModel == null)
                continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var typeDeclarations = root.DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>();

            foreach (var typeDecl in typeDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
                if (symbol != null)
                {
                    types.Add(symbol.Name);
                }
            }
        }

        return types;
    }

    private async Task<IMethodSymbol?> GetMethodSymbol(string? filePath, int line)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        var document = _workspace.GetDocument(filePath);
        if (document == null)
            return null;

        var symbolInfo = await _symbolQuery.GetSymbolAtLocationAsync(filePath, line, 0);
        return symbolInfo?.Symbol as IMethodSymbol;
    }

    private static MethodSignature CreateMethodSignature(IMethodSymbol symbol)
    {
        var parameters = symbol.Parameters.Select(p => new ParameterInfo
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
            Name = symbol.Name,
            ReturnType = symbol.ReturnType.ToDisplayString(),
            Parameters = parameters,
            ContainingType = symbol.ContainingType?.ToDisplayString() ?? "",
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            IsStatic = symbol.IsStatic,
            IsVirtual = symbol.IsVirtual,
            IsAbstract = symbol.IsAbstract,
            IsOverride = symbol.IsOverride,
            IsExtension = symbol.IsExtensionMethod,
            IsAsync = symbol.IsAsync,
            FullyQualifiedName = symbol.ToDisplayString()
        };
    }

    private static bool MatchesSignature(MethodSignature signature1, MethodSignature signature2, bool exactMatch)
    {
        if (signature1.Name != signature2.Name)
            return false;

        if (signature1.Parameters.Count != signature2.Parameters.Count)
            return false;

        for (int i = 0; i < signature1.Parameters.Count; i++)
        {
            var param1 = signature1.Parameters[i];
            var param2 = signature2.Parameters[i];

            if (exactMatch)
            {
                if (param1.Type != param2.Type ||
                    param1.IsOptional != param2.IsOptional ||
                    param1.IsOut != param2.IsOut ||
                    param1.IsRef != param2.IsRef ||
                    param1.IsParams != param2.IsParams)
                    return false;
            }
            else
            {
                // Allow compatible types
                if (!AreTypesCompatible(param1.Type, param2.Type))
                    return false;
            }
        }

        return true;
    }

    private static bool AreTypesCompatible(string type1, string type2)
    {
        // Simple compatibility check
        if (type1 == type2)
            return true;

        if (CompatibleBaseTypes.Contains(type1) && CompatibleBaseTypes.Contains(type2))
            return true;

        // Handle generic types
        if (type1.Contains('<') && type2.Contains('<'))
        {
            var generic1 = type1.Substring(0, type1.IndexOf('<'));
            var generic2 = type2.Substring(0, type2.IndexOf('<'));
            return generic1 == generic2;
        }

        return false;
    }

    private static double CalculateSimilarity(string str1, string str2)
    {
        // Simple Levenshtein distance based similarity
        var distance = CalculateLevenshteinDistance(str1, str2);
        var maxLength = Math.Max(str1.Length, str2.Length);
        return maxLength == 0 ? 1.0 : 1.0 - (double)distance / maxLength;
    }

    private static int CalculateLevenshteinDistance(string str1, string str2)
    {
        var matrix = new int[str1.Length + 1, str2.Length + 1];

        for (int i = 0; i <= str1.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= str2.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= str1.Length; i++)
        {
            for (int j = 1; j <= str2.Length; j++)
            {
                var cost = str1[i - 1] == str2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[str1.Length, str2.Length];
    }

    private async Task<List<TypeUsageInfo>> FindGenericUsagesAsync(string typeName, CancellationToken cancellationToken)
    {
        return await _typeUsage.FindGenericUsagesAsync(typeName, cancellationToken);
    }
}
