using MCPsharp.Models.LargeFileOptimization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// God-method detection for <see cref="LargeFileOptimizerService"/>.
/// </summary>
public partial class LargeFileOptimizerService
{
    #region God Method Analysis

    private static GodMethodAnalysis AnalyzeGodMethod(MethodDeclarationSyntax methodDecl, string filePath)
    {
        var methodName = methodDecl.Identifier.Text;
        var className = methodDecl.FirstAncestorOrSelf<ClassDeclarationSyntax>()?.Identifier.Text ?? "Unknown";
        var violations = new List<string>();

        var lineCount = methodDecl.GetText().Lines.Count;
        var parameterCount = methodDecl.ParameterList.Parameters.Count;

        // Count local variables
        var localVariableCount = 0;
        // Count control structures
        var loopCount = 0;
        var conditionalCount = 0;
        var tryCatchCount = 0;

        if (methodDecl.Body != null)
        {
            var localDeclarations = methodDecl.Body.DescendantNodes()
                .OfType<LocalDeclarationStatementSyntax>();
            localVariableCount = localDeclarations.Count();

            loopCount = methodDecl.Body.DescendantNodes()
                .Count(n => n is ForStatementSyntax || n is WhileStatementSyntax || n is ForEachStatementSyntax);

            conditionalCount = methodDecl.Body.DescendantNodes()
                .Count(n => n is IfStatementSyntax || n is SwitchStatementSyntax);

            tryCatchCount = methodDecl.Body.DescendantNodes()
                .Count(n => n is TryStatementSyntax);
        }

        // Calculate complexity
        var complexity = CalculateMethodComplexity(methodDecl);

        // Calculate God method score
        var godMethodScore = CalculateGodMethodScore(lineCount, parameterCount, localVariableCount, complexity);

        // Identify violations
        if (lineCount > GodMethodThreshold)
            violations.Add($"Too many lines: {lineCount} (threshold: {GodMethodThreshold})");

        if (parameterCount > MaxParameters)
            violations.Add($"Too many parameters: {parameterCount} (threshold: {MaxParameters})");

        if (localVariableCount > 10)
            violations.Add($"Too many local variables: {localVariableCount} (threshold: 10)");

        if (complexity.CyclomaticComplexity > HighComplexityThreshold)
            violations.Add($"Too high cyclomatic complexity: {complexity.CyclomaticComplexity} (threshold: {HighComplexityThreshold})");

        if (complexity.CognitiveComplexity > CognitiveComplexityThreshold)
            violations.Add($"Too high cognitive complexity: {complexity.CognitiveComplexity} (threshold: {CognitiveComplexityThreshold})");

        if (complexity.MaximumNestingDepth > MaxNestingDepthThreshold)
            violations.Add($"Too deeply nested: {complexity.MaximumNestingDepth} levels (threshold: {MaxNestingDepthThreshold})");

        // Find problematic parameters

        // Find problematic local variables
        var tooManyLocals = new List<string>();
        if (methodDecl.Body != null)
        {
            tooManyLocals = methodDecl.Body.DescendantNodes()
                .OfType<LocalDeclarationStatementSyntax>()
                .SelectMany(ld => ld.Declaration.Variables)
                .Select(v => v.Identifier.Text)
                .ToList();
        }

        // Find high complexity blocks
        var highComplexityBlocks = DescribeComplexBlocks(complexity);

        // Generate recommended refactorings first
        var methodMetrics = new MethodMetrics
        {
            Complexity = complexity,
            LineCount = lineCount,
            ParameterCount = parameterCount,
            LocalVariableCount = localVariableCount,
            LoopCount = loopCount,
            ConditionalCount = conditionalCount,
            TryCatchCount = tryCatchCount,
            MaximumNestingDepth = complexity.MaximumNestingDepth,
            GodMethodScore = new GodMethodAnalysis
            {
                MethodName = methodName,
                ClassName = className,
                FilePath = filePath,
                GodMethodScore = 0,
                Severity = GodMethodSeverity.None,
                Violations = new List<string>(),
                TooManyParameters = new List<string>(),
                TooManyLocals = new List<string>(),
                HighComplexityBlocks = new List<string>(),
                RecommendedRefactorings = new List<MethodRefactoringStrategy>()
            }, // Placeholder
            IsTooLarge = lineCount > GodMethodThreshold,
            HasTooManyParameters = parameterCount > MaxParameters,
            IsTooComplex = IsTooComplex(complexity)
        };

        var recommendedRefactorings = GenerateMethodRefactoringStrategies(methodMetrics);

        // Create a GodMethodAnalysis object for the metrics
        var godMethodAnalysis = new GodMethodAnalysis
        {
            MethodName = methodName,
            ClassName = className,
            FilePath = filePath,
            GodMethodScore = godMethodScore,
            Severity = DetermineGodMethodSeverity(godMethodScore),
            Violations = violations,
            TooManyParameters = methodDecl.ParameterList.Parameters.Select(p => p.Identifier.Text).ToList(),
            TooManyLocals = tooManyLocals,
            HighComplexityBlocks = highComplexityBlocks,
            RecommendedRefactorings = recommendedRefactorings
        };

        return godMethodAnalysis;
    }

    private static double CalculateGodMethodScore(int lineCount, int parameterCount, int localVariableCount, ComplexityMetrics complexity)
    {
        var lineScore = Math.Min(1.0, lineCount / (double)GodMethodThreshold);
        var parameterScore = Math.Min(1.0, parameterCount / (double)MaxParameters);
        var localScore = Math.Min(1.0, localVariableCount / 10.0);
        var complexityScore = Math.Min(1.0, Math.Max(
            complexity.CyclomaticComplexity / (double)HighComplexityThreshold,
            complexity.CognitiveComplexity / (double)CognitiveComplexityThreshold));

        return (lineScore + parameterScore + localScore + complexityScore) / 4.0;
    }

    private static GodMethodSeverity DetermineGodMethodSeverity(double score)
    {
        if (score <= 0.3)
            return GodMethodSeverity.None;
        if (score <= 0.5)
            return GodMethodSeverity.Low;
        if (score <= 0.7)
            return GodMethodSeverity.Medium;
        if (score <= 0.9)
            return GodMethodSeverity.High;
        return GodMethodSeverity.Critical;
    }

    #endregion
}
