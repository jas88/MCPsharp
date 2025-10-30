using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for analyzing call chains (who calls who, and who calls them)
/// </summary>
public class CallChainService : ICallChainService
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
        return await FindCallChainsAsync(methodName, containingType, CallDirection.Backward, maxDepth, cancellationToken);
    }

    public async Task<CallChainResult?> FindForwardCallChainsAsync(string methodName, string? containingType = null, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        return await FindCallChainsAsync(methodName, containingType, CallDirection.Forward, maxDepth, cancellationToken);
    }

    public async Task<CallChainResult?> FindCallChainsAtLocationAsync(string filePath, int line, int column, CallDirection direction = CallDirection.Backward, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        var symbolInfo = await _symbolQuery.GetSymbolAtLocationAsync(filePath, line, column);
        if (symbolInfo?.Symbol == null)
            return null;

        var methodSymbol = await ConvertToMethodSymbol(symbolInfo.Symbol, filePath, line, column, cancellationToken);
        if (methodSymbol == null)
            return null;

        return await FindCallChainsAsync(methodSymbol, direction, maxDepth, cancellationToken);
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

    public async Task<List<CallChainPath>> FindCallChainsBetweenAsync(MethodSignature fromMethod, MethodSignature toMethod, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        var paths = new List<CallChainPath>();
        var visited = new HashSet<string>();

        var fromSymbol = await FindMethodSymbol(fromMethod.Name, fromMethod.ContainingType, cancellationToken);
        var toSymbol = await FindMethodSymbol(toMethod.Name, toMethod.ContainingType, cancellationToken);

        if (fromSymbol == null || toSymbol == null)
            return paths;

        await FindPathsBetweenAsync(fromSymbol, toSymbol, new List<CallChainStep>(), paths, visited, maxDepth, cancellationToken);
        return paths;
    }

    public async Task<List<CallChainPath>> FindRecursiveCallChainsAsync(string methodName, string? containingType = null, int maxDepth = 20, CancellationToken cancellationToken = default)
    {
        var symbol = await FindMethodSymbol(methodName, containingType, cancellationToken);
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
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
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
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var methodDeclarations = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
                if (symbol == null)
                    continue;

                // Check if method is in scope
                if (!IsMethodInScope(symbol, typeName, namespaceName))
                    continue;

                var signature = CreateMethodSignature(symbol);
                methods.Add(signature);

                // Analyze method calls
                var methodCalls = await AnalyzeMethodCalls(methodDecl, semanticModel, cancellationToken);
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
        var circularDependencies = await FindCircularDependenciesInGraph(callGraph, cancellationToken);

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
        var startSymbol = await FindMethodSymbol(methodName, containingType, cancellationToken);
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
            MaxDepthReached = methodsByDepth.Keys.Any() ? methodsByDepth.Keys.Max() : 0,
            TotalMethodsAnalyzed = allMethods.Count,
            AnalysisDuration = analysisDuration,
            ReachedAnalysisLimit = false
        };
    }

    private async Task<(bool reachedMaxDepth, int filesAnalyzed, int methodsAnalyzed)> BuildBackwardCallChainsAsync(ISymbol currentSymbol, MethodSignature currentSignature, List<CallChainStep> currentPath, List<CallChainPath> paths, HashSet<string> visited, int maxDepth, bool reachedMaxDepth, int filesAnalyzed, int methodsAnalyzed, CancellationToken cancellationToken)
    {
        if (currentPath.Count >= maxDepth)
        {
            reachedMaxDepth = true;
            return (reachedMaxDepth, filesAnalyzed, methodsAnalyzed);
        }

        var key = $"{currentSignature.ContainingType}.{currentSignature.Name}";
        if (visited.Contains(key))
            return (reachedMaxDepth, filesAnalyzed, methodsAnalyzed);

        visited.Add(key);

        // Find callers of current method
        var callers = await _callerAnalysis.FindCallersAsync(currentSymbol, cancellationToken);
        if (callers == null)
            return (reachedMaxDepth, filesAnalyzed, methodsAnalyzed);

        foreach (var caller in callers.Callers)
        {
            methodsAnalyzed++;
            var step = new CallChainStep
            {
                FromMethod = caller.CallerSignature ?? new MethodSignature { Name = caller.CallerMethod, ContainingType = caller.CallerType, ReturnType = "void", Accessibility = "public" },
                ToMethod = currentSignature,
                File = caller.File,
                Line = caller.Line,
                Column = caller.Column,
                CallType = caller.CallType,
                Confidence = caller.Confidence,
                CallExpression = caller.CallExpression,
                Context = caller.Context
            };

            var newPath = new List<CallChainStep>(currentPath) { step };
            paths.Add(new CallChainPath
            {
                Steps = newPath,
                Confidence = caller.Confidence,
                IsRecursive = caller.IsRecursive,
                LoopDetected = caller.CallChain
            });

            // Continue building chain backwards
            if (caller.CallerSignature != null)
            {
                var callerSymbol = await FindMethodSymbol(caller.CallerSignature.Name, caller.CallerSignature.ContainingType, cancellationToken);
                if (callerSymbol != null)
                {
                    var result = await BuildBackwardCallChainsAsync(callerSymbol, caller.CallerSignature, newPath, paths, visited, maxDepth, reachedMaxDepth, filesAnalyzed, methodsAnalyzed, cancellationToken);
                    reachedMaxDepth = result.reachedMaxDepth;
                    filesAnalyzed = result.filesAnalyzed;
                    methodsAnalyzed = result.methodsAnalyzed;
                }
            }
        }

        return (reachedMaxDepth, filesAnalyzed, methodsAnalyzed);
    }

    private async Task<(bool reachedMaxDepth, int filesAnalyzed, int methodsAnalyzed)> BuildForwardCallChainsAsync(ISymbol currentSymbol, MethodSignature currentSignature, List<CallChainStep> currentPath, List<CallChainPath> paths, HashSet<string> visited, int maxDepth, bool reachedMaxDepth, int filesAnalyzed, int methodsAnalyzed, CancellationToken cancellationToken)
    {
        if (currentPath.Count >= maxDepth)
        {
            reachedMaxDepth = true;
            return (reachedMaxDepth, filesAnalyzed, methodsAnalyzed);
        }

        var key = $"{currentSignature.ContainingType}.{currentSignature.Name}";
        if (visited.Contains(key))
            return (reachedMaxDepth, filesAnalyzed, methodsAnalyzed);

        visited.Add(key);

        // Find methods called by current method
        var calledMethods = await FindCalledMethodsAsync(currentSymbol, cancellationToken);
        foreach (var calledMethod in calledMethods)
        {
            methodsAnalyzed++;
            var step = new CallChainStep
            {
                FromMethod = currentSignature,
                ToMethod = calledMethod.Signature,
                File = calledMethod.File,
                Line = calledMethod.Line,
                Column = calledMethod.Column,
                CallType = calledMethod.CallType,
                Confidence = calledMethod.Confidence,
                CallExpression = calledMethod.CallExpression,
                Context = calledMethod.Context
            };

            var newPath = new List<CallChainStep>(currentPath) { step };
            paths.Add(new CallChainPath
            {
                Steps = newPath,
                Confidence = calledMethod.Confidence
            });

            // Continue building chain forwards
            var calledSymbol = await FindMethodSymbol(calledMethod.Signature.Name, calledMethod.Signature.ContainingType, cancellationToken);
            if (calledSymbol != null)
            {
                var result = await BuildForwardCallChainsAsync(calledSymbol, calledMethod.Signature, newPath, paths, visited, maxDepth, reachedMaxDepth, filesAnalyzed, methodsAnalyzed, cancellationToken);
                reachedMaxDepth = result.reachedMaxDepth;
                filesAnalyzed = result.filesAnalyzed;
                methodsAnalyzed = result.methodsAnalyzed;
            }
        }

        return (reachedMaxDepth, filesAnalyzed, methodsAnalyzed);
    }

    private async Task DetectRecursiveCallsAsync(ISymbol originalSymbol, ISymbol currentSymbol, List<CallChainStep> callStack, List<CallChainPath> paths, HashSet<string> visited, int maxDepth, CancellationToken cancellationToken)
    {
        if (callStack.Count >= maxDepth)
            return;

        var currentSignature = CreateMethodSignature(currentSymbol);
        var key = $"{currentSignature.ContainingType}.{currentSignature.Name}";

        // Check for recursion
        if (callStack.Any(s => s.ToMethod.Name == currentSymbol.Name && s.ToMethod.ContainingType == currentSignature.ContainingType))
        {
            var recursivePath = new CallChainPath
            {
                Steps = new List<CallChainStep>(callStack),
                Confidence = ConfidenceLevel.High,
                IsRecursive = true
            };
            paths.Add(recursivePath);
            return;
        }

        if (visited.Contains(key))
            return;

        visited.Add(key);

        // Find methods called by current method
        var calledMethods = await FindCalledMethodsAsync(currentSymbol, cancellationToken);
        foreach (var calledMethod in calledMethods)
        {
            var step = new CallChainStep
            {
                FromMethod = currentSignature,
                ToMethod = calledMethod.Signature,
                File = calledMethod.File,
                Line = calledMethod.Line,
                Column = calledMethod.Column,
                CallType = calledMethod.CallType,
                Confidence = calledMethod.Confidence,
                CallExpression = calledMethod.CallExpression,
                Context = calledMethod.Context
            };

            callStack.Add(step);

            var calledSymbol = await FindMethodSymbol(calledMethod.Signature.Name, calledMethod.Signature.ContainingType, cancellationToken);
            if (calledSymbol != null)
            {
                await DetectRecursiveCallsAsync(originalSymbol, calledSymbol, callStack, paths, visited, maxDepth, cancellationToken);
            }

            callStack.RemoveAt(callStack.Count - 1);
        }
    }

    private async Task FindPathsBetweenAsync(ISymbol fromSymbol, ISymbol toSymbol, List<CallChainStep> currentPath, List<CallChainPath> paths, HashSet<string> visited, int maxDepth, CancellationToken cancellationToken)
    {
        if (currentPath.Count >= maxDepth)
            return;

        var fromSignature = CreateMethodSignature(fromSymbol);
        var key = $"{fromSignature.ContainingType}.{fromSignature.Name}";

        if (visited.Contains(key))
            return;

        visited.Add(key);

        // Find methods called by current symbol
        var calledMethods = await FindCalledMethodsAsync(fromSymbol, cancellationToken);
        foreach (var calledMethod in calledMethods)
        {
            var step = new CallChainStep
            {
                FromMethod = fromSignature,
                ToMethod = calledMethod.Signature,
                File = calledMethod.File,
                Line = calledMethod.Line,
                Column = calledMethod.Column,
                CallType = calledMethod.CallType,
                Confidence = calledMethod.Confidence,
                CallExpression = calledMethod.CallExpression,
                Context = calledMethod.Context
            };

            // Check if we reached the target
            if (calledMethod.Signature.Name == toSymbol.Name &&
                calledMethod.Signature.ContainingType == toSymbol.ContainingType?.ToDisplayString())
            {
                paths.Add(new CallChainPath
                {
                    Steps = new List<CallChainStep>(currentPath) { step },
                    Confidence = calledMethod.Confidence
                });
                continue;
            }

            // Continue searching
            var calledSymbol = await FindMethodSymbol(calledMethod.Signature.Name, calledMethod.Signature.ContainingType, cancellationToken);
            if (calledSymbol != null)
            {
                await FindPathsBetweenAsync(calledSymbol, toSymbol, new List<CallChainStep>(currentPath) { step }, paths, visited, maxDepth, cancellationToken);
            }
        }
    }

    private async Task<List<MethodCallInfo>> FindCalledMethodsAsync(ISymbol methodSymbol, CancellationToken cancellationToken)
    {
        var calledMethods = new List<MethodCallInfo>();
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return calledMethods;

        var references = await SymbolFinder.FindReferencesAsync(methodSymbol, _workspace.Solution, cancellationToken);
        foreach (var reference in references)
        {
            foreach (var location in reference.Locations)
            {
                if (!location.Location.IsInSource)
                    continue;

                var document = location.Document;
                if (document == null)
                    continue;

                var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (syntaxTree == null || semanticModel == null)
                    continue;

                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var node = root.FindNode(location.Location.SourceSpan);

                // Find method calls within this method
                var methodCalls = node.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>();

                foreach (var call in methodCalls)
                {
                    var callSymbol = semanticModel.GetSymbolInfo(call).Symbol;
                    if (callSymbol != null && callSymbol is IMethodSymbol calledMethodSymbol)
                    {
                        var lineSpan = call.GetLocation().GetLineSpan();
                        var signature = CreateMethodSignature(calledMethodSymbol);

                        calledMethods.Add(new MethodCallInfo
                        {
                            Signature = signature,
                            File = document.FilePath ?? "",
                            Line = lineSpan.StartLinePosition.Line,
                            Column = lineSpan.StartLinePosition.Character,
                            CallType = DetermineCallType(call, calledMethodSymbol),
                            Confidence = ConfidenceLevel.High,
                            CallExpression = call.ToString(),
                            Context = call.FirstAncestorOrSelf<MethodDeclarationSyntax>()?.ToString() ?? ""
                        });
                    }
                }
            }
        }

        return calledMethods;
    }

    private async Task<List<MethodCallInfo>> AnalyzeMethodCalls(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var methodCalls = new List<MethodCallInfo>();
        var filePath = semanticModel.SyntaxTree.FilePath;
        var document = _workspace.GetDocument(filePath);
        if (document == null)
            return methodCalls;

        var invocations = methodDecl.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var symbol = semanticModel.GetSymbolInfo(invocation).Symbol;
            if (symbol is IMethodSymbol methodSymbol)
            {
                var lineSpan = invocation.GetLocation().GetLineSpan();
                var signature = CreateMethodSignature(methodSymbol);

                methodCalls.Add(new MethodCallInfo
                {
                    Signature = signature,
                    File = document.FilePath ?? "",
                    Line = lineSpan.StartLinePosition.Line,
                    Column = lineSpan.StartLinePosition.Character,
                    CallType = DetermineCallType(invocation, methodSymbol),
                    Confidence = ConfidenceLevel.High,
                    CallExpression = invocation.ToString(),
                    Context = invocation.Parent?.ToString() ?? ""
                });
            }
        }

        return methodCalls;
    }

    private async Task BuildReachableMethodsAsync(ISymbol currentSymbol, MethodSignature currentSignature, int currentDepth, int maxDepth, List<MethodSignature> reachableMethods, Dictionary<int, List<MethodSignature>> methodsByDepth, HashSet<string> visited, CancellationToken cancellationToken)
    {
        if (currentDepth >= maxDepth)
            return;

        var key = $"{currentSignature.ContainingType}.{currentSignature.Name}";
        if (visited.Contains(key))
            return;

        visited.Add(key);
        reachableMethods.Add(currentSignature);

        if (!methodsByDepth.ContainsKey(currentDepth))
            methodsByDepth[currentDepth] = new List<MethodSignature>();
        methodsByDepth[currentDepth].Add(currentSignature);

        var calledMethods = await FindCalledMethodsAsync(currentSymbol, cancellationToken);
        foreach (var calledMethod in calledMethods)
        {
            var calledSymbol = await FindMethodSymbol(calledMethod.Signature.Name, calledMethod.Signature.ContainingType, cancellationToken);
            if (calledSymbol != null)
            {
                await BuildReachableMethodsAsync(calledSymbol, calledMethod.Signature, currentDepth + 1, maxDepth, reachableMethods, methodsByDepth, visited, cancellationToken);
            }
        }
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
                if (symbol != null)
                {
                    methods.Add(CreateMethodSignature(symbol));
                }
            }
        }

        return methods;
    }

    private bool IsMethodInScope(ISymbol symbol, string? typeName, string? namespaceName)
    {
        if (string.IsNullOrEmpty(typeName) && string.IsNullOrEmpty(namespaceName))
            return true;

        var containingType = symbol.ContainingType?.ToDisplayString() ?? "";
        var containingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? "";

        if (!string.IsNullOrEmpty(typeName) && containingType.Contains(typeName))
            return true;

        if (!string.IsNullOrEmpty(namespaceName) && containingNamespace.Contains(namespaceName))
            return true;

        return false;
    }

    private async Task<List<CircularDependency>> FindCircularDependenciesInGraph(Dictionary<string, List<string>> callGraph, CancellationToken cancellationToken)
    {
        var circularDependencies = new List<CircularDependency>();
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var method in callGraph.Keys)
        {
            if (!visited.Contains(method))
            {
                var cycle = DetectCycleInGraph(method, callGraph, visited, recursionStack, new List<string>());
                if (cycle.Count > 0)
                {
                    var dependency = new CircularDependency
                    {
                        Methods = cycle.Select(m => CreateMethodSignatureFromString(m)).ToList(),
                        CycleLength = cycle.Count,
                        Steps = cycle.Zip(cycle.Skip(1).Append(cycle.First()), (from, to) => new CallChainStep
                        {
                            FromMethod = CreateMethodSignatureFromString(from),
                            ToMethod = CreateMethodSignatureFromString(to),
                            File = "",
                            Line = 0,
                            Column = 0,
                            CallType = CallType.Direct,
                            Confidence = ConfidenceLevel.High
                        }).ToList(),
                        Confidence = ConfidenceLevel.Medium,
                        FilesInvolved = new List<string>()
                    };
                    circularDependencies.Add(dependency);
                }
            }
        }

        return circularDependencies;
    }

    private List<string> DetectCycleInGraph(string method, Dictionary<string, List<string>> callGraph, HashSet<string> visited, HashSet<string> recursionStack, List<string> path)
    {
        visited.Add(method);
        recursionStack.Add(method);
        path.Add(method);

        if (callGraph.ContainsKey(method))
        {
            foreach (var calledMethod in callGraph[method])
            {
                if (!visited.Contains(calledMethod))
                {
                    var cycle = DetectCycleInGraph(calledMethod, callGraph, visited, recursionStack, new List<string>(path));
                    if (cycle.Count > 0)
                        return cycle;
                }
                else if (recursionStack.Contains(calledMethod))
                {
                    // Found a cycle
                    var cycleStart = path.IndexOf(calledMethod);
                    return path.Skip(cycleStart).ToList();
                }
            }
        }

        recursionStack.Remove(method);
        return new List<string>();
    }

    private int CalculateMaxCallDepth(Dictionary<string, List<string>> callGraph, List<string> entryPoints)
    {
        var maxDepth = 0;
        foreach (var entryPoint in entryPoints)
        {
            var depth = CalculateMaxDepthFromNode(entryPoint, callGraph, new HashSet<string>());
            maxDepth = Math.Max(maxDepth, depth);
        }
        return maxDepth;
    }

    private int CalculateMaxDepthFromNode(string node, Dictionary<string, List<string>> callGraph, HashSet<string> visited)
    {
        if (visited.Contains(node) || !callGraph.ContainsKey(node))
            return 0;

        visited.Add(node);
        var maxChildDepth = 0;

        foreach (var child in callGraph[node])
        {
            var childDepth = CalculateMaxDepthFromNode(child, callGraph, new HashSet<string>(visited));
            maxChildDepth = Math.Max(maxChildDepth, childDepth);
        }

        return maxChildDepth + 1;
    }

    private double CalculateAverageCallDepth(Dictionary<string, List<string>> callGraph, List<string> entryPoints)
    {
        var depths = new List<int>();
        foreach (var entryPoint in entryPoints)
        {
            var depth = CalculateMaxDepthFromNode(entryPoint, callGraph, new HashSet<string>());
            depths.Add(depth);
        }

        return depths.Count > 0 ? depths.Average() : 0;
    }

    private async Task<CallChainResult?> FindCallChainsAsync(string methodName, string? containingType, CallDirection direction, int maxDepth, CancellationToken cancellationToken)
    {
        var symbol = await FindMethodSymbol(methodName, containingType, cancellationToken);
        if (symbol == null)
            return null;

        return await FindCallChainsAsync(symbol, direction, maxDepth, cancellationToken);
    }

    private async Task<IMethodSymbol?> FindMethodSymbol(string methodName, string? containingType, CancellationToken cancellationToken)
    {
        var symbols = await _symbolQuery.FindSymbolsAsync(methodName, "method");

        if (string.IsNullOrEmpty(containingType))
            return GetMethodSymbol(symbols.FirstOrDefault()?.File, symbols.FirstOrDefault()?.Line ?? 0);

        var matchingSymbol = symbols.FirstOrDefault(s => s.ContainerName == containingType || s.ContainerName?.EndsWith(containingType) == true);
        return GetMethodSymbol(matchingSymbol?.File, matchingSymbol?.Line ?? 0);
    }

    private IMethodSymbol? GetMethodSymbol(string? filePath, int line)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        var document = _workspace.GetDocument(filePath);
        if (document == null)
            return null;

        var symbolInfo = _symbolQuery.GetSymbolAtLocationAsync(filePath, line, 0).Result;
        if (symbolInfo?.Symbol == null)
            return null;

        return ConvertToMethodSymbol(symbolInfo.Symbol, filePath, line, 0, CancellationToken.None).Result;
    }

    private async Task<IMethodSymbol?> ConvertToMethodSymbol(ISymbol symbol, string filePath, int line, int column, CancellationToken cancellationToken)
    {
        if (symbol is IMethodSymbol methodSymbol)
            return methodSymbol;

        return null;
    }

    private CallType DetermineCallType(SyntaxNode node, ISymbol targetSymbol)
    {
        if (node is InvocationExpressionSyntax)
            return CallType.Direct;

        return CallType.Unknown;
    }

    private MethodSignature CreateMethodSignature(ISymbol symbol)
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
            Accessibility = methodSymbol.DeclaredAccessibility.ToString().ToLower(),
            IsStatic = methodSymbol.IsStatic,
            IsVirtual = methodSymbol.IsVirtual,
            IsAbstract = methodSymbol.IsAbstract,
            IsOverride = methodSymbol.IsOverride,
            IsExtension = methodSymbol.IsExtensionMethod,
            IsAsync = methodSymbol.IsAsync,
            FullyQualifiedName = methodSymbol.ToDisplayString()
        };
    }

    private MethodSignature CreateMethodSignatureFromString(string methodKey)
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

    private class MethodCallInfo
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