using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.LargeFileOptimization;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Phase3.LargeFileOptimization;

/// <summary>
/// Service for calculating code complexity metrics (cyclomatic, cognitive, Halstead, etc.)
/// </summary>
internal class ComplexityCalculator
{
    private const int HighComplexityThreshold = 10;
    private const int MaxNestingDepth = 4;

    public async Task<ComplexityMetrics> CalculateFileComplexity(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var cyclomaticComplexity = 0;
        var cognitiveComplexity = 0;
        var maxNestingDepth = 0;
        var decisionPoints = 0;
        var hotspots = new List<ComplexityHotspot>();

        // Traverse all methods in the file
        var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

        foreach (var method in methodDeclarations)
        {
            var methodComplexity = await CalculateMethodComplexity(method, semanticModel, cancellationToken);
            cyclomaticComplexity = Math.Max(cyclomaticComplexity, methodComplexity.CyclomaticComplexity);
            cognitiveComplexity = Math.Max(cognitiveComplexity, methodComplexity.CognitiveComplexity);
            maxNestingDepth = Math.Max(maxNestingDepth, methodComplexity.MaximumNestingDepth);
            decisionPoints += methodComplexity.NumberOfDecisionPoints;
            hotspots.AddRange(methodComplexity.Hotspots);
        }

        // Calculate simplified Halstead metrics
        var text = root.GetText().ToString();
        var halsteadMetrics = CalculateHalsteadMetrics(text);

        // Calculate maintainability index (simplified)
        var maintainabilityIndex = CalculateMaintainabilityIndex(
            cyclomaticComplexity,
            text.Length,
            halsteadMetrics.volume);

        var complexityLevel = DetermineComplexityLevel(cyclomaticComplexity, cognitiveComplexity);

        return new ComplexityMetrics
        {
            CyclomaticComplexity = cyclomaticComplexity,
            CognitiveComplexity = cognitiveComplexity,
            HalsteadVolume = halsteadMetrics.volume,
            HalsteadDifficulty = halsteadMetrics.difficulty,
            MaintainabilityIndex = maintainabilityIndex,
            MaximumNestingDepth = maxNestingDepth,
            NumberOfDecisionPoints = decisionPoints,
            ComplexityLevel = complexityLevel,
            Hotspots = hotspots
        };
    }

    public async Task<ComplexityMetrics> CalculateMethodComplexity(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var cyclomaticComplexity = 1; // Base complexity
        var cognitiveComplexity = 0;
        var maxNestingDepth = 0;
        var decisionPoints = 0;
        var hotspots = new List<ComplexityHotspot>();

        // Analyze method body
        var body = method.Body;
        if (body != null)
        {
            var analyzer = new ComplexityAnalyzer();
            var result = analyzer.Analyze(body);

            cyclomaticComplexity += result.CyclomaticComplexity;
            cognitiveComplexity += result.CognitiveComplexity;
            maxNestingDepth = result.MaxNestingDepth;
            decisionPoints = result.DecisionPoints;

            // Identify complexity hotspots
            hotspots = IdentifyComplexityHotspots(body, result);
        }

        // Calculate Halstead metrics
        var methodText = method.GetText().ToString();
        var halsteadMetrics = CalculateHalsteadMetrics(methodText);

        // Calculate maintainability index
        var maintainabilityIndex = CalculateMaintainabilityIndex(
            cyclomaticComplexity,
            methodText.Length,
            halsteadMetrics.volume);

        var complexityLevel = DetermineComplexityLevel(cyclomaticComplexity, cognitiveComplexity);

        return new ComplexityMetrics
        {
            CyclomaticComplexity = cyclomaticComplexity,
            CognitiveComplexity = cognitiveComplexity,
            HalsteadVolume = halsteadMetrics.volume,
            HalsteadDifficulty = halsteadMetrics.difficulty,
            MaintainabilityIndex = maintainabilityIndex,
            MaximumNestingDepth = maxNestingDepth,
            NumberOfDecisionPoints = decisionPoints,
            ComplexityLevel = complexityLevel,
            Hotspots = hotspots
        };
    }

    public (int volume, int difficulty) CalculateHalsteadMetrics(string code)
    {
        // Simplified Halstead metrics calculation
        var operators = Regex.Matches(code, @"(\+|\-|\*|\/|%|=|==|!=|<|>|<=|>=|&&|\|\||!|\+\+|\-\-)").Count;
        var operands = Regex.Matches(code, @"\b[a-zA-Z_][a-zA-Z0-9_]*\b").Count;

        var vocabulary = operators + operands;
        var length = code.Length;

        var volume = vocabulary > 0 ? length * Math.Log(vocabulary, 2) : 0;
        var difficulty = operands > 0 ? (operators / 2.0) * (operands / 2.0) : 0;

        return ((int)volume, (int)difficulty);
    }

    public double CalculateMaintainabilityIndex(int cyclomaticComplexity, int linesOfCode, int halsteadVolume)
    {
        // Simplified maintainability index calculation
        if (halsteadVolume == 0) return 100.0;

        var maintainabilityIndex = 171.0
            - 5.2 * Math.Log(halsteadVolume)
            - 0.23 * cyclomaticComplexity
            - 16.2 * Math.Log(linesOfCode);

        return Math.Max(0, Math.Min(100, maintainabilityIndex));
    }

    public ComplexityLevel DetermineComplexityLevel(int cyclomatic, int cognitive)
    {
        var maxComplexity = Math.Max(cyclomatic, cognitive);

        if (maxComplexity <= 10) return ComplexityLevel.Simple;
        if (maxComplexity <= 20) return ComplexityLevel.Moderate;
        if (maxComplexity <= 50) return ComplexityLevel.Complex;
        return ComplexityLevel.VeryComplex;
    }

    private List<ComplexityHotspot> IdentifyComplexityHotspots(
        BlockSyntax body,
        ComplexityAnalyzer.AnalysisResult result)
    {
        var hotspots = new List<ComplexityHotspot>();

        // This is a simplified implementation
        // In practice, you'd identify specific complex blocks

        return hotspots;
    }
}
