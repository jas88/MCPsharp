using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.LargeFileOptimization;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Phase3.LargeFileOptimization;

/// <summary>
/// Service for detecting and analyzing God method anti-patterns
/// </summary>
internal class GodMethodAnalyzer
{
    private const int GodMethodThreshold = 50; // lines
    private const int MaxParameters = 7;
    private const int HighComplexityThreshold = 10;
    private const int MaxLocalVariables = 10;

    public async Task<GodMethodAnalysis> AnalyzeGodMethod(
        MethodDeclarationSyntax methodDecl,
        SemanticModel semanticModel,
        string filePath,
        ComplexityCalculator complexityCalculator,
        CancellationToken cancellationToken)
    {
        var methodName = methodDecl.Identifier.Text;
        var className = methodDecl.FirstAncestorOrSelf<ClassDeclarationSyntax>()?.Identifier.Text ?? "Unknown";
        var violations = new List<string>();

        var lineCount = methodDecl.GetText().Lines.Count;
        var parameterCount = methodDecl.ParameterList.Parameters.Count;

        // Count local variables
        var localVariableCount = 0;
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
        var complexity = await complexityCalculator.CalculateMethodComplexity(methodDecl, semanticModel, cancellationToken);

        // Calculate God method score
        var godMethodScore = CalculateGodMethodScore(lineCount, parameterCount, localVariableCount, complexity.CyclomaticComplexity);

        // Identify violations
        if (lineCount > GodMethodThreshold)
            violations.Add($"Too many lines: {lineCount} (threshold: {GodMethodThreshold})");

        if (parameterCount > MaxParameters)
            violations.Add($"Too many parameters: {parameterCount} (threshold: {MaxParameters})");

        if (localVariableCount > MaxLocalVariables)
            violations.Add($"Too many local variables: {localVariableCount} (threshold: {MaxLocalVariables})");

        if (complexity.CyclomaticComplexity > HighComplexityThreshold)
            violations.Add($"Too high cyclomatic complexity: {complexity.CyclomaticComplexity} (threshold: {HighComplexityThreshold})");

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

        // Generate recommended refactorings
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
            IsTooLarge = lineCount > GodMethodThreshold,
            HasTooManyParameters = parameterCount > MaxParameters,
            IsTooComplex = complexity.CyclomaticComplexity > HighComplexityThreshold
        };

        var recommendedRefactorings = GenerateMethodRefactoringStrategies(methodDecl, semanticModel, methodMetrics);

        return new GodMethodAnalysis
        {
            MethodName = methodName,
            ClassName = className,
            FilePath = filePath,
            GodMethodScore = godMethodScore,
            Severity = DetermineGodMethodSeverity(godMethodScore),
            Violations = violations,
            TooManyParameters = methodDecl.ParameterList.Parameters.Select(p => p.Identifier.Text).ToList(),
            TooManyLocals = tooManyLocals,
            HighComplexityBlocks = new List<string>(),
            RecommendedRefactorings = recommendedRefactorings
        };
    }

    public double CalculateGodMethodScore(
        int lineCount,
        int parameterCount,
        int localVariableCount,
        int cyclomaticComplexity)
    {
        var lineScore = Math.Min(1.0, lineCount / (double)GodMethodThreshold);
        var parameterScore = Math.Min(1.0, parameterCount / (double)MaxParameters);
        var localScore = Math.Min(1.0, localVariableCount / 10.0);
        var complexityScore = Math.Min(1.0, cyclomaticComplexity / (double)HighComplexityThreshold);

        return (lineScore + parameterScore + localScore + complexityScore) / 4.0;
    }

    public GodMethodSeverity DetermineGodMethodSeverity(double score)
    {
        if (score <= 0.3) return GodMethodSeverity.None;
        if (score <= 0.5) return GodMethodSeverity.Low;
        if (score <= 0.7) return GodMethodSeverity.Medium;
        if (score <= 0.9) return GodMethodSeverity.High;
        return GodMethodSeverity.Critical;
    }

    private List<MethodRefactoringStrategy> GenerateMethodRefactoringStrategies(
        MethodDeclarationSyntax methodDecl,
        SemanticModel semanticModel,
        MethodMetrics metrics)
    {
        var strategies = new List<MethodRefactoringStrategy>();

        if (metrics.LineCount > GodMethodThreshold)
        {
            strategies.Add(new MethodRefactoringStrategy
            {
                StrategyName = "Extract Method",
                Description = "Extract logical sections of the method into separate methods",
                RefactoringType = MethodRefactoringType.ExtractMethod,
                TargetLines = new List<string>(),
                Confidence = 0.9,
                EstimatedEffortHours = 2,
                Pros = new List<string> { "Improved readability", "Better reusability", "Easier testing" },
                Cons = new List<string> { "May increase class size", "Additional method calls" }
            });
        }

        if (metrics.ParameterCount > MaxParameters)
        {
            strategies.Add(new MethodRefactoringStrategy
            {
                StrategyName = "Introduce Parameter Object",
                Description = "Group related parameters into a parameter object",
                RefactoringType = MethodRefactoringType.IntroduceParameterObject,
                TargetLines = new List<string>(),
                Confidence = 0.8,
                EstimatedEffortHours = 3,
                Pros = new List<string> { "Cleaner method signature", "Better encapsulation", "Easier maintenance" },
                Cons = new List<string> { "Requires new class", "API changes" }
            });
        }

        if (metrics.Complexity.CyclomaticComplexity > HighComplexityThreshold)
        {
            strategies.Add(new MethodRefactoringStrategy
            {
                StrategyName = "Replace Conditional with Polymorphism",
                Description = "Replace complex conditional logic with polymorphic dispatch",
                RefactoringType = MethodRefactoringType.ReplaceConditional,
                TargetLines = new List<string>(),
                Confidence = 0.7,
                EstimatedEffortHours = 6,
                Pros = new List<string> { "Reduced complexity", "Better extensibility", "Cleaner code" },
                Cons = new List<string> { "Requires new classes", "More complex structure" }
            });
        }

        return strategies;
    }
}
