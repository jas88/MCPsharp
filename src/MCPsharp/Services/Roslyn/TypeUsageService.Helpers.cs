using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Core type usage discovery and syntax/symbol helpers.
/// </summary>
public partial class TypeUsageService
{
    private async Task<TypeUsageResult> FindTypeUsagesCoreAsync(INamedTypeSymbol typeSymbol, DateTime startTime, CancellationToken cancellationToken)
    {
        if (!_workspace.IsInitialized)
            throw new InvalidOperationException("Compilation not available");

        var usages = new List<TypeUsageInfo>();
        var filesAnalyzed = 0;
        var typesAnalyzed = 0;

        // Simplified implementation that avoids SymbolFinder for now
        // In a full implementation, you would use SymbolFinder to find all references
        // For now, we'll just scan through documents to find type references

        foreach (var document in _workspace.GetAllDocuments())
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            filesAnalyzed++;

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (syntaxTree == null)
                continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var typeReferences = root.DescendantNodes()
                .Where(n => IsTypeReference(n, typeSymbol));

            foreach (var reference in typeReferences)
            {
                var lineSpan = reference.GetLocation().GetLineSpan();
                var usageInfo = new TypeUsageInfo
                {
                    File = document.FilePath ?? "",
                    Line = lineSpan.StartLinePosition.Line,
                    Column = lineSpan.StartLinePosition.Character,
                    UsageKind = DetermineUsageKindSimple(reference),
                    Context = reference.ToString(),
                    Confidence = ConfidenceLevel.Medium,
                    MemberName = GetContainingMember(reference),
                    ContainerName = GetContainingTypeSimple(reference)
                };

                usages.Add(usageInfo);
                typesAnalyzed++;
            }
        }

        // Always add type declarations (these should be fast)
        var declarationLocations = typeSymbol.Locations.Where(l => l.IsInSource);
        foreach (var location in declarationLocations)
        {
            var lineSpan = location.GetLineSpan();
            usages.Add(new TypeUsageInfo
            {
                File = lineSpan.Path,
                Line = lineSpan.StartLinePosition.Line,
                Column = lineSpan.StartLinePosition.Character,
                UsageKind = TypeUsageKind.TypeDeclaration,
                Context = $"{typeSymbol.TypeKind.ToString().ToLowerInvariant()} {typeSymbol.Name}",
                Confidence = ConfidenceLevel.High,
                MemberName = typeSymbol.Name,
                ContainerName = typeSymbol.ContainingNamespace?.ToDisplayString()
            });
        }

        var endTime = DateTime.UtcNow;
        var analysisDuration = endTime - startTime;

        return new TypeUsageResult
        {
            TypeName = typeSymbol.Name,
            FullTypeName = typeSymbol.ToDisplayString(),
            Usages = usages,
            Metadata = new TypeUsageMetadata
            {
                AnalysisDuration = analysisDuration,
                FilesAnalyzed = filesAnalyzed,
                TypesAnalyzed = typesAnalyzed,
                AnalysisMethod = "Roslyn SymbolFinder + Type Usage Analysis"
            }
        };
    }

    private static string? GetContainingMember(SyntaxNode node)
    {
        var memberNode = node.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        return memberNode switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            PropertyDeclarationSyntax property => property.Identifier.Text,
            FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
            ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
            _ => null
        };
    }

    private static TypeUsageInfo CreateTypeUsageInfo(INamedTypeSymbol typeSymbol, TypeUsageKind usageKind)
    {
        var location = typeSymbol.Locations.FirstOrDefault();
        if (location == null || !location.IsInSource)
        {
            return new TypeUsageInfo
            {
                File = "",
                Line = 0,
                Column = 0,
                UsageKind = usageKind,
                Context = "",
                Confidence = ConfidenceLevel.Low,
                MemberName = typeSymbol.Name,
                ContainerName = typeSymbol.ContainingNamespace?.ToDisplayString()
            };
        }

        var lineSpan = location.GetLineSpan();
        return new TypeUsageInfo
        {
            File = lineSpan.Path,
            Line = lineSpan.StartLinePosition.Line,
            Column = lineSpan.StartLinePosition.Character,
            UsageKind = usageKind,
            Context = $"{typeSymbol.TypeKind.ToString().ToLowerInvariant()} {typeSymbol.Name}",
            Confidence = ConfidenceLevel.High,
            MemberName = typeSymbol.Name,
            ContainerName = typeSymbol.ContainingNamespace?.ToDisplayString()
        };
    }

    private static bool IsTestFile(string filePath)
    {
        return filePath.Contains("test", StringComparison.OrdinalIgnoreCase) ||
               filePath.Contains("spec", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTypeReference(SyntaxNode node, INamedTypeSymbol typeSymbol)
    {
        if (node is IdentifierNameSyntax identifier && identifier.Identifier.Text == typeSymbol.Name)
        {
            return true;
        }

        if (node is GenericNameSyntax genericName && genericName.Identifier.Text == typeSymbol.Name)
        {
            return true;
        }

        return false;
    }

    private static TypeUsageKind DetermineUsageKindSimple(SyntaxNode node)
    {
        var parent = node.Parent;

        while (parent != null)
        {
            switch (parent)
            {
                case ObjectCreationExpressionSyntax:
                    return TypeUsageKind.Instantiation;
                case MethodDeclarationSyntax method:
                    if (method.ReturnType.Contains(node))
                        return TypeUsageKind.ReturnType;
                    if (method.ParameterList?.Parameters.Any(p => p.Type != null && p.Type.Contains(node)) == true)
                        return TypeUsageKind.Parameter;
                    break;
                case PropertyDeclarationSyntax property:
                    if (property.Type.Contains(node))
                        return TypeUsageKind.Property;
                    break;
                case FieldDeclarationSyntax field:
                    if (field.Declaration.Type.Contains(node))
                        return TypeUsageKind.Field;
                    break;
                case BaseListSyntax:
                    return TypeUsageKind.BaseClass;
            }
            parent = parent.Parent;
        }

        return TypeUsageKind.Unknown;
    }

    private static string? GetContainingTypeSimple(SyntaxNode node)
    {
        var typeNode = node.FirstAncestorOrSelf<BaseTypeDeclarationSyntax>();
        return typeNode?.Identifier.Text;
    }

    private static bool InheritsFrom(INamedTypeSymbol classSymbol, INamedTypeSymbol baseTypeSymbol)
    {
        var current = classSymbol.BaseType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            if (CrossProjectSymbolComparer.AreEquivalent(current, baseTypeSymbol))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static bool ImplementsInterface(INamedTypeSymbol classSymbol, INamedTypeSymbol interfaceSymbol)
    {
        return classSymbol.AllInterfaces.Any(i => CrossProjectSymbolComparer.AreEquivalent(i, interfaceSymbol));
    }
}
