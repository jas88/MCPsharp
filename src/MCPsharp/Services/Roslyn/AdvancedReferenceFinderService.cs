using MCPsharp.Models.Roslyn;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Advanced service for comprehensive reference finding and analysis
/// </summary>
public partial class AdvancedReferenceFinderService
{
    private static readonly string[] CompatibleBaseTypes = { "object", "string", "int", "long", "double", "float", "bool", "decimal" };

    private readonly RoslynWorkspace _workspace;
    private readonly SymbolQueryService _symbolQuery;
    private readonly ICallerAnalysisService _callerAnalysis;
    private readonly ICallChainService _callChain;
    private readonly ITypeUsageService _typeUsage;
    private readonly ILogger<AdvancedReferenceFinderService> _logger;

    public AdvancedReferenceFinderService(
        RoslynWorkspace workspace,
        SymbolQueryService symbolQuery,
        ICallerAnalysisService callerAnalysis,
        ICallChainService callChain,
        ITypeUsageService typeUsage)
        : this(workspace, symbolQuery, callerAnalysis, callChain, typeUsage, NullLogger<AdvancedReferenceFinderService>.Instance)
    {
    }

    public AdvancedReferenceFinderService(
        RoslynWorkspace workspace,
        SymbolQueryService symbolQuery,
        ICallerAnalysisService callerAnalysis,
        ICallChainService callChain,
        ITypeUsageService typeUsage,
        ILogger<AdvancedReferenceFinderService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _symbolQuery = symbolQuery ?? throw new ArgumentNullException(nameof(symbolQuery));
        _callerAnalysis = callerAnalysis ?? throw new ArgumentNullException(nameof(callerAnalysis));
        _callChain = callChain ?? throw new ArgumentNullException(nameof(callChain));
        _typeUsage = typeUsage ?? throw new ArgumentNullException(nameof(typeUsage));
    }

    /// <summary>
    /// Find who calls a specific method
    /// </summary>
    public async Task<CallerResult?> FindCallersAsync(string methodName, string? containingType = null, bool includeIndirect = true, CancellationToken cancellationToken = default)
    {
        try
        {
            CallerResult? result;
            if (includeIndirect)
            {
                result = await _callerAnalysis.FindCallersAsync(methodName, containingType, cancellationToken);
            }
            else
            {
                result = await _callerAnalysis.FindDirectCallersAsync(methodName, containingType, cancellationToken);
            }

            // If the underlying service returns null (method not found), return a default result instead
            if (result == null)
            {
                return new CallerResult
                {
                    TargetSymbol = methodName,
                    TargetSignature = new MethodSignature
                    {
                        Name = methodName,
                        ContainingType = containingType ?? "",
                        ReturnType = "void",
                        Accessibility = "public"
                    },
                    Callers = new List<CallerInfo>(),
                    AnalysisTime = DateTime.UtcNow,
                    Metadata = new CallAnalysisMetadata
                    {
                        AnalysisWarnings = new List<string> { $"Method '{methodName}' not found in '{containingType ?? "any type"}'" },
                        IncompleteAnalysis = false
                    }
                };
            }

            return result;
        }
        catch (Exception ex)
        {
            LogCallerAnalysisFailed(_logger, methodName, ex);
            // Return default result on error
            return new CallerResult
            {
                TargetSymbol = methodName,
                TargetSignature = new MethodSignature
                {
                    Name = methodName,
                    ContainingType = containingType ?? "",
                    ReturnType = "void",
                    Accessibility = "public"
                },
                Callers = new List<CallerInfo>(),
                AnalysisTime = DateTime.UtcNow,
                Metadata = new CallAnalysisMetadata
                {
                    AnalysisWarnings = new List<string> { ex.Message },
                    IncompleteAnalysis = true
                }
            };
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
            Callers = result.DirectCallers.ToList(),
            AnalysisTime = result.AnalysisTime,
            Metadata = result.Metadata
        };
    }

    /// <summary>
    /// Find call chains leading to or from a specific method
    /// </summary>
    public async Task<CallChainResult?> FindCallChainsAsync(string methodName, string? containingType = null, CallDirection direction = CallDirection.Backward, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            if (direction == CallDirection.Forward)
            {
                return await _callChain.FindForwardCallChainsAsync(methodName, containingType, maxDepth, cancellationToken);
            }
            else
            {
                return await _callChain.FindCallChainsAsync(methodName, containingType, maxDepth, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            LogCallChainAnalysisFailed(_logger, methodName, ex);
            // Return default result on error
            return new CallChainResult
            {
                TargetMethod = new MethodSignature
                {
                    Name = methodName,
                    ContainingType = containingType ?? "",
                    ReturnType = "void",
                    Accessibility = "public"
                },
                Direction = CallDirection.Backward,
                Paths = new List<CallChainPath>(),
                AnalysisTime = DateTime.UtcNow,
                Metadata = new CallChainMetadata
                {
                    AnalysisWarnings = new List<string> { ex.Message },
                    ReachedMaxDepth = false,
                    IncompleteAnalysis = true
                }
            };
        }
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
        try
        {
            return await _typeUsage.FindTypeUsagesAsync(typeName, cancellationToken);
        }
        catch (Exception ex)
        {
            LogTypeUsageAnalysisFailed(_logger, typeName, ex);
            // Return default result on error
            return new TypeUsageResult
            {
                TypeName = typeName,
                FullTypeName = typeName,
                Usages = new List<TypeUsageInfo>(),
                AnalysisTime = DateTime.UtcNow,
                Metadata = new TypeUsageMetadata
                {
                    AnalysisWarnings = new List<string> { ex.Message },
                    IncompleteAnalysis = true
                }
            };
        }
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
        try
        {
            return await _callerAnalysis.AnalyzeCallPatternsAsync(methodName, containingType, cancellationToken);
        }
        catch (Exception ex)
        {
            LogCallPatternAnalysisFailed(_logger, methodName, ex);
            // Return default result on error
            return new CallPatternAnalysis
            {
                TargetMethod = new MethodSignature
                {
                    Name = methodName,
                    ContainingType = containingType ?? "",
                    ReturnType = "void",
                    Accessibility = "public"
                },
                Patterns = new List<CallPattern>(),
                TotalCallSites = 0,
                CallFrequencyByFile = new Dictionary<string, int>(),
                CommonCallContexts = new List<string> { ex.Message },
                HasRecursiveCalls = false,
                IsCalledAsynchronously = false,
                IsCalledInLoops = false,
                IsCalledInExceptionHandlers = false
            };
        }
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
        try
        {
            return await _typeUsage.AnalyzeInheritanceAsync(typeName, cancellationToken);
        }
        catch (Exception ex)
        {
            LogInheritanceAnalysisFailed(_logger, typeName, ex);
            // Return default result on error
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
        }
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
        try
        {
            return await _typeUsage.AnalyzeTypeDependenciesAsync(typeName, cancellationToken);
        }
        catch (Exception ex)
        {
            LogTypeDependencyAnalysisFailed(_logger, typeName, ex);
            // Return default result on error
            return new TypeDependencyAnalysis
            {
                TargetType = typeName,
                Dependencies = new List<TypeDependency>(),
                Dependents = new List<TypeDependency>(),
                DependencyFrequency = new Dictionary<string, int>(),
                CircularDependencies = new List<string>(),
                TotalDependencies = 0,
                TotalDependents = 0,
                HasCircularDependencies = false
            };
        }
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
