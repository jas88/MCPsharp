using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models;

namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Determines whether a duplicated code block can be extracted into a shared method, using Roslyn
/// data-flow and control-flow analysis when a semantic model is available and syntax heuristics otherwise.
/// </summary>
public static class ExtractionDependencyAnalyzer
{
    /// <summary>Name used for the extracted method in suggested signatures</summary>
    public const string ExtractedMethodName = "ExtractedMethod";

    /// <summary>
    /// Analyses extraction of a single block.
    /// </summary>
    /// <param name="node">Method, constructor or block node to extract</param>
    /// <param name="semanticModel">Semantic model for the node's tree, or <c>null</c> for syntax-only analysis</param>
    /// <param name="filePath">File containing the node (for reporting)</param>
    /// <param name="startLine">Starting line of the node (for reporting)</param>
    public static BlockExtractionAnalysis Analyze(SyntaxNode node, SemanticModel? semanticModel, string filePath, int startLine)
    {
        ArgumentNullException.ThrowIfNull(node);

        var region = ExtractionRegion.From(node);
        if (region == null)
        {
            return ExtractionAnalysisBuilder.NotApplicable(filePath, startLine, node.Kind().ToString());
        }

        if (semanticModel != null && semanticModel.SyntaxTree == node.SyntaxTree)
        {
            var semantic = AnalyzeSemantic(region, semanticModel, filePath, startLine);
            if (semantic != null)
            {
                return semantic;
            }
        }

        return SyntacticExtractionAnalyzer.Analyze(region, filePath, startLine);
    }

    /// <summary>
    /// Aggregates block analyses of a duplicate group into a single safety verdict.
    /// </summary>
    /// <param name="blocks">Code blocks of the group paired with their analyses</param>
    /// <param name="requireStaticTarget">Whether the shared method must be static (e.g. a utility class)</param>
    public static ExtractionSafetyResult AnalyzeGroup(
        IReadOnlyList<(CodeBlock Block, BlockExtractionAnalysis Analysis)> blocks,
        bool requireStaticTarget = false)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var reasons = new List<string>();
        var warnings = new List<string>();

        foreach (var (_, analysis) in blocks)
        {
            reasons.AddRange(analysis.UnsafeReasons.Select(r => $"{Path.GetFileName(analysis.FilePath)}:{analysis.StartLine}: {r}"));
            warnings.AddRange(analysis.Warnings.Select(w => $"{Path.GetFileName(analysis.FilePath)}:{analysis.StartLine}: {w}"));
        }

        var containingTypes = blocks
            .Select(b => $"{b.Block.Namespace}.{b.Block.ContainingType}".Trim('.'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (containingTypes.Count > 1)
        {
            var typeList = string.Join(", ", containingTypes);
            if (blocks.Any(b => b.Analysis.RequiresInstanceAccess))
            {
                reasons.Add($"Blocks in different types ({typeList}) use instance state; a single shared method cannot access both");
            }

            var nonPublic = blocks.SelectMany(b => b.Analysis.ReferencedNonPublicMembers).Distinct(StringComparer.Ordinal).ToList();
            if (nonPublic.Count > 0)
            {
                reasons.Add($"Blocks in different types ({typeList}) reference non-public members ({string.Join(", ", nonPublic)}) that a shared method cannot access");
            }
        }

        if (requireStaticTarget && blocks.Any(b => b.Analysis.RequiresInstanceAccess))
        {
            reasons.Add("A static utility method cannot access the instance members used by the duplicated code");
        }

        var parameterCounts = blocks.Select(b => b.Analysis.Parameters.Count).Distinct().ToList();
        if (parameterCounts.Count > 1)
        {
            warnings.Add($"Blocks require different numbers of parameters ({string.Join(" vs ", parameterCounts)}); the shared method must be parameterised");
        }

        var returnCounts = blocks.Select(b => b.Analysis.ReturnValues.Count).Distinct().ToList();
        if (returnCounts.Count > 1)
        {
            warnings.Add($"Blocks produce different numbers of results ({string.Join(" vs ", returnCounts)})");
        }

        return new ExtractionSafetyResult
        {
            IsSafe = reasons.Count == 0 && blocks.All(b => b.Analysis.IsSafe),
            UnsafeReasons = reasons,
            Warnings = warnings,
            Blocks = blocks.Select(b => b.Analysis).ToList()
        };
    }

    private static BlockExtractionAnalysis? AnalyzeSemantic(
        ExtractionRegion region,
        SemanticModel model,
        string filePath,
        int startLine)
    {
        DataFlowAnalysis? dataFlow;
        ControlFlowAnalysis? controlFlow = null;

        if (region.Expression != null)
        {
            dataFlow = model.AnalyzeDataFlow(region.Expression);
        }
        else if (region.Statements.Count == 0)
        {
            return ExtractionAnalysisBuilder.Empty(filePath, startLine, ExtractionAnalysisMode.Semantic);
        }
        else
        {
            dataFlow = model.AnalyzeDataFlow(region.Statements[0], region.Statements[^1]);
            controlFlow = model.AnalyzeControlFlow(region.Statements[0], region.Statements[^1]);
        }

        if (dataFlow == null || !dataFlow.Succeeded || (controlFlow != null && !controlFlow.Succeeded))
        {
            return null;
        }

        var builder = new ExtractionAnalysisBuilder(filePath, startLine, ExtractionAnalysisMode.Semantic);

        foreach (var symbol in dataFlow.DataFlowsIn.Where(s => s is not IParameterSymbol { IsThis: true }))
        {
            builder.Parameters.Add(new ExtractionVariable { Name = symbol.Name, Type = TypeOf(symbol)?.ToDisplayString() });
        }

        AnalyzeOutputs(region, dataFlow, builder);

        var capturedOuter = dataFlow.CapturedInside.Except(dataFlow.VariablesDeclared, SymbolEqualityComparer.Default).ToList();
        builder.CapturedVariables.AddRange(capturedOuter.Select(s => s.Name));
        if (capturedOuter.Count > 0)
        {
            builder.Warnings.Add($"Lambdas capture outer variable(s) {string.Join(", ", builder.CapturedVariables)}; they will be passed by value to the extracted method");
        }

        var usesThis = dataFlow.ReadInside.Concat(dataFlow.WrittenInside).Any(s => s is IParameterSymbol { IsThis: true });
        AnalyzeMemberReferences(region, model, builder);
        builder.RequiresInstanceAccess |= usesThis;

        if (controlFlow != null && !region.IsWholeBody)
        {
            AnalyzeControlFlow(region, controlFlow, builder);
        }

        AnalyzeAsyncAndIterators(region, builder);
        builder.SuggestedSignature = BuildSignature(region, model, builder);
        return builder.Build();
    }

    private static void AnalyzeOutputs(ExtractionRegion region, DataFlowAnalysis dataFlow, ExtractionAnalysisBuilder builder)
    {
        // For a whole body only ref/out parameters can carry values out; locals go out of scope
        var outputs = region.IsWholeBody
            ? dataFlow.DataFlowsOut.Where(s => s is IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out })
            : dataFlow.DataFlowsOut;

        foreach (var symbol in outputs)
        {
            builder.ReturnValues.Add(new ExtractionVariable { Name = symbol.Name, Type = TypeOf(symbol)?.ToDisplayString() });
        }

        if (builder.ReturnValues.Count > 0)
        {
            var names = string.Join(", ", builder.ReturnValues.Select(v => v.Name));
            builder.Warnings.Add(builder.ReturnValues.Count == 1
                ? $"Assigns outer variable '{names}' which is read afterwards; the extracted method must return it"
                : $"Assigns outer variables {names} which are read afterwards; the extracted method must return a tuple or use out parameters");
        }
    }

    private static void AnalyzeMemberReferences(ExtractionRegion region, SemanticModel model, ExtractionAnalysisBuilder builder)
    {
        var containingType = model.GetEnclosingSymbol(region.SpanStart)?.ContainingType;
        var nonPublic = new HashSet<string>(StringComparer.Ordinal);
        var outerLocalFunctions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in region.Nodes.SelectMany(n => n.DescendantNodesAndSelf()))
        {
            if (node is ThisExpressionSyntax or BaseExpressionSyntax)
            {
                builder.RequiresInstanceAccess = true;
                continue;
            }

            if (node is not SimpleNameSyntax name)
            {
                continue;
            }

            var symbol = model.GetSymbolInfo(name).Symbol;
            if (symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFunction)
            {
                var declaration = localFunction.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                if (declaration != null && declaration.SyntaxTree == name.SyntaxTree && !region.Contains(declaration))
                {
                    outerLocalFunctions.Add(localFunction.Name);
                }

                continue;
            }

            if (symbol is not (IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol) || symbol.ContainingType == null)
            {
                continue;
            }

            if (!symbol.IsStatic && !HasExplicitReceiver(name))
            {
                builder.RequiresInstanceAccess = true;
            }

            if (symbol.DeclaredAccessibility is Microsoft.CodeAnalysis.Accessibility.Private
                    or Microsoft.CodeAnalysis.Accessibility.Protected
                    or Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal
                && containingType != null
                && IsSameOrBaseType(symbol.ContainingType, containingType))
            {
                nonPublic.Add($"{symbol.ContainingType.Name}.{symbol.Name}");
            }
        }

        builder.ReferencedNonPublicMembers.AddRange(nonPublic.Order(StringComparer.Ordinal));
        if (outerLocalFunctions.Count > 0)
        {
            builder.UnsafeReasons.Add($"Calls local function(s) {string.Join(", ", outerLocalFunctions)} declared outside the block");
        }
    }

    private static void AnalyzeControlFlow(ExtractionRegion region, ControlFlowAnalysis controlFlow, ExtractionAnalysisBuilder builder)
    {
        if (!controlFlow.ReturnStatements.IsEmpty)
        {
            builder.ContainsReturnStatement = true;
            builder.UnsafeReasons.Add("Contains a return statement; the extracted method cannot return from its caller");
        }

        var jumps = controlFlow.ExitPoints.Where(e => e is not ReturnStatementSyntax).ToList();
        if (jumps.Count > 0)
        {
            builder.ContainsJumpOutOfBlock = true;
            var kinds = string.Join(", ", jumps.Select(j => j.Kind().ToString()).Distinct(StringComparer.Ordinal));
            builder.UnsafeReasons.Add($"Contains jump statement(s) ({kinds}) that leave the block");
        }

        if (region.Statements.Count > 0 && !controlFlow.EndPointIsReachable && controlFlow.ReturnStatements.IsEmpty && jumps.Count == 0)
        {
            builder.Warnings.Add("The end of the block is unreachable (it always throws)");
        }
    }

    private static void AnalyzeAsyncAndIterators(ExtractionRegion region, ExtractionAnalysisBuilder builder)
    {
        var ownNodes = region.Nodes
            .SelectMany(n => n.DescendantNodesAndSelf(d => d is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
            .ToList();

        if (!region.IsWholeBody && ownNodes.Exists(n => n is YieldStatementSyntax))
        {
            builder.UnsafeReasons.Add("Contains yield statements; iterator code cannot be extracted into a separate method");
        }

        if (ownNodes.Exists(IsAsynchronous))
        {
            builder.Warnings.Add("Contains await expressions; the extracted method must be async");
        }
    }

    internal static bool IsAsynchronous(SyntaxNode node) => node switch
    {
        AwaitExpressionSyntax => true,
        CommonForEachStatementSyntax forEach => !forEach.AwaitKeyword.IsKind(SyntaxKind.None),
        UsingStatementSyntax usingStatement => !usingStatement.AwaitKeyword.IsKind(SyntaxKind.None),
        LocalDeclarationStatementSyntax declaration => !declaration.AwaitKeyword.IsKind(SyntaxKind.None),
        _ => false
    };

    private static string BuildSignature(ExtractionRegion region, SemanticModel model, ExtractionAnalysisBuilder builder)
    {
        string returnType;
        if (region.IsWholeBody)
        {
            var declared = model.GetDeclaredSymbol(region.Owner is BlockSyntax { Parent: { } parent } ? parent : region.Owner);
            returnType = declared is IMethodSymbol { MethodKind: not MethodKind.Constructor } method
                ? method.ReturnType.ToDisplayString()
                : "void";
        }
        else
        {
            returnType = ExtractionAnalysisBuilder.ReturnTypeFor(builder.ReturnValues);
        }

        return builder.FormatSignature(returnType);
    }

    private static bool HasExplicitReceiver(SimpleNameSyntax name) =>
        name.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == name =>
                access.Expression is not (ThisExpressionSyntax or BaseExpressionSyntax),
            MemberBindingExpressionSyntax => true,
            _ => false
        };

    private static bool IsSameOrBaseType(INamedTypeSymbol candidate, INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, candidate.OriginalDefinition))
            {
                return true;
            }
        }

        // Members of enclosing types are also accessible from nested types
        return type.ContainingType != null && IsSameOrBaseType(candidate, type.ContainingType);
    }

    private static ITypeSymbol? TypeOf(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        _ => null
    };
}
