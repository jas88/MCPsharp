using MCPsharp.Models.Roslyn;
using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Comprehensive method/type analysis, signature and similarity search, and capability reporting.
/// </summary>
public partial class AdvancedReferenceFinderService
{
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
            TotalDirectCallers = callers?.DirectCallers.Count() ?? 0,
            TotalIndirectCallers = callers?.IndirectCallers.Count() ?? 0,
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
            InstantiationCount = usages?.Instantiations.Count() ?? 0,
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
        try
        {
            // Wait a short time for workspace to initialize
            await Task.Delay(100, cancellationToken);

            if (!_workspace.IsInitialized)
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

            var totalFiles = _workspace.GetSourceTrees().Count;
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
        catch (Exception ex)
        {
            LogCapabilitiesFailed(_logger, ex);
            // Return default capabilities on error
            return new ReverseSearchCapabilities
            {
                IsWorkspaceReady = false,
                TotalFiles = 0,
                TotalTypes = 0,
                TotalMethods = 0,
                SupportedAnalyses = new List<string>()
            };
        }
    }
}
