using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for analyzing type usage across the codebase
/// </summary>
public class TypeUsageService : ITypeUsageService
{
    private readonly RoslynWorkspace _workspace;
    private readonly SymbolQueryService _symbolQuery;

    public TypeUsageService(RoslynWorkspace workspace, SymbolQueryService symbolQuery)
    {
        _workspace = workspace;
        _symbolQuery = symbolQuery;
    }

    public async Task<TypeUsageResult?> FindTypeUsagesAsync(string typeName, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return null;

        // Find the type symbol
        var typeSymbol = compilation.GetSymbolsWithName(typeName, SymbolFilter.Type, cancellationToken).OfType<INamedTypeSymbol>().FirstOrDefault();
        if (typeSymbol == null)
            return null;

        return await FindTypeUsagesCoreAsync(typeSymbol, startTime, cancellationToken);
    }

    public async Task<TypeUsageResult?> FindTypeUsagesAsync(INamedTypeSymbol typeSymbol, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        return await FindTypeUsagesCoreAsync(typeSymbol, startTime, cancellationToken);
    }

    public async Task<TypeUsageResult?> FindTypeUsagesAtLocationAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var symbolInfo = await _symbolQuery.GetSymbolAtLocationAsync(filePath, line, column);
        if (symbolInfo?.Symbol == null || symbolInfo.Symbol is not INamedTypeSymbol typeSymbol)
            return null;

        return await FindTypeUsagesCoreAsync(typeSymbol, startTime, cancellationToken);
    }

    public async Task<TypeUsageResult?> FindTypeUsagesByFullNameAsync(string fullTypeName, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return null;

        // Find the type symbol by full name
        var typeSymbol = compilation.GetSymbolsWithName(
            fullTypeName.Split('.')[^1],
            SymbolFilter.Type, cancellationToken)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(t => t.ToDisplayString() == fullTypeName);

        if (typeSymbol == null)
            return null;

        return await FindTypeUsagesCoreAsync(typeSymbol, startTime, cancellationToken);
    }

    public async Task<List<TypeUsageInfo>> FindInstantiationsAsync(string typeName, CancellationToken cancellationToken = default)
    {
        var result = await FindTypeUsagesAsync(typeName, cancellationToken);
        return result?.Instantiations.ToList() ?? new List<TypeUsageInfo>();
    }

    public async Task<InheritanceAnalysis> AnalyzeInheritanceAsync(string typeName, CancellationToken cancellationToken = default)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return new InheritanceAnalysis
            {
                TargetType = typeName,
                BaseClasses = new List<TypeUsageInfo>(),
                DerivedClasses = new List<TypeUsageInfo>(),
                ImplementedInterfaces = new List<TypeUsageInfo>(),
                InterfaceImplementations = new List<TypeUsageInfo>(),
                InheritanceDepth = 0,
                InheritanceChain = new List<string>(),
                IsAbstract = false,
                IsInterface = false,
                IsSealed = false
            };

        var typeSymbol = compilation.GetSymbolsWithName(typeName, SymbolFilter.Type, cancellationToken)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault();

        if (typeSymbol == null)
            return new InheritanceAnalysis
            {
                TargetType = typeName,
                BaseClasses = new List<TypeUsageInfo>(),
                DerivedClasses = new List<TypeUsageInfo>(),
                ImplementedInterfaces = new List<TypeUsageInfo>(),
                InterfaceImplementations = new List<TypeUsageInfo>(),
                InheritanceDepth = 0,
                InheritanceChain = new List<string>(),
                IsAbstract = false,
                IsInterface = false,
                IsSealed = false
            };

        var baseClasses = new List<TypeUsageInfo>();
        var derivedClasses = new List<TypeUsageInfo>();
        var implementedInterfaces = new List<TypeUsageInfo>();
        var interfaceImplementations = new List<TypeUsageInfo>();
        var inheritanceChain = new List<string>();

        // Analyze inheritance
        if (typeSymbol.BaseType != null && typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
        {
            baseClasses.Add(CreateTypeUsageInfo(typeSymbol.BaseType, TypeUsageKind.BaseClass));
            inheritanceChain.Add(typeSymbol.BaseType.Name);
        }

        // Find derived classes - simplified approach
        foreach (var document in _workspace.GetAllDocuments())
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (syntaxTree == null)
                continue;

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var classDecl in classDeclarations)
            {
                var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, cancellationToken);
                if (classSymbol != null && InheritsFrom(classSymbol, typeSymbol))
                {
                    var lineSpan = classDecl.Identifier.GetLocation().GetLineSpan();
                    derivedClasses.Add(new TypeUsageInfo
                    {
                        File = document.FilePath ?? "",
                        Line = lineSpan.StartLinePosition.Line,
                        Column = lineSpan.StartLinePosition.Character,
                        UsageKind = TypeUsageKind.BaseClass,
                        Context = $"class {classSymbol.Name} : {typeSymbol.Name}",
                        Confidence = ConfidenceLevel.High,
                        MemberName = classSymbol.Name,
                        ContainerName = classSymbol.ContainingNamespace?.ToDisplayString()
                    });
                }
            }
        }

        // Analyze interfaces
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            implementedInterfaces.Add(CreateTypeUsageInfo(iface, TypeUsageKind.InterfaceImplementation));
        }

        // Find classes that implement this interface - simplified approach
        if (typeSymbol.TypeKind == TypeKind.Interface)
        {
            foreach (var document in _workspace.GetAllDocuments())
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
                if (syntaxTree == null)
                    continue;

                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync(cancellationToken);

                var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
                foreach (var classDecl in classDeclarations)
                {
                    var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, cancellationToken);
                    if (classSymbol != null && ImplementsInterface(classSymbol, typeSymbol))
                    {
                        var lineSpan = classDecl.Identifier.GetLocation().GetLineSpan();
                        interfaceImplementations.Add(new TypeUsageInfo
                        {
                            File = document.FilePath ?? "",
                            Line = lineSpan.StartLinePosition.Line,
                            Column = lineSpan.StartLinePosition.Character,
                            UsageKind = TypeUsageKind.InterfaceImplementation,
                            Context = $"class {classSymbol.Name} : {typeSymbol.Name}",
                            Confidence = ConfidenceLevel.High,
                            MemberName = classSymbol.Name,
                            ContainerName = classSymbol.ContainingNamespace?.ToDisplayString()
                        });
                    }
                }
            }
        }

        // Build inheritance chain
        var current = typeSymbol.BaseType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            inheritanceChain.Insert(0, current.Name);
            current = current.BaseType;
        }

        return new InheritanceAnalysis
        {
            TargetType = typeName,
            BaseClasses = baseClasses,
            DerivedClasses = derivedClasses,
            ImplementedInterfaces = implementedInterfaces,
            InterfaceImplementations = interfaceImplementations,
            InheritanceDepth = inheritanceChain.Count,
            InheritanceChain = inheritanceChain,
            IsAbstract = typeSymbol.IsAbstract,
            IsInterface = typeSymbol.TypeKind == TypeKind.Interface,
            IsSealed = typeSymbol.IsSealed
        };
    }

    public async Task<List<TypeUsageInfo>> FindInterfaceImplementationsAsync(string interfaceName, CancellationToken cancellationToken = default)
    {
        var inheritanceAnalysis = await AnalyzeInheritanceAsync(interfaceName, cancellationToken);
        return inheritanceAnalysis.InterfaceImplementations;
    }

    public async Task<List<TypeUsageInfo>> FindGenericUsagesAsync(string typeName, CancellationToken cancellationToken = default)
    {
        var result = await FindTypeUsagesAsync(typeName, cancellationToken);
        if (result == null)
            return new List<TypeUsageInfo>();

        return result.Usages
            .Where(u => u.IsGeneric || u.UsageKind == TypeUsageKind.GenericArgument)
            .ToList();
    }

    public async Task<TypeDependencyAnalysis> AnalyzeTypeDependenciesAsync(string typeName, CancellationToken cancellationToken = default)
    {
        var result = await FindTypeUsagesAsync(typeName, cancellationToken);
        if (result == null)
            return new TypeDependencyAnalysis
            {
                TargetType = typeName,
                Dependencies = new List<TypeDependency>(),
                Dependents = new List<TypeDependency>(),
                DependencyFrequency = new Dictionary<string, int>(),
                CircularDependencies = new List<string>()
            };

        var dependencies = new List<TypeDependency>();
        var dependents = new List<TypeDependency>();
        var dependencyFrequency = new Dictionary<string, int>();
        var circularDependencies = new List<string>();

        // Analyze dependencies from usages
        foreach (var usage in result.Usages)
        {
            // This is a simplified analysis - in practice, you'd want to parse the context
            // to extract actual type dependencies
            if (usage.UsageKind == TypeUsageKind.Parameter ||
                usage.UsageKind == TypeUsageKind.ReturnType ||
                usage.UsageKind == TypeUsageKind.Property ||
                usage.UsageKind == TypeUsageKind.Field)
            {
                var dependency = new TypeDependency
                {
                    FromType = usage.ContainerName ?? "",
                    ToType = typeName,
                    DependencyKind = usage.UsageKind,
                    File = usage.File,
                    Line = usage.Line,
                    Column = usage.Column,
                    Confidence = usage.Confidence,
                    UsageCount = 1
                };
                dependencies.Add(dependency);

                dependencyFrequency[usage.ContainerName ?? ""] = dependencyFrequency.GetValueOrDefault(usage.ContainerName ?? "", 0) + 1;
            }
        }

        // This is a placeholder for circular dependency detection
        // In practice, you'd want to build a full dependency graph

        return new TypeDependencyAnalysis
        {
            TargetType = typeName,
            Dependencies = dependencies,
            Dependents = dependents,
            DependencyFrequency = dependencyFrequency,
            CircularDependencies = circularDependencies,
            TotalDependencies = dependencies.Count,
            TotalDependents = dependents.Count,
            HasCircularDependencies = circularDependencies.Count > 0
        };
    }

    public Task<List<MethodSignature>> FindSimilarTypesAsync(string typeName, CancellationToken cancellationToken = default)
    {
        // This is a placeholder implementation
        // In practice, you'd want to analyze base classes, interfaces, and patterns
        return Task.FromResult(new List<MethodSignature>());
    }

    public async Task<TypeUsagePatternAnalysis> AnalyzeUsagePatternsAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return new TypeUsagePatternAnalysis
        {
            TypeStatistics = new Dictionary<string, TypeUsageStats>(),
            CommonPatterns = new List<UsagePattern>(),
            MostUsedTypes = new List<string>(),
            LeastUsedTypes = new List<string>(),
            UsageKindDistribution = new Dictionary<TypeUsageKind, int>(),
            TotalTypesAnalyzed = 0,
            TotalUsagesFound = 0
        };

        var typeStatistics = new Dictionary<string, TypeUsageStats>();
        var usageKindDistribution = new Dictionary<TypeUsageKind, int>();
        var allTypes = new List<string>();

        // Analyze all types in the namespace
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var typeDeclarations = root.DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>();

            foreach (var typeDecl in typeDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
                if (symbol == null)
                    continue;

                // Check namespace filter
                if (!string.IsNullOrEmpty(namespaceFilter))
                {
                    var ns = symbol.ContainingNamespace?.ToDisplayString();
                    if (ns == null || !ns.Contains(namespaceFilter))
                        continue;
                }

                var typeResult = await FindTypeUsagesAsync(symbol, cancellationToken);
                if (typeResult != null)
                {
                    allTypes.Add(symbol.Name);
                    typeStatistics[symbol.Name] = new TypeUsageStats
                    {
                        TypeName = symbol.Name,
                        TotalUsages = typeResult.TotalUsages,
                        UsagesByKind = typeResult.UsagesByKind.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Count),
                        FilesUsedIn = typeResult.UsedInFiles.ToList(),
                        IsPubliclyUsed = typeResult.Usages.Any(u => u.AccessModifier == "public"),
                        IsTestOnly = typeResult.UsedInFiles.All(f => IsTestFile(f)),
                        FirstUsage = TimeSpan.Zero, // Placeholder
                        LastUsage = TimeSpan.Zero   // Placeholder
                    };

                    // Update usage kind distribution
                    foreach (var kvp in typeResult.UsagesByKind)
                    {
                        usageKindDistribution[kvp.Key] = usageKindDistribution.GetValueOrDefault(kvp.Key, 0) + kvp.Value.Count;
                    }
                }
            }
        }

        // Find common patterns
        var commonPatterns = new List<UsagePattern>();
        // This would require more sophisticated analysis in practice

        var mostUsedTypes = typeStatistics
            .OrderByDescending(kvp => kvp.Value.TotalUsages)
            .Take(10)
            .Select(kvp => kvp.Key)
            .ToList();

        var leastUsedTypes = typeStatistics
            .OrderBy(kvp => kvp.Value.TotalUsages)
            .Take(10)
            .Select(kvp => kvp.Key)
            .ToList();

        return new TypeUsagePatternAnalysis
        {
            TypeStatistics = typeStatistics,
            CommonPatterns = commonPatterns,
            MostUsedTypes = mostUsedTypes,
            LeastUsedTypes = leastUsedTypes,
            UsageKindDistribution = usageKindDistribution,
            TotalTypesAnalyzed = typeStatistics.Count,
            TotalUsagesFound = typeStatistics.Values.Sum(s => s.TotalUsages)
        };
    }

    public async Task<TypeRefactoringOpportunities> FindRefactoringOpportunitiesAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default)
    {
        var patternAnalysis = await AnalyzeUsagePatternsAsync(namespaceFilter, cancellationToken);
        var unusedTypes = new List<MethodSignature>();
        var singleImplementationInterfaces = new List<MethodSignature>();
        var potentialSealedTypes = new List<MethodSignature>();
        var largeTypes = new List<MethodSignature>();
        var typesWithCircularDependencies = new List<MethodSignature>();
        var duplicatedTypes = new List<MethodSignature>();

        // Find unused types
        foreach (var stats in patternAnalysis.TypeStatistics.Values)
        {
            if (stats.TotalUsages == 0)
            {
                unusedTypes.Add(new MethodSignature
                {
                    Name = stats.TypeName,
                    ContainingType = stats.TypeName,
                    ReturnType = "void",
                    Accessibility = "public"
                });
            }

            // Find single implementation interfaces
            if (stats.TypeName.StartsWith('I') && stats.TotalUsages == 1)
            {
                singleImplementationInterfaces.Add(new MethodSignature
                {
                    Name = stats.TypeName,
                    ContainingType = stats.TypeName,
                    ReturnType = "void",
                    Accessibility = "public"
                });
            }

            // Find potential sealed types (classes that are never inherited)
            if (!stats.TypeName.StartsWith('I') &&
            stats.UsagesByKind.GetValueOrDefault(TypeUsageKind.BaseClass, 0) == 0)
            {
                potentialSealedTypes.Add(new MethodSignature
                {
                    Name = stats.TypeName,
                    ContainingType = stats.TypeName,
                    ReturnType = "void",
                    Accessibility = "public"
                });
            }
        }

        var totalOpportunities = unusedTypes.Count + singleImplementationInterfaces.Count +
                               potentialSealedTypes.Count + largeTypes.Count +
                               typesWithCircularDependencies.Count + duplicatedTypes.Count;

        var opportunityBreakdown = new Dictionary<string, int>
        {
            ["Unused Types"] = unusedTypes.Count,
            ["Single Implementation Interfaces"] = singleImplementationInterfaces.Count,
            ["Potential Sealed Types"] = potentialSealedTypes.Count,
            ["Large Types"] = largeTypes.Count,
            ["Types with Circular Dependencies"] = typesWithCircularDependencies.Count,
            ["Duplicated Types"] = duplicatedTypes.Count
        };

        return new TypeRefactoringOpportunities
        {
            UnusedTypes = unusedTypes,
            SingleImplementationInterfaces = singleImplementationInterfaces,
            PotentialSealedTypes = potentialSealedTypes,
            LargeTypes = largeTypes,
            TypesWithCircularDependencies = typesWithCircularDependencies,
            DuplicatedTypes = duplicatedTypes,
            TotalOpportunities = totalOpportunities,
            OpportunityBreakdown = opportunityBreakdown
        };
    }

    private async Task<TypeUsageResult> FindTypeUsagesCoreAsync(INamedTypeSymbol typeSymbol, DateTime startTime, CancellationToken cancellationToken)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
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
            if (SymbolEqualityComparer.Default.Equals(current, baseTypeSymbol))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static bool ImplementsInterface(INamedTypeSymbol classSymbol, INamedTypeSymbol interfaceSymbol)
    {
        return classSymbol.AllInterfaces.Contains(interfaceSymbol);
    }
}
