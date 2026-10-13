using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for analyzing call chains (who calls who, and who calls them)
/// </summary>
public partial class CallChainService : ICallChainService
{
    private readonly RoslynWorkspace _workspace;
    private readonly SymbolQueryService _symbolQuery;
    private readonly ICallerAnalysisService _callerAnalysis;

    public CallChainService(RoslynWorkspace workspace, SymbolQueryService symbolQuery, ICallerAnalysisService callerAnalysis)
    {
        _workspace = workspace;
        _symbolQuery = symbolQuery;
        _callerAnalysis = callerAnalysis;
    }

    public async Task<CallChainResult?> FindCallChainsAsync(string methodName, string? containingType = null, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        return await FindCallChainsByNameAsync(methodName, containingType, CallDirection.Backward, maxDepth, cancellationToken);
    }

    public async Task<CallChainResult?> FindCallChainsAsync(ISymbol symbol, CallDirection direction = CallDirection.Backward, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var targetSignature = CreateMethodSignature(symbol);
        var paths = new List<CallChainPath>();
        var visited = new HashSet<string>();
        var filesAnalyzed = 0;
        var methodsAnalyzed = 0;
        var reachedMaxDepth = false;

        if (direction == CallDirection.Backward)
        {
            // Find who calls this method
            var result = await BuildBackwardCallChainsAsync(symbol, targetSignature, new List<CallChainStep>(), paths, visited, maxDepth, false, 0, 0, cancellationToken);
            reachedMaxDepth = result.reachedMaxDepth;
            filesAnalyzed = result.filesAnalyzed;
            methodsAnalyzed = result.methodsAnalyzed;
        }
        else if (direction == CallDirection.Forward)
        {
            // Find what this method calls
            var result = await BuildForwardCallChainsAsync(symbol, targetSignature, new List<CallChainStep>(), paths, visited, maxDepth, false, 0, 0, cancellationToken);
            reachedMaxDepth = result.reachedMaxDepth;
            filesAnalyzed = result.filesAnalyzed;
            methodsAnalyzed = result.methodsAnalyzed;
        }

        var endTime = DateTime.UtcNow;
        var analysisDuration = endTime - startTime;

        return new CallChainResult
        {
            TargetMethod = targetSignature,
            Direction = direction,
            Paths = paths,
            Metadata = new CallChainMetadata
            {
                AnalysisDuration = analysisDuration,
                MaxDepth = maxDepth,
                MethodsAnalyzed = methodsAnalyzed,
                FilesAnalyzed = filesAnalyzed,
                ReachedMaxDepth = reachedMaxDepth,
                AnalysisMethod = "Recursive Call Chain Traversal"
            }
        };
    }

    public async Task<CallChainResult?> FindForwardCallChainsAsync(string methodName, string? containingType = null, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        return await FindCallChainsByNameAsync(methodName, containingType, CallDirection.Forward, maxDepth, cancellationToken);
    }

    public async Task<CallChainResult?> FindCallChainsAtLocationAsync(string filePath, int line, int column, CallDirection direction = CallDirection.Backward, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        var symbolInfo = await _symbolQuery.GetSymbolAtLocationAsync(filePath, line, column);
        if (symbolInfo?.Symbol == null)
            return null;

        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return null;

        return await FindCallChainsAsync(methodSymbol, direction, maxDepth, cancellationToken);
    }

    public async Task<List<CallChainPath>> FindCallChainsBetweenAsync(MethodSignature fromMethod, MethodSignature toMethod, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        var paths = new List<CallChainPath>();
        var visited = new HashSet<string>();

        var fromSymbol = FindMethodSymbol(fromMethod.Name, fromMethod.ContainingType, cancellationToken);
        var toSymbol = FindMethodSymbol(toMethod.Name, toMethod.ContainingType, cancellationToken);

        if (fromSymbol == null || toSymbol == null)
            return paths;

        await FindPathsBetweenAsync(fromSymbol, toSymbol, new List<CallChainStep>(), paths, visited, maxDepth, cancellationToken);
        return paths;
    }

    public async Task<List<CallChainPath>> FindRecursiveCallChainsAsync(string methodName, string? containingType = null, int maxDepth = 20, CancellationToken cancellationToken = default)
    {
        var symbol = FindMethodSymbol(methodName, containingType, cancellationToken);
        if (symbol == null)
            return new List<CallChainPath>();

        var paths = new List<CallChainPath>();
        var visited = new HashSet<string>();
        var callStack = new List<CallChainStep>();

        await DetectRecursiveCallsAsync(symbol, symbol, callStack, paths, visited, maxDepth, cancellationToken);
        return paths;
    }

    public async Task<CallGraphAnalysis> AnalyzeCallGraphAsync(string? typeName = null, string? namespaceName = null, CancellationToken cancellationToken = default)
    {
        if (!_workspace.IsInitialized)
            return new CallGraphAnalysis
            {
                Scope = typeName ?? namespaceName ?? "all",
                Methods = new List<MethodSignature>(),
                Relationships = new List<CallRelationship>(),
                CallGraph = new Dictionary<string, List<string>>(),
                ReverseCallGraph = new Dictionary<string, List<string>>(),
                EntryPoints = new List<string>(),
                LeafMethods = new List<string>(),
                CircularDependencies = new List<CircularDependency>(),
                MaxCallDepth = 0,
                AverageCallDepth = 0
            };

        var methods = new List<MethodSignature>();
        var relationships = new List<CallRelationship>();
        var callGraph = new Dictionary<string, List<string>>();
        var reverseCallGraph = new Dictionary<string, List<string>>();

        // Get all methods in scope
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
                if (symbol == null)
                    continue;

                // Check if method is in scope
                if (!IsMethodInScope(symbol, typeName, namespaceName))
                    continue;

                var signature = CreateMethodSignature(symbol);
                methods.Add(signature);

                // Analyze method calls
                var methodCalls = AnalyzeMethodCalls(methodDecl, semanticModel, cancellationToken);
                foreach (var call in methodCalls)
                {
                    var relationship = new CallRelationship
                    {
                        FromMethod = signature,
                        ToMethod = call.Signature,
                        CallType = call.CallType,
                        File = call.File,
                        Line = call.Line,
                        Column = call.Column,
                        CallCount = 1,
                        Confidence = call.Confidence
                    };
                    relationships.Add(relationship);

                    // Update call graphs
                    var fromKey = $"{signature.ContainingType}.{signature.Name}";
                    var toKey = $"{call.Signature.ContainingType}.{call.Signature.Name}";

                    if (!callGraph.ContainsKey(fromKey))
                        callGraph[fromKey] = new List<string>();
                    callGraph[fromKey].Add(toKey);

                    if (!reverseCallGraph.ContainsKey(toKey))
                        reverseCallGraph[toKey] = new List<string>();
                    reverseCallGraph[toKey].Add(fromKey);
                }
            }
        }

        // Find entry points (methods not called by others)
        var allCalledMethods = relationships.Select(r => $"{r.ToMethod.ContainingType}.{r.ToMethod.Name}").ToHashSet();
        var entryPoints = methods
            .Where(m => !allCalledMethods.Contains($"{m.ContainingType}.{m.Name}"))
            .Select(m => $"{m.ContainingType}.{m.Name}")
            .ToList();

        // Find leaf methods (methods that don't call others)
        var callingMethods = relationships.Select(r => $"{r.FromMethod.ContainingType}.{r.FromMethod.Name}").ToHashSet();
        var leafMethods = methods
            .Where(m => !callingMethods.Contains($"{m.ContainingType}.{m.Name}"))
            .Select(m => $"{m.ContainingType}.{m.Name}")
            .ToList();

        // Find circular dependencies
        var circularDependencies = FindCircularDependenciesInGraph(callGraph);

        return new CallGraphAnalysis
        {
            Scope = typeName ?? namespaceName ?? "all",
            Methods = methods,
            Relationships = relationships,
            CallGraph = callGraph,
            ReverseCallGraph = reverseCallGraph,
            EntryPoints = entryPoints,
            LeafMethods = leafMethods,
            CircularDependencies = circularDependencies,
            MaxCallDepth = CalculateMaxCallDepth(callGraph, entryPoints),
            AverageCallDepth = CalculateAverageCallDepth(callGraph, entryPoints)
        };
    }

    public async Task<List<CircularDependency>> FindCircularDependenciesAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default)
    {
        var callGraph = await AnalyzeCallGraphAsync(null, namespaceFilter, cancellationToken);
        return callGraph.CircularDependencies;
    }

    public async Task<CallChainPath?> FindShortestPathAsync(MethodSignature fromMethod, MethodSignature toMethod, CancellationToken cancellationToken = default)
    {
        var allPaths = await FindCallChainsBetweenAsync(fromMethod, toMethod, 50, cancellationToken);
        return allPaths.OrderBy(p => p.Length).FirstOrDefault();
    }

    public async Task<ReachabilityAnalysis> FindReachableMethodsAsync(string methodName, string? containingType = null, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var startSymbol = FindMethodSymbol(methodName, containingType, cancellationToken);
        if (startSymbol == null)
            return new ReachabilityAnalysis
        {
            StartMethod = new MethodSignature
            {
                Name = methodName,
                ContainingType = containingType ?? "",
                ReturnType = "void",
                Accessibility = "public"
            },
            ReachableMethods = new List<MethodSignature>(),
            MethodsByDepth = new Dictionary<int, List<MethodSignature>>(),
            UnreachableMethods = new List<MethodSignature>(),
            MaxDepthReached = 0,
            TotalMethodsAnalyzed = 0,
            AnalysisDuration = TimeSpan.Zero,
            ReachedAnalysisLimit = false
        };

        var startSignature = CreateMethodSignature(startSymbol);
        var reachableMethods = new List<MethodSignature>();
        var methodsByDepth = new Dictionary<int, List<MethodSignature>>();
        var visited = new HashSet<string>();

        await BuildReachableMethodsAsync(startSymbol, startSignature, 0, maxDepth, reachableMethods, methodsByDepth, visited, cancellationToken);

        var endTime = DateTime.UtcNow;
        var analysisDuration = endTime - startTime;

        // Find all methods in the project to determine unreachable ones
        var allMethods = await GetAllMethodsAsync(cancellationToken);
        var unreachableMethods = allMethods
            .Where(m => !reachableMethods.Any(rm => rm.Name == m.Name && rm.ContainingType == m.ContainingType))
            .ToList();

        return new ReachabilityAnalysis
        {
            StartMethod = startSignature,
            ReachableMethods = reachableMethods,
            MethodsByDepth = methodsByDepth,
            UnreachableMethods = unreachableMethods,
            MaxDepthReached = methodsByDepth.Count > 0 ? methodsByDepth.Keys.Max() : 0,
            TotalMethodsAnalyzed = allMethods.Count,
            AnalysisDuration = analysisDuration,
            ReachedAnalysisLimit = false
        };
    }
}
