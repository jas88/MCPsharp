using MCPsharp.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Heuristic, syntax-only extraction analysis used when no semantic model is available
/// </summary>
internal static class SyntacticExtractionAnalyzer
{
    public static BlockExtractionAnalysis Analyze(ExtractionRegion region, string filePath, int startLine)
    {
        var builder = new ExtractionAnalysisBuilder(filePath, startLine, ExtractionAnalysisMode.SyntaxOnly);
        builder.Warnings.Add("No semantic model available; dependency analysis is based on syntax only");

        var regionNodes = region.Nodes.SelectMany(n => n.DescendantNodesAndSelf()).ToList();
        var declaredInside = CollectDeclaredNames(regionNodes);
        var function = FindEnclosingFunction(region.Owner);
        var knownOuterLocals = CollectOuterLocals(function, region);

        var referenced = regionNodes
            .OfType<IdentifierNameSyntax>()
            .Where(IsVariableReference)
            .ToList();

        var freeNames = referenced
            .Select(n => n.Identifier.ValueText)
            .Where(name => !declaredInside.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var name in freeNames.Where(knownOuterLocals.Contains))
        {
            builder.Parameters.Add(new ExtractionVariable { Name = name });
        }

        var unresolved = freeNames.Where(n => !knownOuterLocals.Contains(n)).ToList();
        var usesThis = regionNodes.Exists(n => n is ThisExpressionSyntax or BaseExpressionSyntax);
        builder.RequiresInstanceAccess = usesThis || unresolved.Count > 0;
        if (unresolved.Count > 0)
        {
            builder.Warnings.Add($"Could not resolve {string.Join(", ", unresolved)} without a semantic model; assuming they may be instance members");
        }

        if (!region.IsWholeBody && function != null)
        {
            AnalyzeOutputs(region, function, regionNodes, knownOuterLocals, declaredInside, builder);
        }

        AnalyzeCaptures(regionNodes, knownOuterLocals, declaredInside, builder);

        if (!region.IsWholeBody)
        {
            AnalyzeJumps(region, regionNodes, builder);
        }

        builder.SuggestedSignature = builder.FormatSignature(
            region.IsWholeBody ? WholeBodyReturnType(region) : ExtractionAnalysisBuilder.ReturnTypeFor(builder.ReturnValues));
        return builder.Build();
    }

    private static void AnalyzeOutputs(
        ExtractionRegion region,
        SyntaxNode function,
        List<SyntaxNode> regionNodes,
        HashSet<string> knownOuterLocals,
        HashSet<string> declaredInside,
        ExtractionAnalysisBuilder builder)
    {
        var written = regionNodes
            .Select(WrittenName)
            .OfType<string>()
            .Where(name => knownOuterLocals.Contains(name) && !declaredInside.Contains(name))
            .ToHashSet(StringComparer.Ordinal);

        var readAfter = function.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(n => n.SpanStart >= region.SpanEnd && IsVariableReference(n))
            .Select(n => n.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in written.Where(readAfter.Contains).Order(StringComparer.Ordinal))
        {
            builder.ReturnValues.Add(new ExtractionVariable { Name = name });
        }

        if (builder.ReturnValues.Count > 0)
        {
            builder.Warnings.Add(
                $"Assigns outer variable(s) {string.Join(", ", builder.ReturnValues.Select(v => v.Name))} which are read afterwards; the extracted method must return them");
        }
    }

    private static void AnalyzeCaptures(
        List<SyntaxNode> regionNodes,
        HashSet<string> knownOuterLocals,
        HashSet<string> declaredInside,
        ExtractionAnalysisBuilder builder)
    {
        var captured = regionNodes
            .Where(n => n is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            .SelectMany(n => n.DescendantNodes().OfType<IdentifierNameSyntax>())
            .Select(n => n.Identifier.ValueText)
            .Where(name => knownOuterLocals.Contains(name) && !declaredInside.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        builder.CapturedVariables.AddRange(captured);
        if (captured.Count > 0)
        {
            builder.Warnings.Add($"Lambdas capture outer variable(s) {string.Join(", ", captured)}; they will be passed by value to the extracted method");
        }
    }

    private static void AnalyzeJumps(ExtractionRegion region, List<SyntaxNode> regionNodes, ExtractionAnalysisBuilder builder)
    {
        var ownNodes = region.Nodes
            .SelectMany(n => n.DescendantNodesAndSelf(d => d is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
            .ToList();

        if (ownNodes.Exists(n => n is ReturnStatementSyntax))
        {
            builder.ContainsReturnStatement = true;
            builder.UnsafeReasons.Add("Contains a return statement; the extracted method cannot return from its caller");
        }

        var escapingJumps = ownNodes
            .Where(n => n is GotoStatementSyntax ||
                        (n is BreakStatementSyntax or ContinueStatementSyntax && !region.Contains(JumpTarget(n) ?? region.Owner.Parent ?? region.Owner)))
            .Select(n => n.Kind().ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (escapingJumps.Count > 0)
        {
            builder.ContainsJumpOutOfBlock = true;
            builder.UnsafeReasons.Add($"Contains jump statement(s) ({string.Join(", ", escapingJumps)}) that leave the block");
        }

        if (ownNodes.Exists(n => n is YieldStatementSyntax))
        {
            builder.UnsafeReasons.Add("Contains yield statements; iterator code cannot be extracted into a separate method");
        }

        if (regionNodes.Exists(ExtractionDependencyAnalyzer.IsAsynchronous))
        {
            builder.Warnings.Add("Contains await expressions; the extracted method must be async");
        }
    }

    private static SyntaxNode? JumpTarget(SyntaxNode jump)
    {
        for (var current = jump.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                case ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax:
                    return current;
                case SwitchStatementSyntax when jump is BreakStatementSyntax:
                    return current;
                case AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax:
                    return null;
            }
        }

        return null;
    }

    private static HashSet<string> CollectDeclaredNames(IEnumerable<SyntaxNode> nodes)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var name = node switch
            {
                VariableDeclaratorSyntax declarator => declarator.Identifier.ValueText,
                ForEachStatementSyntax forEach => forEach.Identifier.ValueText,
                SingleVariableDesignationSyntax designation => designation.Identifier.ValueText,
                ParameterSyntax parameter => parameter.Identifier.ValueText,
                CatchDeclarationSyntax catchDeclaration => catchDeclaration.Identifier.ValueText,
                LocalFunctionStatementSyntax localFunction => localFunction.Identifier.ValueText,
                _ => null
            };

            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static HashSet<string> CollectOuterLocals(SyntaxNode? function, ExtractionRegion region)
    {
        if (function == null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var candidates = function.DescendantNodesAndSelf(n => n == function || n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .Where(n => !region.Contains(n) && (n is ParameterSyntax || n.SpanStart < region.SpanStart));
        return CollectDeclaredNames(candidates);
    }

    private static SyntaxNode? FindEnclosingFunction(SyntaxNode node) =>
        node.AncestorsAndSelf().FirstOrDefault(n => n is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);

    private static bool IsVariableReference(IdentifierNameSyntax name)
    {
        if (name.IsVar || SyntaxFacts.IsInTypeOnlyContext(name))
        {
            return false;
        }

        return name.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Expression == name,
            MemberBindingExpressionSyntax => false,
            QualifiedNameSyntax => false,
            NameColonSyntax => false,
            InvocationExpressionSyntax invocation when invocation.Expression == name => false,
            _ => true
        };
    }

    private static string? WrittenName(SyntaxNode node) => node switch
    {
        AssignmentExpressionSyntax { Left: IdentifierNameSyntax left } => left.Identifier.ValueText,
        PrefixUnaryExpressionSyntax { Operand: IdentifierNameSyntax operand } prefix
            when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression) => operand.Identifier.ValueText,
        PostfixUnaryExpressionSyntax { Operand: IdentifierNameSyntax operand } postfix
            when postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression) => operand.Identifier.ValueText,
        ArgumentSyntax { RefKindKeyword.RawKind: not 0, Expression: IdentifierNameSyntax argument } => argument.Identifier.ValueText,
        _ => null
    };

    private static string WholeBodyReturnType(ExtractionRegion region)
    {
        var owner = region.Owner is BlockSyntax { Parent: { } parent } ? parent : region.Owner;
        return owner switch
        {
            MethodDeclarationSyntax method => method.ReturnType.ToString(),
            LocalFunctionStatementSyntax localFunction => localFunction.ReturnType.ToString(),
            _ => "void"
        };
    }
}
