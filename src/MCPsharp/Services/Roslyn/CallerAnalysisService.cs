using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for analyzing who calls a specific method or symbol
/// </summary>
public partial class CallerAnalysisService : ICallerAnalysisService
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

        return await FindCallersCoreAsync(targetSymbol, startTime, cancellationToken);
    }

    public async Task<CallerResult?> FindCallersAsync(ISymbol symbol, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        return await FindCallersCoreAsync(symbol, startTime, cancellationToken);
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

        return await FindCallersCoreAsync(methodSymbol, startTime, cancellationToken);
    }

    public async Task<CallerResult?> FindCallersBySignatureAsync(MethodSignature signature, bool exactMatch = false, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // Find all method symbols with this name in every project of the solution
        var methodSymbols = _workspace.FindSourceSymbols(signature.Name, SymbolFilter.Member, cancellationToken)
            .OfType<IMethodSymbol>();

        // Find the first method that matches the signature
        var matchingSymbol = methodSymbols
            .FirstOrDefault(s => MatchesSignature(s, signature, exactMatch));

        if (matchingSymbol == null)
            return null;

        return await FindCallersCoreAsync(matchingSymbol, startTime, cancellationToken);
    }

    public async Task<CallerResult?> FindDirectCallersAsync(string methodName, string? containingType = null, CancellationToken cancellationToken = default)
    {
        var result = await FindCallersAsync(methodName, containingType, cancellationToken);
        if (result == null)
            return null;

        // Filter to only direct callers
        var directCallers = result.DirectCallers.ToList();

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
        var indirectCallers = result.IndirectCallers.ToList();

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
        if (callerResult.Callers.Count > 0)
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
                var context = caller.Context?.ToLowerInvariant() ?? "";
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
        if (!_workspace.IsInitialized)
            return new List<MethodSignature>();

        var unusedMethods = new List<MethodSignature>();

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
                var references = await SymbolFinder.FindReferencesAsync(symbol, _workspace.Solution, cancellationToken);
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
        if (!_workspace.IsInitialized)
            return new List<MethodSignature>();

        var testOnlyMethods = new List<MethodSignature>();

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

                // Find references to this method
                var references = await SymbolFinder.FindReferencesAsync(symbol, _workspace.Solution, cancellationToken);
                var referenceFiles = references
                    .SelectMany(r => r.Locations)
                    .Select(l => l.Document.FilePath ?? "")
                    .ToList();

                // Check if all references are in test files
                if (referenceFiles.All(f => IsTestFile(f)) && referenceFiles.Count > 0)
                {
                    testOnlyMethods.Add(CreateMethodSignature(symbol));
                }
            }
        }

        return testOnlyMethods;
    }
}
