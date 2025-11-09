using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.LargeFileOptimization;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Phase3.LargeFileOptimization;

/// <summary>
/// Service for detecting and analyzing God class anti-patterns
/// </summary>
internal class GodClassAnalyzer
{
    private const int GodClassThreshold = 20; // methods
    private const int MaxFieldCount = 20;
    private const int MaxResponsibilities = 5;

    public async Task<GodClassAnalysis> AnalyzeGodClass(
        ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel,
        string filePath,
        CancellationToken cancellationToken)
    {
        var className = classDecl.Identifier.Text;
        var violations = new List<string>();
        var responsibilities = IdentifyClassResponsibilities(classDecl, semanticModel);
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
            .ToList();

        // Analyze coupling
        var highCouplingClasses = IdentifyHighCouplingClasses(classDecl, semanticModel);

        // Generate recommended splits
        var recommendedSplits = GenerateClassSplittingStrategies(classDecl, semanticModel, responsibilities);

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

    public double CalculateGodClassScore(
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

    public GodClassSeverity DetermineGodClassSeverity(double score)
    {
        if (score <= 0.3) return GodClassSeverity.None;
        if (score <= 0.5) return GodClassSeverity.Low;
        if (score <= 0.7) return GodClassSeverity.Medium;
        if (score <= 0.9) return GodClassSeverity.High;
        return GodClassSeverity.Critical;
    }

    private List<string> IdentifyClassResponsibilities(
        ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel)
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
            if (!responsibilitiesByPattern.ContainsKey(pattern))
            {
                responsibilitiesByPattern[pattern] = new List<string>();
            }
            responsibilitiesByPattern[pattern].Add(methodName);
        }

        return responsibilitiesByPattern.Keys.ToList();
    }

    private string IdentifyResponsibilityPattern(string methodName)
    {
        if (methodName.StartsWith("Get") || methodName.StartsWith("Set"))
            return "Data Access";
        if (methodName.StartsWith("Validate") || methodName.StartsWith("Check"))
            return "Validation";
        if (methodName.StartsWith("Calculate") || methodName.StartsWith("Compute"))
            return "Calculation";
        if (methodName.StartsWith("Save") || methodName.StartsWith("Load") || methodName.StartsWith("Delete"))
            return "Persistence";
        if (methodName.StartsWith("Render") || methodName.StartsWith("Draw"))
            return "Rendering";
        if (methodName.StartsWith("Send") || methodName.StartsWith("Receive"))
            return "Communication";
        if (methodName.StartsWith("Parse") || methodName.StartsWith("Format"))
            return "Data Processing";

        return "Other";
    }

    private List<string> IdentifyHighCouplingClasses(
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

    private List<SplittingStrategy> GenerateClassSplittingStrategies(
        ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel,
        List<string> responsibilities)
    {
        var strategies = new List<SplittingStrategy>();
        var className = classDecl.Identifier.Text;

        foreach (var responsibility in responsibilities.Take(3))
        {
            strategies.Add(new SplittingStrategy
            {
                StrategyName = $"Extract {responsibility}",
                Description = $"Extract {responsibility.ToLower()} functionality into a separate class",
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
