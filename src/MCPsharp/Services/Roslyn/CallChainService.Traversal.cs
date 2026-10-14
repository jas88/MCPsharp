using MCPsharp.Models.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Recursive traversal of the call graph (backward, forward, recursive and between-method paths).
/// </summary>
public partial class CallChainService
{
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
                var callerSymbol = FindMethodSymbol(caller.CallerSignature.Name, caller.CallerSignature.ContainingType, cancellationToken);
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
            var calledSymbol = FindMethodSymbol(calledMethod.Signature.Name, calledMethod.Signature.ContainingType, cancellationToken);
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

            var calledSymbol = FindMethodSymbol(calledMethod.Signature.Name, calledMethod.Signature.ContainingType, cancellationToken);
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
            var calledSymbol = FindMethodSymbol(calledMethod.Signature.Name, calledMethod.Signature.ContainingType, cancellationToken);
            if (calledSymbol != null)
            {
                await FindPathsBetweenAsync(calledSymbol, toSymbol, new List<CallChainStep>(currentPath) { step }, paths, visited, maxDepth, cancellationToken);
            }
        }
    }

    private async Task<List<MethodCallInfo>> FindCalledMethodsAsync(ISymbol methodSymbol, CancellationToken cancellationToken)
    {
        var calledMethods = new List<MethodCallInfo>();

        // Find the syntax nodes for this method
        var syntaxReferences = methodSymbol.DeclaringSyntaxReferences;
        foreach (var syntaxRef in syntaxReferences)
        {
            var syntaxNode = await syntaxRef.GetSyntaxAsync(cancellationToken);
            if (syntaxNode == null)
                continue;

            // Get the semantic model from the compilation of the project that declares the method
            var syntaxTree = syntaxNode.SyntaxTree;
            var semanticModel = _workspace.GetSemanticModel(syntaxTree);
            if (semanticModel == null)
                continue;

            // Find the method declaration
            var methodDecl = syntaxNode.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (methodDecl == null)
                continue;

            // Find all invocation expressions within this method
            var invocations = methodDecl.DescendantNodes()
                .OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                var callSymbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol;
                if (callSymbol is IMethodSymbol calledMethodSymbol)
                {
                    var lineSpan = invocation.GetLocation().GetLineSpan();
                    var signature = CreateMethodSignature(calledMethodSymbol);

                    calledMethods.Add(new MethodCallInfo
                    {
                        Signature = signature,
                        File = syntaxTree.FilePath ?? "",
                        Line = lineSpan.StartLinePosition.Line,
                        Column = lineSpan.StartLinePosition.Character,
                        CallType = DetermineCallType(invocation),
                        Confidence = ConfidenceLevel.High,
                        CallExpression = invocation.ToString(),
                        Context = invocation.Parent?.ToString() ?? ""
                    });
                }
            }
        }

        return calledMethods;
    }

    private List<MethodCallInfo> AnalyzeMethodCalls(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel, CancellationToken cancellationToken)
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
            var symbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol;
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
                    CallType = DetermineCallType(invocation),
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
        foreach (var calledSignature in calledMethods.Select(m => m.Signature))
        {
            var calledSymbol = FindMethodSymbol(calledSignature.Name, calledSignature.ContainingType, cancellationToken);
            if (calledSymbol != null)
            {
                await BuildReachableMethodsAsync(calledSymbol, calledSignature, currentDepth + 1, maxDepth, reachableMethods, methodsByDepth, visited, cancellationToken);
            }
        }
    }
}
