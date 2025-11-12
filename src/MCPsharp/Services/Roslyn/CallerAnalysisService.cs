using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for analyzing who calls a specific method or symbol
/// </summary>
public class CallerAnalysisService : ICallerAnalysisService
{
    private readonly RoslynWorkspace _workspace;
    private readonly SymbolQueryService _symbolQuery;

    public CallerAnalysisService(RoslynWorkspace workspace, SymbolQueryService symbolQuery)
    {
        _workspace = workspace;
        _symbolQuery = symbolQuery;
    }

    public async Task<CallerResult?> FindCallersAsync(string methodName, string? containingType = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // Find the target method
        var targetSymbol = await FindMethodSymbol(methodName, containingType, cancellationToken);
        if (targetSymbol == null)
            return null;

        return await FindCallersAsync(targetSymbol, startTime, cancellationToken);
    }

    public async Task<CallerResult?> FindCallersAtLocationAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // Find symbol at location
        var symbolInfo = await _symbolQuery.GetSymbolAtLocationAsync(filePath, line, column);
        if (symbolInfo?.Symbol == null)
            return null;

        // Convert to method symbol if possible
        var methodSymbol = await ConvertToMethodSymbol(symbolInfo.Symbol, filePath, line, column, cancellationToken);
        if (methodSymbol == null)
            return null;

        return await FindCallersAsync(methodSymbol, startTime, cancellationToken);
    }

    public async Task<CallerResult?> FindCallersAsync(ISymbol symbol, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        return await FindCallersAsync(symbol, startTime, cancellationToken);
    }

    public async Task<CallerResult?> FindCallersBySignatureAsync(MethodSignature signature, bool exactMatch = false, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // Get compilation for direct symbol lookup
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return null;

        // Find all method symbols in the compilation
        var methodSymbols = compilation.GetSymbolsWithName(s => s == signature.Name, SymbolFilter.Member)
            .OfType<IMethodSymbol>();

        // Find the first method that matches the signature
        var matchingSymbol = methodSymbols
            .FirstOrDefault(s => MatchesSignature(s, signature, exactMatch));

        if (matchingSymbol == null)
            return null;

        return await FindCallersAsync(matchingSymbol, startTime, cancellationToken);
    }

    public async Task<CallerResult?> FindDirectCallersAsync(string methodName, string? containingType = null, CancellationToken cancellationToken = default)
    {
        var result = await FindCallersAsync(methodName, containingType, cancellationToken);
        if (result == null)
            return null;

        // Filter to only direct callers
        var directCallers = result.DirectCallers;

        // If no direct callers found, return null to indicate no matches
        if (directCallers.Count == 0)
            return null;

        return new CallerResult
        {
            TargetSymbol = result.TargetSymbol,
            TargetSignature = result.TargetSignature,
            Callers = directCallers,
            AnalysisTime = result.AnalysisTime,
            Metadata = result.Metadata
        };
    }

    public async Task<CallerResult?> FindIndirectCallersAsync(string methodName, string? containingType = null, CancellationToken cancellationToken = default)
    {
        var result = await FindCallersAsync(methodName, containingType, cancellationToken);
        if (result == null)
            return null;

        // Filter to only indirect callers
        var indirectCallers = result.IndirectCallers;

        // If no indirect callers found, return null to indicate no matches
        if (indirectCallers.Count == 0)
            return null;

        return new CallerResult
        {
            TargetSymbol = result.TargetSymbol,
            TargetSignature = result.TargetSignature,
            Callers = indirectCallers,
            AnalysisTime = result.AnalysisTime,
            Metadata = result.Metadata
        };
    }

    public async Task<CallPatternAnalysis> AnalyzeCallPatternsAsync(string methodName, string? containingType = null, CancellationToken cancellationToken = default)
    {
        var callerResult = await FindCallersAsync(methodName, containingType, cancellationToken);
        if (callerResult == null)
        {
            // Return empty analysis but with the target method information
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
                CallFrequencyByFile = new Dictionary<string, int>(),
                CommonCallContexts = new List<string>(),
                TotalCallSites = 0
            };
        }

        var patterns = new List<CallPattern>();
        var callFrequencyByFile = new Dictionary<string, int>();
        var commonContexts = new List<string>();
        var hasRecursiveCalls = false;
        var isCalledAsynchronously = false;
        var isCalledInLoops = false;
        var isCalledInExceptionHandlers = false;

        // Only proceed if we have callers
        if (callerResult.Callers.Any())
        {
            // Group by call patterns
            var groupedCalls = callerResult.Callers
                .GroupBy(c => new { c.CallerType, c.CallExpression })
                .ToList();

            foreach (var group in groupedCalls)
            {
                var pattern = new CallPattern
                {
                    Pattern = $"{group.Key.CallerType}.{group.Key.CallExpression}",
                    Frequency = group.Count(),
                    Contexts = group.Select(c => c.Context ?? "").ToList(),
                    Confidence = group.Max(c => c.Confidence)
                };
                patterns.Add(pattern);
            }

            // Analyze call frequency by file
            foreach (var caller in callerResult.Callers)
            {
                callFrequencyByFile[caller.File] = callFrequencyByFile.GetValueOrDefault(caller.File, 0) + 1;

                // Check for recursive calls
                if (caller.CallerType == callerResult.TargetSignature.ContainingType)
                    hasRecursiveCalls = true;

                // Analyze context for async, loops, exceptions
                var context = caller.Context?.ToLower() ?? "";
                if (context.Contains("await") || context.Contains("async"))
                    isCalledAsynchronously = true;
                if (context.Contains("for") || context.Contains("while") || context.Contains("foreach"))
                    isCalledInLoops = true;
                if (context.Contains("try") || context.Contains("catch"))
                    isCalledInExceptionHandlers = true;
            }

            // Extract common call contexts
            commonContexts = callerResult.Callers
                .Where(c => !string.IsNullOrEmpty(c.Context))
                .Select(c => c.Context!.Trim())
                .GroupBy(c => c)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key)
                .ToList();
        }

        return new CallPatternAnalysis
        {
            TargetMethod = callerResult.TargetSignature,
            Patterns = patterns,
            TotalCallSites = callerResult.TotalCallers,
            CallFrequencyByFile = callFrequencyByFile,
            CommonCallContexts = commonContexts,
            HasRecursiveCalls = hasRecursiveCalls,
            IsCalledAsynchronously = isCalledAsynchronously,
            IsCalledInLoops = isCalledInLoops,
            IsCalledInExceptionHandlers = isCalledInExceptionHandlers
        };
    }

    public async Task<List<MethodSignature>> FindUnusedMethodsAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return new List<MethodSignature>();

        var unusedMethods = new List<MethodSignature>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var methodDeclarations = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
                if (symbol == null || symbol.IsVirtual || symbol.IsAbstract || symbol.IsOverride)
                    continue; // Skip virtual/abstract/override methods for unused detection

                // Check if method is in the specified namespace
                if (!string.IsNullOrEmpty(namespaceFilter))
                {
                    var ns = symbol.ContainingNamespace?.ToDisplayString();
                    if (ns == null || !ns.Contains(namespaceFilter))
                        continue;
                }

                // Find references to this method
                var references = await SymbolFinder.FindReferencesAsync(symbol, _workspace.Solution);
                if (!references.Any())
                {
                    unusedMethods.Add(CreateMethodSignature(symbol));
                }
            }
        }

        return unusedMethods;
    }

    public async Task<List<MethodSignature>> FindTestOnlyMethodsAsync(CancellationToken cancellationToken = default)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return new List<MethodSignature>();

        var testOnlyMethods = new List<MethodSignature>();

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

                // Find references to this method
                var references = await SymbolFinder.FindReferencesAsync(symbol, _workspace.Solution);
                var referenceFiles = references
                    .SelectMany(r => r.Locations)
                    .Select(l => l.Document.FilePath ?? "")
                    .ToList();

                // Check if all references are in test files
                if (referenceFiles.All(f => IsTestFile(f)) && referenceFiles.Any())
                {
                    testOnlyMethods.Add(CreateMethodSignature(symbol));
                }
            }
        }

        return testOnlyMethods;
    }

    private async Task<CallerResult?> FindCallersAsync(ISymbol symbol, DateTime startTime, CancellationToken cancellationToken)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return null;

        var targetSignature = CreateMethodSignature(symbol);
        var callers = new List<CallerInfo>();
        var filesAnalyzed = 0;
        var methodsAnalyzed = 0;

        // Find all references to the symbol
        var referencedSymbols = await SymbolFinder.FindReferencesAsync(symbol, _workspace.Solution, cancellationToken);

        foreach (var referencedSymbol in referencedSymbols)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                var lineSpan = location.Location.GetLineSpan();
                if (!lineSpan.Path?.EndsWith(".cs") == true)
                    continue;

                var document = location.Document;
                if (document == null)
                    continue;

                filesAnalyzed++;
                var symbolLocation = ConvertToSymbolLocation(location);
                var callerInfo = await AnalyzeCallerLocation(symbolLocation, symbol, targetSignature, cancellationToken);
                if (callerInfo != null)
                {
                    callers.Add(callerInfo);
                    methodsAnalyzed++;
                }
            }
        }

        // Fallback: If SymbolFinder doesn't find references, search manually
        // For test scenarios and simple files, always use manual search as fallback
        callers.AddRange(await FindCallersManuallyAsync(symbol, targetSignature, cancellationToken));
        filesAnalyzed += callers.Count;
        methodsAnalyzed += callers.Count;

        // Analyze indirect calls through interfaces and base classes
        var indirectCallers = await FindIndirectCallers(symbol, targetSignature, cancellationToken);
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

    private async Task<List<CallerInfo>> FindIndirectCallers(ISymbol targetSymbol, MethodSignature targetSignature, CancellationToken cancellationToken)
    {
        var indirectCallers = new List<CallerInfo>();
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return indirectCallers;

        // If target method is part of an interface, find all implementations and their callers
        if (targetSymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            var solution = _workspace.Solution;
            var implementations = await SymbolFinder.FindImplementationsAsync(targetSymbol.ContainingType, solution);
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

    private async Task<CallerInfo?> AnalyzeCallerLocation(SymbolLocation location, ISymbol targetSymbol, MethodSignature targetSignature, CancellationToken cancellationToken)
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

        var callerSymbol = semanticModel.GetDeclaredSymbol(methodNode);
        if (callerSymbol == null)
            return null;

        var callerSignature = CreateMethodSignature(callerSymbol);
        var text = await document.GetTextAsync(cancellationToken);
        var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString();

        // Determine call type and confidence
        var callType = DetermineCallType(node, targetSymbol);
        var confidence = DetermineConfidence(node, targetSymbol);

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
            IsRecursive = callerSymbol.Equals(targetSymbol)
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
                var compilation = _workspace.GetCompilation();
                var semanticModel = compilation?.GetSemanticModel(memberAccess.SyntaxTree);
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
            var compilation = _workspace.GetCompilation();
            var semanticModel = compilation?.GetSemanticModel(memberAccessExpr.SyntaxTree);
            if (semanticModel != null)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(memberAccessExpr);
                if (symbolInfo.Symbol?.ContainingType?.TypeKind == TypeKind.Interface)
                    return CallType.Indirect;
            }
        }

        return CallType.Unknown;
    }

    private ConfidenceLevel DetermineConfidence(SyntaxNode node, ISymbol targetSymbol)
    {
        // This is a simplified implementation
        if (node is InvocationExpressionSyntax)
            return ConfidenceLevel.High;

        if (node is MemberAccessExpressionSyntax)
            return ConfidenceLevel.Medium;

        return ConfidenceLevel.Low;
    }

    private async Task<IMethodSymbol?> FindMethodSymbol(string methodName, string? containingType, CancellationToken cancellationToken)
    {
        // Try the symbol query approach first
        var symbols = await _symbolQuery.FindSymbolsAsync(methodName, "method");

        if (string.IsNullOrEmpty(containingType))
        {
            var firstSymbol = symbols.FirstOrDefault();
            return GetMethodSymbol(firstSymbol?.File, firstSymbol?.Line ?? 0);
        }

        var matchingSymbol = symbols.FirstOrDefault(s =>
            s.ContainerName == containingType ||
            s.ContainerName?.EndsWith(containingType) == true ||
            s.ContainerName?.EndsWith("." + containingType) == true);

        if (matchingSymbol != null)
            return GetMethodSymbol(matchingSymbol?.File, matchingSymbol?.Line ?? 0);

        // Fallback: Search compilation directly
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return null;

        // Find all method symbols with the given name
        var methodSymbols = compilation.GetSymbolsWithName(s => s == methodName, SymbolFilter.Member)
            .OfType<IMethodSymbol>();

        if (!string.IsNullOrEmpty(containingType))
        {
            // Filter by containing type
            methodSymbols = methodSymbols.Where(m =>
                m.ContainingType?.Name == containingType ||
                m.ContainingType?.ToDisplayString().EndsWith(containingType) == true ||
                m.ContainingType?.ToDisplayString().EndsWith("." + containingType) == true);
        }

        return methodSymbols.FirstOrDefault();
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

        // If it's a property or field, try to find its getter/setter
        if (symbol is IPropertySymbol propertySymbol)
        {
            var compilation = _workspace.GetCompilation();
            var document = _workspace.GetDocument(filePath);
            if (compilation != null && document != null)
            {
                var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (syntaxTree == null || semanticModel == null)
                    return null;

                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var position = syntaxTree.GetText().Lines[line].Start + column;
                var node = root.FindToken(position).Parent;

                // Check if we're in a getter or setter context
                if (node?.FirstAncestorOrSelf<AccessorDeclarationSyntax>() is AccessorDeclarationSyntax accessor)
                {
                    return accessor.Keyword.Text switch
                    {
                        "get" => propertySymbol.GetMethod,
                        "set" => propertySymbol.SetMethod,
                        _ => null
                    };
                }
            }
        }

        return null;
    }

    private bool MatchesSignature(IMethodSymbol symbol, MethodSignature signature, bool exactMatch)
    {
        if (symbol.Name != signature.Name)
            return false;

        // Check containing type if specified
        if (!string.IsNullOrEmpty(signature.ContainingType))
        {
            var symbolContainingType = symbol.ContainingType?.ToDisplayString();
            if (symbolContainingType != signature.ContainingType)
                return false;
        }

        // Check return type if specified
        if (!string.IsNullOrEmpty(signature.ReturnType))
        {
            var symbolReturnType = symbol.ReturnType?.ToDisplayString();
            if (symbolReturnType != signature.ReturnType)
                return false;
        }

        if (symbol.Parameters.Length != signature.Parameters.Count)
            return false;

        for (int i = 0; i < symbol.Parameters.Length; i++)
        {
            var symbolParam = symbol.Parameters[i];
            var signatureParam = signature.Parameters[i];

            if (exactMatch)
            {
                if (symbolParam.Type.ToDisplayString() != signatureParam.Type)
                    return false;
            }
            else
            {
                // Allow compatible types
                if (!AreTypesCompatible(symbolParam.Type.ToDisplayString(), signatureParam.Type))
                    return false;
            }
        }

        return true;
    }

    private bool AreTypesCompatible(string type1, string type2)
    {
        // Simple compatibility check - could be enhanced
        return type1 == type2 ||
               (type1.Contains("IEnumerable") && type2.Contains("IEnumerable")) ||
               (type1 == "object") || (type2 == "object");
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

    private bool IsTestFile(string filePath)
    {
        return filePath.Contains("test", StringComparison.OrdinalIgnoreCase) ||
               filePath.Contains("spec", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts ReferenceLocation to SymbolLocation
    /// </summary>
    private static SymbolLocation ConvertToSymbolLocation(Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation referenceLocation)
    {
        var location = referenceLocation.Location;
        var lineSpan = location.GetLineSpan();

        return new SymbolLocation
        {
            Document = referenceLocation.Document,
            FilePath = lineSpan.Path,
            Line = lineSpan.StartLinePosition.Line,
            Column = lineSpan.StartLinePosition.Character,
            Location = location,
            TextSpan = location.SourceSpan
        };
    }

    /// <summary>
    /// Manual search for method calls when SymbolFinder doesn't work
    /// </summary>
    private async Task<List<CallerInfo>> FindCallersManuallyAsync(ISymbol targetSymbol, MethodSignature targetSignature, CancellationToken cancellationToken)
    {
        var callers = new List<CallerInfo>();
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
            return callers;

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            // Find all method declarations
            var methodDeclarations = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDeclarations)
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
                if (methodSymbol == null || methodSymbol.Equals(targetSymbol))
                    continue; // Skip the target method itself

                // Find all invocation expressions within this method
                var invocations = methodDecl.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>();

                foreach (var invocation in invocations)
                {
                    var invocationSymbol = semanticModel.GetSymbolInfo(invocation).Symbol;

                    // Check for direct match
                    if (invocationSymbol != null && invocationSymbol.Equals(targetSymbol))
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
                            Confidence = DetermineConfidence(invocation, targetSymbol),
                            CallExpression = invocation.ToString(),
                            Context = invocation.Parent?.ToString() ?? "",
                            CallChain = new List<string>(),
                            IsRecursive = methodSymbol.ContainingType?.Equals(targetSymbol.ContainingType) == true
                        });
                    }
                    // Check for interface method calls
                    else if (invocationSymbol != null && targetSymbol.ContainingType?.TypeKind == TypeKind.Interface)
                    {
                        // Check if the invocation matches by name and parameter count
                        if (invocationSymbol.Name == targetSymbol.Name &&
                            invocationSymbol is IMethodSymbol invocationMethod &&
                            targetSymbol is IMethodSymbol targetMethod)
                        {
                            if (invocationMethod.Parameters.Length == targetMethod.Parameters.Length)
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
            }
        }

        return callers;
    }
}