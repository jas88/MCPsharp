using MCPsharp.Models.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Discovery of direct and indirect callers and classification of call sites.
/// </summary>
public partial class CallerAnalysisService
{
    private async Task<CallerResult?> FindCallersCoreAsync(ISymbol symbol, DateTime startTime, CancellationToken cancellationToken)
    {
        if (!_workspace.IsInitialized)
            return null;

        var targetSignature = CreateMethodSignature(symbol);
        var callers = new List<CallerInfo>();
        var filesAnalyzed = 0;
        var methodsAnalyzed = 0;

        // Find all references to the symbol across every project in the solution
        var seenLocations = new HashSet<(string, int, int)>();
        var referencedSymbols = await SymbolFinder.FindReferencesAsync(symbol, _workspace.Solution, cancellationToken);

        foreach (var referencedSymbol in referencedSymbols)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                var lineSpan = location.Location.GetLineSpan();
                if (!lineSpan.Path?.EndsWith(".cs", StringComparison.Ordinal) == true)
                    continue;

                var document = location.Document;
                if (document == null)
                    continue;

                // Multi-targeted projects report the same source location once per target framework
                if (!seenLocations.Add((lineSpan.Path ?? "", lineSpan.StartLinePosition.Line, lineSpan.StartLinePosition.Character)))
                    continue;

                filesAnalyzed++;
                var symbolLocation = ConvertToSymbolLocation(location);
                var callerInfo = await AnalyzeCallerLocation(symbolLocation, symbol, cancellationToken);
                if (callerInfo != null)
                {
                    callers.Add(callerInfo);
                    methodsAnalyzed++;
                }
            }
        }

        // Fallback: If SymbolFinder doesn't find references, search manually
        // For test scenarios and simple files, always use manual search as fallback
        // Only keep manual matches for call sites SymbolFinder did not already report
        var reportedSites = callers.Select(c => (c.File, c.Line, c.CallerMethod)).ToHashSet();
        callers.AddRange((await FindCallersManuallyAsync(symbol, cancellationToken))
            .Where(c => reportedSites.Add((c.File, c.Line, c.CallerMethod))));
        filesAnalyzed += callers.Count;
        methodsAnalyzed += callers.Count;

        // Analyze indirect calls through interfaces and base classes
        var indirectCallers = await FindIndirectCallers(symbol, cancellationToken);
        callers.AddRange(indirectCallers);

        var endTime = DateTime.UtcNow;
        var analysisDuration = endTime - startTime;

        return new CallerResult
        {
            TargetSymbol = symbol.Name,
            TargetSignature = targetSignature,
            Callers = callers,
            Metadata = new CallAnalysisMetadata
            {
                AnalysisDuration = analysisDuration,
                FilesAnalyzed = filesAnalyzed,
                MethodsAnalyzed = methodsAnalyzed,
                AnalysisMethod = "Roslyn SymbolFinder + Indirect Analysis"
            }
        };
    }

    private async Task<List<CallerInfo>> FindIndirectCallers(ISymbol targetSymbol, CancellationToken cancellationToken)
    {
        var indirectCallers = new List<CallerInfo>();
        if (!_workspace.IsInitialized)
            return indirectCallers;

        // If target method is part of an interface, find all implementations and their callers
        if (targetSymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            var solution = _workspace.Solution;
            var implementations = await SymbolFinder.FindImplementationsAsync(targetSymbol.ContainingType, solution, cancellationToken: cancellationToken);
            foreach (var impl in implementations)
            {
                var implMethod = impl.GetMembers(targetSymbol.Name).FirstOrDefault();
                if (implMethod != null)
                {
                    var implCallers = await FindCallersAsync(implMethod, cancellationToken);
                    if (implCallers != null)
                    {
                        foreach (var caller in implCallers.Callers)
                        {
                            // Mark as indirect call through interface
                            var indirectCaller = new CallerInfo
                            {
                                File = caller.File,
                                Line = caller.Line,
                                Column = caller.Column,
                                CallerMethod = caller.CallerMethod,
                                CallerType = caller.CallerType,
                                CallExpression = caller.CallExpression,
                                CallType = CallType.Indirect,
                                Confidence = ConfidenceLevel.Medium,
                                CallerSignature = caller.CallerSignature,
                                Context = caller.Context,
                                CallChain = caller.CallChain.Concat(new[] { targetSymbol.ContainingType.Name }).ToList()
                            };
                            indirectCallers.Add(indirectCaller);
                        }
                    }
                }
            }
        }

        return indirectCallers;
    }

    private async Task<CallerInfo?> AnalyzeCallerLocation(SymbolLocation location, ISymbol targetSymbol, CancellationToken cancellationToken)
    {
        var document = location.Document;
        if (document == null)
            return null;

        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (syntaxTree == null || semanticModel == null || location.Location == null)
            return null;

        var lineSpan = location.Location.GetLineSpan();
        var root = await syntaxTree.GetRootAsync(cancellationToken);
        var node = root.FindNode(location.Location.SourceSpan);

        // Find the containing method
        var methodNode = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (methodNode == null)
            return null;

        var callerSymbol = semanticModel.GetDeclaredSymbol(methodNode, cancellationToken);
        if (callerSymbol == null)
            return null;

        var callerSignature = CreateMethodSignature(callerSymbol);
        var text = await document.GetTextAsync(cancellationToken);
        var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString();

        // Determine call type and confidence
        var callType = DetermineCallType(node, targetSymbol);
        var confidence = DetermineConfidence(node);

        return new CallerInfo
        {
            File = document.FilePath ?? "",
            Line = lineSpan.StartLinePosition.Line,
            Column = lineSpan.StartLinePosition.Character,
            CallerMethod = callerSymbol.Name,
            CallerType = callerSymbol.ContainingType?.Name ?? "",
            CallExpression = node.ToString(),
            CallType = callType,
            Confidence = confidence,
            CallerSignature = callerSignature,
            Context = lineText.Trim(),
            IsRecursive = CrossProjectSymbolComparer.AreEquivalent(callerSymbol, targetSymbol)
        };
    }

    private CallType DetermineCallType(SyntaxNode node, ISymbol targetSymbol)
    {
        // For invocation expressions, check if they're through interface or base class
        if (node is InvocationExpressionSyntax invocation)
        {
            // Check if this is an interface method call
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var semanticModel = _workspace.GetSemanticModel(memberAccess.SyntaxTree);
                if (semanticModel != null)
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
                    var calledSymbol = symbolInfo.Symbol;

                    // If the called symbol is from an interface, mark as indirect
                    if (calledSymbol?.ContainingType?.TypeKind == TypeKind.Interface)
                        return CallType.Indirect;

                    // If the target symbol is from an interface but we're calling through implementation
                    if (targetSymbol.ContainingType?.TypeKind == TypeKind.Interface)
                        return CallType.Indirect;
                }
            }
            return CallType.Direct;
        }

        // For member access expressions
        if (node is MemberAccessExpressionSyntax memberAccessExpr)
        {
            var semanticModel = _workspace.GetSemanticModel(memberAccessExpr.SyntaxTree);
            if (semanticModel != null)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(memberAccessExpr);
                if (symbolInfo.Symbol?.ContainingType?.TypeKind == TypeKind.Interface)
                    return CallType.Indirect;
            }
        }

        return CallType.Unknown;
    }

    private static ConfidenceLevel DetermineConfidence(SyntaxNode node)
    {
        // This is a simplified implementation
        if (node is InvocationExpressionSyntax)
            return ConfidenceLevel.High;

        if (node is MemberAccessExpressionSyntax)
            return ConfidenceLevel.Medium;

        return ConfidenceLevel.Low;
    }

    /// <summary>
    /// Manual search for method calls when SymbolFinder doesn't work
    /// </summary>
    private async Task<List<CallerInfo>> FindCallersManuallyAsync(ISymbol targetSymbol, CancellationToken cancellationToken)
    {
        var callers = new List<CallerInfo>();

        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            var semanticModel = _workspace.GetSemanticModel(syntaxTree);
            if (semanticModel == null)
                continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken);

            // Find all method declarations
            var methodDeclarations = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDeclarations)
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl, cancellationToken);
                if (methodSymbol == null || CrossProjectSymbolComparer.AreEquivalent(methodSymbol, targetSymbol))
                    continue; // Skip the target method itself

                // Find all invocation expressions within this method
                var invocations = methodDecl.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>();

                foreach (var invocation in invocations)
                {
                    var invocationSymbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol;

                    // Check for direct match
                    if (invocationSymbol != null && CrossProjectSymbolComparer.AreEquivalent(invocationSymbol, targetSymbol))
                    {
                        var lineSpan = invocation.GetLocation().GetLineSpan();
                        var callType = DetermineCallType(invocation, targetSymbol);

                        callers.Add(new CallerInfo
                        {
                            CallerMethod = methodSymbol.Name,
                            CallerType = methodSymbol.ContainingType?.ToDisplayString() ?? "",
                            CallerSignature = CreateMethodSignature(methodSymbol),
                            File = syntaxTree.FilePath ?? "",
                            Line = lineSpan.StartLinePosition.Line,
                            Column = lineSpan.StartLinePosition.Character,
                            CallType = callType,
                            Confidence = DetermineConfidence(invocation),
                            CallExpression = invocation.ToString(),
                            Context = invocation.Parent?.ToString() ?? "",
                            CallChain = new List<string>(),
                            IsRecursive = methodSymbol.ContainingType != null && CrossProjectSymbolComparer.AreEquivalent(methodSymbol.ContainingType, targetSymbol.ContainingType)
                        });
                    }
                    // Check for interface method calls matching by name and parameter count
                    else if (invocationSymbol != null &&
                             targetSymbol.ContainingType?.TypeKind == TypeKind.Interface &&
                             invocationSymbol.Name == targetSymbol.Name &&
                             invocationSymbol is IMethodSymbol invocationMethod &&
                             targetSymbol is IMethodSymbol targetMethod &&
                             invocationMethod.Parameters.Length == targetMethod.Parameters.Length)
                    {
                        var lineSpan = invocation.GetLocation().GetLineSpan();

                        callers.Add(new CallerInfo
                        {
                            CallerMethod = methodSymbol.Name,
                            CallerType = methodSymbol.ContainingType?.ToDisplayString() ?? "",
                            CallerSignature = CreateMethodSignature(methodSymbol),
                            File = syntaxTree.FilePath ?? "",
                            Line = lineSpan.StartLinePosition.Line,
                            Column = lineSpan.StartLinePosition.Character,
                            CallType = CallType.Indirect,
                            Confidence = ConfidenceLevel.Medium,
                            CallExpression = invocation.ToString(),
                            Context = invocation.Parent?.ToString() ?? "",
                            CallChain = new List<string>(),
                            IsRecursive = false
                        });
                    }
                }
            }
        }

        return callers;
    }
}
