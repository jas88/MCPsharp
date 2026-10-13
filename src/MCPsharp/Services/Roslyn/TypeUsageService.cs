using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for analyzing type usage across the codebase
/// </summary>
public partial class TypeUsageService : ITypeUsageService
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
        // Find the type symbol in any project of the solution
        var typeSymbol = _workspace.FindSourceSymbols(typeName, SymbolFilter.Type, cancellationToken).OfType<INamedTypeSymbol>().FirstOrDefault();
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
        // Find the type symbol by full name
        var typeSymbol = _workspace.FindSourceSymbols(
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
        if (!_workspace.IsInitialized)
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

        var typeSymbol = _workspace.FindSourceSymbols(typeName, SymbolFilter.Type, cancellationToken)
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

            // Use the semantic model of the document's own project so derived types in other projects resolve
            var semanticModel = await _workspace.GetSemanticModelAsync(document, cancellationToken);
            if (semanticModel == null)
                continue;

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

                var semanticModel = await _workspace.GetSemanticModelAsync(document, cancellationToken);
                if (semanticModel == null)
                    continue;

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
}
