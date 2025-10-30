using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Advanced service for comprehensive reference finding and analysis
/// </summary>
public class AdvancedReferenceFinderService
{
    private readonly RoslynWorkspace _workspace;
    private readonly SymbolQueryService _symbolQuery;
    private readonly ICallerAnalysisService _callerAnalysis;
    private readonly ICallChainService _callChain;
    private readonly ITypeUsageService _typeUsage;

    public AdvancedReferenceFinderService(
        RoslynWorkspace workspace,
        SymbolQueryService symbolQuery,
        ICallerAnalysisService callerAnalysis,
        ICallChainService callChain,
        ITypeUsageService typeUsage)
    {
        _workspace = workspace;
        _symbolQuery = symbolQuery;
        _callerAnalysis = callerAnalysis;
        _callChain = callChain;
        _typeUsage = typeUsage;
    }

    /// <summary>
    /// Find who calls a specific method
    /// </summary>
    public async Task<CallerResult?> FindCallersAsync(string methodName, string? containingType = null, bool includeIndirect = true, CancellationToken cancellationToken = default)
    {
        if (includeIndirect)
        {
            return await _callerAnalysis.FindCallersAsync(methodName, containingType, cancellationToken);
        }
        else
        {
            return await _callerAnalysis.FindDirectCallersAsync(methodName, containingType, cancellationToken);
        }
    }

    /// <summary>
    /// Find who calls a method at a specific location
    /// </summary>
    public async Task<CallerResult?> FindCallersAtLocationAsync(string filePath, int line, int column, bool includeIndirect = true, CancellationToken cancellationToken = default)
    {
        var result = await _callerAnalysis.FindCallersAtLocationAsync(filePath, line, column, cancellationToken);
        if (result == null || includeIndirect)
            return result;

        // Filter to direct callers only
        return new CallerResult
        {
            TargetSymbol = result.TargetSymbol,
            TargetSignature = result.TargetSignature,
            Callers = result.DirectCallers,
            AnalysisTime = result.AnalysisTime,
            Metadata = result.Metadata
        };
    }

    /// <summary>
    /// Find call chains leading to a specific method
    /// </summary>
    public async Task<CallChainResult?> FindCallChainsAsync(string methodName, string? containingType = null, CallDirection direction = CallDirection.Backward, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        return await _callChain.FindCallChainsAsync(methodName, containingType, maxDepth, cancellationToken);
    }

    /// <summary>
    /// Find call chains for a method at a specific location
    /// </summary>
    public async Task<CallChainResult?> FindCallChainsAtLocationAsync(string filePath, int line, int column, CallDirection direction = CallDirection.Backward, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        return await _callChain.FindCallChainsAtLocationAsync(filePath, line, column, direction, maxDepth, cancellationToken);
    }

    /// <summary>
    /// Find all usages of a specific type
    /// </summary>
    public async Task<TypeUsageResult?> FindTypeUsagesAsync(string typeName, CancellationToken cancellationToken = default)
    {
        return await _typeUsage.FindTypeUsagesAsync(typeName, cancellationToken);
    }

    /// <summary>
    /// Find type usages at a specific location
    /// </summary>
    public async Task<TypeUsageResult?> FindTypeUsagesAtLocationAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        return await _typeUsage.FindTypeUsagesAtLocationAsync(filePath, line, column, cancellationToken);
    }

    /// <summary>
    /// Analyze call patterns for a method
    /// </summary>
    public async Task<CallPatternAnalysis> AnalyzeCallPatternsAsync(string methodName, string? containingType = null, CancellationToken cancellationToken = default)
    {
        return await _callerAnalysis.AnalyzeCallPatternsAsync(methodName, containingType, cancellationToken);
    }

    /// <summary>
    /// Find call chains between two specific methods
    /// </summary>
    public async Task<List<CallChainPath>> FindCallChainsBetweenAsync(MethodSignature fromMethod, MethodSignature toMethod, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        return await _callChain.FindCallChainsBetweenAsync(fromMethod, toMethod, maxDepth, cancellationToken);
    }

    /// <summary>
    /// Find recursive call chains
    /// </summary>
    public async Task<List<CallChainPath>> FindRecursiveCallChainsAsync(string methodName, string? containingType = null, int maxDepth = 20, CancellationToken cancellationToken = default)
    {
        return await _callChain.FindRecursiveCallChainsAsync(methodName, containingType, maxDepth, cancellationToken);
    }

    /// <summary>
    /// Analyze inheritance relationships for a type
    /// </summary>
    public async Task<InheritanceAnalysis> AnalyzeInheritanceAsync(string typeName, CancellationToken cancellationToken = default)
    {
        return await _typeUsage.AnalyzeInheritanceAsync(typeName, cancellationToken);
    }

    /// <summary>
    /// Find methods that are never called
    /// </summary>
    public async Task<List<MethodSignature>> FindUnusedMethodsAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default)
    {
        return await _callerAnalysis.FindUnusedMethodsAsync(namespaceFilter, cancellationToken);
    }

    /// <summary>
    /// Find methods only called by tests (synchronous wrapper for tests)
    /// </summary>
    public async Task<List<MethodSignature>> FindTestOnlyMethods()
    {
        return await FindTestOnlyMethodsAsync();
    }

    /// <summary>
    /// Find methods only called by tests
    /// </summary>
    public async Task<List<MethodSignature>> FindTestOnlyMethodsAsync(CancellationToken cancellationToken = default)
    {
        return await _callerAnalysis.FindTestOnlyMethodsAsync(cancellationToken);
    }

    /// <summary>
    /// Analyze call graph for a specific type or namespace
    /// </summary>
    public async Task<CallGraphAnalysis> AnalyzeCallGraphAsync(string? typeName = null, string? namespaceName = null, CancellationToken cancellationToken = default)
    {
        return await _callChain.AnalyzeCallGraphAsync(typeName, namespaceName, cancellationToken);
    }

    /// <summary>
    /// Find circular dependencies in call chains
    /// </summary>
    public async Task<List<CircularDependency>> FindCircularDependenciesAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default)
    {
        return await _callChain.FindCircularDependenciesAsync(namespaceFilter, cancellationToken);
    }

    /// <summary>
    /// Get shortest path between two methods
    /// </summary>
    public async Task<CallChainPath?> FindShortestPathAsync(MethodSignature fromMethod, MethodSignature toMethod, CancellationToken cancellationToken = default)
    {
        return await _callChain.FindShortestPathAsync(fromMethod, toMethod, cancellationToken);
    }

    /// <summary>
    /// Find all methods reachable from a starting point
    /// </summary>
    public async Task<ReachabilityAnalysis> FindReachableMethodsAsync(string methodName, string? containingType = null, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        return await _callChain.FindReachableMethodsAsync(methodName, containingType, maxDepth, cancellationToken);
    }

    /// <summary>
    /// Find type dependencies
    /// </summary>
    public async Task<TypeDependencyAnalysis> AnalyzeTypeDependenciesAsync(string typeName, CancellationToken cancellationToken = default)
    {
        return await _typeUsage.AnalyzeTypeDependenciesAsync(typeName, cancellationToken);
    }

    /// <summary>
    /// Analyze usage patterns across types
    /// </summary>
    public async Task<TypeUsagePatternAnalysis> AnalyzeUsagePatternsAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default)
    {
        return await _typeUsage.AnalyzeUsagePatternsAsync(namespaceFilter, cancellationToken);
    }

    /// <summary>
    /// Find refactoring opportunities
    /// </summary>
    public async Task<TypeRefactoringOpportunities> FindRefactoringOpportunitiesAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default)
    {
        return await _typeUsage.FindRefactoringOpportunitiesAsync(namespaceFilter, cancellationToken);
    }

    /// <summary>
    /// Comprehensive analysis of a method including callers, call chains, and patterns
    /// </summary>
    public async Task<MethodComprehensiveAnalysis> AnalyzeMethodComprehensivelyAsync(string methodName, string? containingType = null, CancellationToken cancellationToken = default)
    {
        var callersTask = FindCallersAsync(methodName, containingType, cancellationToken: cancellationToken);
        var backwardChainsTask = FindCallChainsAsync(methodName, containingType, CallDirection.Backward, 5, cancellationToken);
        var forwardChainsTask = FindCallChainsAsync(methodName, containingType, CallDirection.Forward, 5, cancellationToken);
        var patternsTask = AnalyzeCallPatternsAsync(methodName, containingType, cancellationToken);
        var recursiveTask = FindRecursiveCallChainsAsync(methodName, containingType, 10, cancellationToken);

        var callers = await callersTask;
        var backwardChains = await backwardChainsTask;
        var forwardChains = await forwardChainsTask;
        var patterns = await patternsTask;
        var recursive = await recursiveTask;

        return new MethodComprehensiveAnalysis
        {
            MethodName = methodName,
            ContainingType = containingType ?? "",
            Callers = callers,
            BackwardCallChains = backwardChains,
            ForwardCallChains = forwardChains,
            CallPatterns = patterns,
            RecursiveCallChains = recursive,
            TotalDirectCallers = callers?.DirectCallers.Count ?? 0,
            TotalIndirectCallers = callers?.IndirectCallers.Count ?? 0,
            TotalBackwardPaths = backwardChains?.TotalPaths ?? 0,
            TotalForwardPaths = forwardChains?.TotalPaths ?? 0,
            HasRecursiveCalls = (recursive?.Count ?? 0) > 0,
            AnalysisTime = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Comprehensive analysis of a type including usages, inheritance, and dependencies
    /// </summary>
    public async Task<TypeComprehensiveAnalysis> AnalyzeTypeComprehensivelyAsync(string typeName, CancellationToken cancellationToken = default)
    {
        var usagesTask = FindTypeUsagesAsync(typeName, cancellationToken);
        var inheritanceTask = AnalyzeInheritanceAsync(typeName, cancellationToken);
        var dependenciesTask = AnalyzeTypeDependenciesAsync(typeName, cancellationToken);
        var genericTask = FindGenericUsagesAsync(typeName, cancellationToken);

        var usages = await usagesTask;
        var inheritance = await inheritanceTask;
        var dependencies = await dependenciesTask;
        var generic = await genericTask;

        return new TypeComprehensiveAnalysis
        {
            TypeName = typeName,
            TypeUsages = usages,
            InheritanceAnalysis = inheritance,
            DependencyAnalysis = dependencies,
            GenericUsages = generic,
            TotalUsages = usages?.TotalUsages ?? 0,
            InstantiationCount = usages?.Instantiations.Count ?? 0,
            DerivedTypeCount = inheritance?.DerivedClasses.Count ?? 0,
            InterfaceImplementationCount = inheritance?.InterfaceImplementations.Count ?? 0,
            DependencyCount = dependencies?.TotalDependencies ?? 0,
            GenericUsageCount = generic?.Count ?? 0,
            AnalysisTime = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Search for methods by signature
    /// </summary>
    public async Task<List<MethodSignature>> FindMethodsBySignatureAsync(MethodSignature signature, bool exactMatch = false, CancellationToken cancellationToken = default)
    {
        var symbols = await _symbolQuery.FindSymbolsAsync(signature.Name, "method");
        var matchingSignatures = new List<MethodSignature>();

        foreach (var symbol in symbols)
        {
            var methodSymbol = await GetMethodSymbol(symbol.File, symbol.Line);
            if (methodSymbol != null)
            {
                var methodSignature = CreateMethodSignature(methodSymbol);
                if (MatchesSignature(methodSignature, signature, exactMatch))
                {
                    matchingSignatures.Add(methodSignature);
                }
            }
        }

        return matchingSignatures;
    }

    /// <summary>
    /// Find methods with similar names (for refactoring suggestions)
    /// </summary>
    public async Task<List<MethodSignature>> FindSimilarMethodsAsync(string methodName, double threshold = 0.8, CancellationToken cancellationToken = default)
    {
        var allMethods = await GetAllMethodsAsync(cancellationToken);
        return allMethods
            .Where(m => CalculateSimilarity(m.Name, methodName) >= threshold)
            .ToList();
    }

    /// <summary>
    /// Find methods with similar names (synchronous wrapper for tests)
    /// </summary>
    public async Task<List<MethodSignature>> FindSimilarMethods(string methodName, double threshold = 0.8)
    {
        return await FindSimilarMethodsAsync(methodName, threshold);
    }

    /// <summary>
    /// Get overview of reverse search capabilities for the current workspace
    /// </summary>
    public async Task<ReverseSearchCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
        {
            return new ReverseSearchCapabilities
            {
                IsWorkspaceReady = false,
                TotalFiles = 0,
                TotalTypes = 0,
                TotalMethods = 0,
                SupportedAnalyses = new List<string>()
            };
        }

        var totalFiles = compilation.SyntaxTrees.Count();
        var totalTypes = await GetAllTypesAsync(cancellationToken);
        var totalMethods = await GetAllMethodsAsync(cancellationToken);

        return new ReverseSearchCapabilities
        {
            IsWorkspaceReady = true,
            TotalFiles = totalFiles,
            TotalTypes = totalTypes.Count,
            TotalMethods = totalMethods.Count,
            SupportedAnalyses = new List<string>
            {
                "Caller Analysis",
                "Call Chain Analysis",
                "Type Usage Analysis",
                "Inheritance Analysis",
                "Dependency Analysis",
                "Call Graph Analysis",
                "Pattern Analysis",
                "Refactoring Analysis"
            }
        };
    }

    private async Task<List<MethodSignature>> GetAllMethodsAsync(CancellationToken cancellationToken)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return new List<MethodSignature>();

        var methods = new List<MethodSignature>();
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var methodDeclarations = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
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
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return new List<string>();

        var types = new List<string>();
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var typeDeclarations = root.DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>();

            foreach (var typeDecl in typeDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDecl);
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

    private MethodSignature CreateMethodSignature(IMethodSymbol symbol)
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
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLower(),
            IsStatic = symbol.IsStatic,
            IsVirtual = symbol.IsVirtual,
            IsAbstract = symbol.IsAbstract,
            IsOverride = symbol.IsOverride,
            IsExtension = symbol.IsExtensionMethod,
            IsAsync = symbol.IsAsync,
            FullyQualifiedName = symbol.ToDisplayString()
        };
    }

    private bool MatchesSignature(MethodSignature signature1, MethodSignature signature2, bool exactMatch)
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

    private bool AreTypesCompatible(string type1, string type2)
    {
        // Simple compatibility check
        if (type1 == type2)
            return true;

        var baseTypes = new[] { "object", "string", "int", "long", "double", "float", "bool", "decimal" };
        if (baseTypes.Contains(type1) && baseTypes.Contains(type2))
            return true;

        // Handle generic types
        if (type1.Contains("<") && type2.Contains("<"))
        {
            var generic1 = type1.Substring(0, type1.IndexOf('<'));
            var generic2 = type2.Substring(0, type2.IndexOf('<'));
            return generic1 == generic2;
        }

        return false;
    }

    private double CalculateSimilarity(string str1, string str2)
    {
        // Simple Levenshtein distance based similarity
        var distance = CalculateLevenshteinDistance(str1, str2);
        var maxLength = Math.Max(str1.Length, str2.Length);
        return maxLength == 0 ? 1.0 : 1.0 - (double)distance / maxLength;
    }

    private int CalculateLevenshteinDistance(string str1, string str2)
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

/// <summary>
/// Comprehensive analysis of a method
/// </summary>
public class MethodComprehensiveAnalysis
{
    public required string MethodName { get; init; }
    public required string ContainingType { get; init; }
    public CallerResult? Callers { get; init; }
    public CallChainResult? BackwardCallChains { get; init; }
    public CallChainResult? ForwardCallChains { get; init; }
    public CallPatternAnalysis? CallPatterns { get; init; }
    public List<CallChainPath>? RecursiveCallChains { get; init; }
    public int TotalDirectCallers { get; init; }
    public int TotalIndirectCallers { get; init; }
    public int TotalBackwardPaths { get; init; }
    public int TotalForwardPaths { get; init; }
    public bool HasRecursiveCalls { get; init; }
    public DateTime AnalysisTime { get; init; }
}

/// <summary>
/// Comprehensive analysis of a type
/// </summary>
public class TypeComprehensiveAnalysis
{
    public required string TypeName { get; init; }
    public TypeUsageResult? TypeUsages { get; init; }
    public InheritanceAnalysis? InheritanceAnalysis { get; init; }
    public TypeDependencyAnalysis? DependencyAnalysis { get; init; }
    public List<TypeUsageInfo>? GenericUsages { get; init; }
    public int TotalUsages { get; init; }
    public int InstantiationCount { get; init; }
    public int DerivedTypeCount { get; init; }
    public int InterfaceImplementationCount { get; init; }
    public int DependencyCount { get; init; }
    public int GenericUsageCount { get; init; }
    public DateTime AnalysisTime { get; init; }
}

/// <summary>
/// Capabilities of the reverse search service
/// </summary>
public class ReverseSearchCapabilities
{
    public required bool IsWorkspaceReady { get; init; }
    public required int TotalFiles { get; init; }
    public required int TotalTypes { get; init; }
    public required int TotalMethods { get; init; }
    public required List<string> SupportedAnalyses { get; init; }
}