using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.LargeFileOptimization;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Phase3.LargeFileOptimization;

/// <summary>
/// Service for detecting and analyzing God class anti-patterns
/// </summary>
internal static class GodClassAnalyzer
{
    private const int GodClassThreshold = 20; // methods
    private const int MaxFieldCount = 20;
    private const int MaxResponsibilities = 5;

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    public static async Task<GodClassAnalysis> AnalyzeGodClass(
        ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel,
        string filePath,
        CancellationToken cancellationToken)
    {
        var className = classDecl.Identifier.Text;
        var violations = new List<string>();
        var responsibilities = IdentifyClassResponsibilities(classDecl);
        var methods = classDecl.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        var fields = classDecl.DescendantNodes().OfType<FieldDeclarationSyntax>().ToList();

        // Calculate God class score
        var godClassScore = CalculateGodClassScore(classDecl, methods, fields, responsibilities);

        // Identify violations
        if (methods.Count > GodClassThreshold)
            violations.Add($"Too many methods: {methods.Count} (threshold: {GodClassThreshold})");

        if (fields.Count > MaxFieldCount)
            violations.Add($"Too many fields: {fields.Count} (threshold: {MaxFieldCount})");

        if (responsibilities.Count > MaxResponsibilities)
            violations.Add($"Too many responsibilities: {responsibilities.Count} (threshold: {MaxResponsibilities})");

        // Find problematic methods
        var tooManyMethods = methods
            .Where(m => m.GetText().Lines.Count > 50)
            .Select(m => $"{m.Identifier.Text} ({m.GetText().Lines.Count} lines)")
            .ToList();

        var tooManyFields = fields
            .Select(f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text)
            .Where(n => !string.IsNullOrEmpty(n))
            .OfType<string>()
            .ToList();

        // Analyze coupling
        var highCouplingClasses = IdentifyHighCouplingClasses(classDecl, semanticModel);

        // Generate recommended splits
        var recommendedSplits = GenerateClassSplittingStrategies(classDecl, responsibilities);

        var severity = DetermineGodClassSeverity(godClassScore);

        return new GodClassAnalysis
        {
            ClassName = className,
            FilePath = filePath,
            GodClassScore = godClassScore,
            Severity = severity,
            Violations = violations,
            Responsibilities = responsibilities,
            TooManyMethods = tooManyMethods,
            TooManyFields = tooManyFields,
            HighCouplingClasses = highCouplingClasses,
            RecommendedSplits = recommendedSplits
        };
    }

    public static double CalculateGodClassScore(
        ClassDeclarationSyntax classDecl,
        List<MethodDeclarationSyntax> methods,
        List<FieldDeclarationSyntax> fields,
        List<string> responsibilities)
    {
        var methodScore = Math.Min(1.0, methods.Count / (double)GodClassThreshold);
        var fieldScore = Math.Min(1.0, fields.Count / (double)MaxFieldCount);
        var responsibilityScore = Math.Min(1.0, responsibilities.Count / (double)MaxResponsibilities);
        var lineCountScore = Math.Min(1.0, classDecl.GetText().Lines.Count / 500.0);

        return (methodScore + fieldScore + responsibilityScore + lineCountScore) / 4.0;
    }

    public static GodClassSeverity DetermineGodClassSeverity(double score)
    {
        if (score <= 0.3) return GodClassSeverity.None;
        if (score <= 0.5) return GodClassSeverity.Low;
        if (score <= 0.7) return GodClassSeverity.Medium;
        if (score <= 0.9) return GodClassSeverity.High;
        return GodClassSeverity.Critical;
    }

    private static List<string> IdentifyClassResponsibilities(
        ClassDeclarationSyntax classDecl)
    {
        var methodNames = classDecl.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(m => m.Identifier.Text)
            .ToList();

        // Group methods by naming patterns to identify responsibilities
        var responsibilitiesByPattern = new Dictionary<string, List<string>>();

        foreach (var methodName in methodNames)
        {
            var pattern = IdentifyResponsibilityPattern(methodName);
            if (!responsibilitiesByPattern.TryGetValue(pattern, out var value))
            {
                value = new List<string>();
                responsibilitiesByPattern[pattern] = value;
            }

            value.Add(methodName);
        }

        return responsibilitiesByPattern.Keys.ToList();
    }

    private static string IdentifyResponsibilityPattern(string methodName)
    {
        if (methodName.StartsWith("Get", StringComparison.Ordinal) || methodName.StartsWith("Set", StringComparison.Ordinal))
            return "Data Access";
        if (methodName.StartsWith("Validate", StringComparison.Ordinal) || methodName.StartsWith("Check", StringComparison.Ordinal))
            return "Validation";
        if (methodName.StartsWith("Calculate", StringComparison.Ordinal) || methodName.StartsWith("Compute", StringComparison.Ordinal))
            return "Calculation";
        if (methodName.StartsWith("Save", StringComparison.Ordinal) || methodName.StartsWith("Load", StringComparison.Ordinal) || methodName.StartsWith("Delete", StringComparison.Ordinal))
            return "Persistence";
        if (methodName.StartsWith("Render", StringComparison.Ordinal) || methodName.StartsWith("Draw", StringComparison.Ordinal))
            return "Rendering";
        if (methodName.StartsWith("Send", StringComparison.Ordinal) || methodName.StartsWith("Receive", StringComparison.Ordinal))
            return "Communication";
        if (methodName.StartsWith("Parse", StringComparison.Ordinal) || methodName.StartsWith("Format", StringComparison.Ordinal))
            return "Data Processing";

        return "Other";
    }

    private static List<string> IdentifyHighCouplingClasses(
        ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel)
    {
        var highCouplingClasses = new List<string>();
        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);

        if (classSymbol != null)
        {
            // This is a simplified analysis
            // In practice, you'd analyze method calls, field usage, etc.
        }

        return highCouplingClasses;
    }

    private static List<SplittingStrategy> GenerateClassSplittingStrategies(
        ClassDeclarationSyntax classDecl,
        List<string> responsibilities)
    {
        var strategies = new List<SplittingStrategy>();
        var className = classDecl.Identifier.Text;

        foreach (var responsibility in responsibilities.Take(3))
        {
            strategies.Add(new SplittingStrategy
            {
                StrategyName = $"Extract {responsibility}",
                Description = $"Extract {responsibility.ToLowerInvariant()} functionality into a separate class",
                TargetMembers = new List<string>(),
                NewClassName = $"{className}{responsibility.Replace(" ", "")}",
                SplitType = SplitType.GroupByResponsibility,
                Confidence = 0.8,
                EstimatedEffortHours = 4,
                Pros = new List<string> { "Single Responsibility Principle", "Better testability" },
                Cons = new List<string> { "Initial complexity", "Requires refactoring" }
            });
        }

        return strategies;
    }
}
