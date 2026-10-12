using MCPsharp.Models.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Method symbol lookup and method signature construction.
/// </summary>
public partial class CallChainService
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
                if (symbol != null)
                {
                    methods.Add(CreateMethodSignature(symbol));
                }
            }
        }

        return methods;
    }

    private async Task<CallChainResult?> FindCallChainsByNameAsync(string methodName, string? containingType, CallDirection direction, int maxDepth, CancellationToken cancellationToken)
    {
        var symbol = FindMethodSymbol(methodName, containingType, cancellationToken);
        if (symbol == null)
            return null;

        return await FindCallChainsAsync(symbol, direction, maxDepth, cancellationToken);
    }

    private IMethodSymbol? FindMethodSymbol(string methodName, string? containingType, CancellationToken cancellationToken)
    {
        // Find all methods with the given name in every project of the solution
        var methodSymbols = _workspace.FindSourceSymbols(methodName, SymbolFilter.Member, cancellationToken)
            .OfType<IMethodSymbol>();

        if (string.IsNullOrEmpty(containingType))
            return methodSymbols.FirstOrDefault();

        // Filter by containing type
        var matchingSymbol = methodSymbols.FirstOrDefault(m =>
            m.ContainingType?.Name == containingType ||
            m.ContainingType?.ToDisplayString().EndsWith(containingType, StringComparison.Ordinal) == true ||
            m.ContainingType?.ToDisplayString().Contains(containingType) == true);

        return matchingSymbol;
    }

    private static CallType DetermineCallType(SyntaxNode node)
    {
        if (node is InvocationExpressionSyntax)
            return CallType.Direct;

        return CallType.Unknown;
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

    private static MethodSignature CreateMethodSignatureFromString(string methodKey)
    {
        var parts = methodKey.Split('.');
        var methodName = parts.LastOrDefault() ?? "";
        var containingType = string.Join(".", parts.Take(parts.Length - 1));

        return new MethodSignature
        {
            Name = methodName,
            ContainingType = containingType,
            ReturnType = "void",
            Accessibility = "public"
        };
    }

    private sealed class MethodCallInfo
    {
        public required MethodSignature Signature { get; init; }
        public required string File { get; init; }
        public required int Line { get; init; }
        public required int Column { get; init; }
        public required CallType CallType { get; init; }
        public required ConfidenceLevel Confidence { get; init; }
        public required string CallExpression { get; init; }
        public required string Context { get; init; }
    }
}
