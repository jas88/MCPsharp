using System.Text.RegularExpressions;
using MCPsharp.Models.LargeFileOptimization;
using MCPsharp.Services.Phase3.LargeFileOptimization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Complexity measurement and hotspot detection for <see cref="LargeFileOptimizerService"/>.
/// </summary>
public partial class LargeFileOptimizerService
{
    /// <summary>Cognitive complexity above which a member is a hotspot (SonarSource S3776 default).</summary>
    private const int CognitiveComplexityThreshold = 15;

    /// <summary>Nesting depth above which a member is a hotspot (matches the code-smell detector's conditional nesting limit).</summary>
    private const int MaxNestingDepthThreshold = 4;

    /// <summary>Cognitive complexity at or above which a single top-level control structure is a hotspot.</summary>
    private const int ComplexBlockCognitiveThreshold = 8;

    private const string HighCyclomaticHotspot = "HighCyclomaticComplexity";
    private const string HighCognitiveHotspot = "HighCognitiveComplexity";
    private const string DeepNestingHotspot = "DeepNesting";
    private const string ComplexBlockHotspot = "ComplexBlock";

    /// <summary>
    /// Aggregates member metrics for a whole file: cyclomatic, cognitive and nesting are the worst member's
    /// values (so <see cref="ComplexityLevel"/> keeps its per-member scale); decision points and hotspots are summed.
    /// </summary>
    private static ComplexityMetrics CalculateFileComplexity(SyntaxNode root, CancellationToken cancellationToken) =>
        AggregateComplexity(root, CodeComplexityCalculator.AnalyzeMembers(root, cancellationToken));

    /// <summary>Aggregates member metrics for one type, using the same rules as <see cref="CalculateFileComplexity"/>.</summary>
    private static ComplexityMetrics CalculateClassComplexity(ClassDeclarationSyntax classDecl, CancellationToken cancellationToken) =>
        AggregateComplexity(classDecl, CodeComplexityCalculator.AnalyzeMembers(classDecl, cancellationToken));

    private static ComplexityMetrics CalculateMethodComplexity(SyntaxNode member)
    {
        var function = CodeComplexityCalculator.Analyze(member);
        var text = member.GetText().ToString();
        var (volume, difficulty) = CalculateHalsteadMetrics(text);

        return new ComplexityMetrics
        {
            CyclomaticComplexity = function.CyclomaticComplexity,
            CognitiveComplexity = function.CognitiveComplexity,
            HalsteadVolume = volume,
            HalsteadDifficulty = difficulty,
            MaintainabilityIndex = CalculateMaintainabilityIndex(function.CyclomaticComplexity, text.Length, volume),
            MaximumNestingDepth = function.MaxNestingDepth,
            NumberOfDecisionPoints = function.DecisionPoints,
            ComplexityLevel = DetermineComplexityLevel(function.CyclomaticComplexity, function.CognitiveComplexity),
            Hotspots = IdentifyComplexityHotspots(function)
        };
    }

    private static ComplexityMetrics AggregateComplexity(SyntaxNode scope, IReadOnlyList<FunctionComplexity> members)
    {
        var cyclomatic = members.Count == 0 ? 0 : members.Max(m => m.CyclomaticComplexity);
        var cognitive = members.Count == 0 ? 0 : members.Max(m => m.CognitiveComplexity);
        var text = scope.GetText().ToString();
        var (volume, difficulty) = CalculateHalsteadMetrics(text);

        return new ComplexityMetrics
        {
            CyclomaticComplexity = cyclomatic,
            CognitiveComplexity = cognitive,
            HalsteadVolume = volume,
            HalsteadDifficulty = difficulty,
            MaintainabilityIndex = CalculateMaintainabilityIndex(cyclomatic, text.Length, volume),
            MaximumNestingDepth = members.Count == 0 ? 0 : members.Max(m => m.MaxNestingDepth),
            NumberOfDecisionPoints = members.Sum(m => m.DecisionPoints),
            ComplexityLevel = DetermineComplexityLevel(cyclomatic, cognitive),
            Hotspots = members.SelectMany(IdentifyComplexityHotspots).ToList()
        };
    }

    private static (int volume, int difficulty) CalculateHalsteadMetrics(string code)
    {
        // Simplified Halstead metrics calculation
        var operators = Regex.Count(code, @"(\+|\-|\*|\/|%|=|==|!=|<|>|<=|>=|&&|\|\||!|\+\+|\-\-)");
        var operands = Regex.Count(code, @"\b[a-zA-Z_][a-zA-Z0-9_]*\b");

        var vocabulary = operators + operands;
        var length = code.Length;

        var volume = vocabulary > 0 ? length * Math.Log(vocabulary, 2) : 0;
        var difficulty = operands > 0 ? (operators / 2.0) * (operands / 2.0) : 0;

        return ((int)volume, (int)difficulty);
    }

    private static double CalculateMaintainabilityIndex(int cyclomaticComplexity, int linesOfCode, int halsteadVolume)
    {
        // Simplified maintainability index calculation
        if (halsteadVolume == 0) return 100.0;

        var maintainabilityIndex = 171.0
            - 5.2 * Math.Log(halsteadVolume)
            - 0.23 * cyclomaticComplexity
            - 16.2 * Math.Log(linesOfCode);

        return Math.Max(0, Math.Min(100, maintainabilityIndex));
    }

    private static ComplexityLevel DetermineComplexityLevel(int cyclomatic, int cognitive)
    {
        var maxComplexity = Math.Max(cyclomatic, cognitive);

        if (maxComplexity <= 10) return ComplexityLevel.Simple;
        if (maxComplexity <= 20) return ComplexityLevel.Moderate;
        if (maxComplexity <= 50) return ComplexityLevel.Complex;
        return ComplexityLevel.VeryComplex;
    }

    /// <summary>
    /// Reports a member as a hotspot when its cyclomatic complexity exceeds <see cref="HighComplexityThreshold"/>,
    /// its cognitive complexity exceeds <see cref="CognitiveComplexityThreshold"/> or its nesting exceeds
    /// <see cref="MaxNestingDepthThreshold"/>; additionally reports each top-level control structure whose own
    /// cognitive complexity reaches <see cref="ComplexBlockCognitiveThreshold"/>.
    /// </summary>
    private static List<ComplexityHotspot> IdentifyComplexityHotspots(FunctionComplexity function)
    {
        var hotspots = new List<ComplexityHotspot>();
        var member = $"{function.Kind.ToLowerInvariant()} '{function.Name}'";

        if (function.CognitiveComplexity > CognitiveComplexityThreshold)
        {
            hotspots.Add(CreateHotspot(function, HighCognitiveHotspot, function.CognitiveComplexity,
                $"Cognitive complexity of {member} is {function.CognitiveComplexity} (threshold: {CognitiveComplexityThreshold})",
                "Extract nested logic into well-named methods and flatten control flow with guard clauses"));
        }

        if (function.CyclomaticComplexity > HighComplexityThreshold)
        {
            hotspots.Add(CreateHotspot(function, HighCyclomaticHotspot, function.CyclomaticComplexity,
                $"Cyclomatic complexity of {member} is {function.CyclomaticComplexity} (threshold: {HighComplexityThreshold})",
                "Reduce the number of independent paths: split the method or replace conditionals with polymorphism or lookup tables"));
        }

        if (function.MaxNestingDepth > MaxNestingDepthThreshold)
        {
            hotspots.Add(CreateHotspot(function, DeepNestingHotspot, function.MaxNestingDepth,
                $"Nesting depth of {member} is {function.MaxNestingDepth} (threshold: {MaxNestingDepthThreshold})",
                "Invert conditions into early returns and extract inner loops into separate methods"));
        }

        foreach (var block in function.Blocks.Where(b => b.CognitiveComplexity >= ComplexBlockCognitiveThreshold))
        {
            hotspots.Add(new ComplexityHotspot
            {
                StartLine = block.StartLine,
                EndLine = block.EndLine,
                HotspotType = ComplexBlockHotspot,
                LocalComplexity = block.CognitiveComplexity,
                Description = $"'{block.Construct}' block in {member} has cognitive complexity {block.CognitiveComplexity}",
                Suggestion = "Extract this block into a separate method"
            });
        }

        return hotspots;
    }

    private static ComplexityHotspot CreateHotspot(FunctionComplexity function, string type, int complexity, string description, string suggestion) =>
        new()
        {
            StartLine = function.StartLine,
            EndLine = function.EndLine,
            HotspotType = type,
            LocalComplexity = complexity,
            Description = description,
            Suggestion = suggestion
        };

    private static bool IsTooComplex(ComplexityMetrics complexity) =>
        complexity.CyclomaticComplexity > HighComplexityThreshold
        || complexity.CognitiveComplexity > CognitiveComplexityThreshold;

    private static List<string> DescribeComplexBlocks(ComplexityMetrics complexity) =>
        complexity.Hotspots
            .Where(h => h.HotspotType == ComplexBlockHotspot)
            .Select(h => $"Lines {h.StartLine}-{h.EndLine}: {h.Description}")
            .ToList();

    /// <summary>
    /// Effort for refactoring one hotspot: two hours base plus an hour per five points above its threshold, capped at 16.
    /// </summary>
    private static int EstimateHotspotEffort(ComplexityHotspot hotspot)
    {
        var threshold = hotspot.HotspotType switch
        {
            HighCyclomaticHotspot => HighComplexityThreshold,
            HighCognitiveHotspot => CognitiveComplexityThreshold,
            DeepNestingHotspot => MaxNestingDepthThreshold,
            _ => ComplexBlockCognitiveThreshold
        };

        return Math.Min(16, 2 + Math.Max(0, hotspot.LocalComplexity - threshold) / 5);
    }

    /// <summary>
    /// Converts complexity hotspots into plan actions; member-level hotspots for the same lines collapse into one action.
    /// </summary>
    private static List<OptimizationAction> ConvertHotspotsToActions(string filePath, IEnumerable<ComplexityHotspot> hotspots)
    {
        return hotspots
            .GroupBy(h => (h.StartLine, h.EndLine))
            .Select(group =>
            {
                var worst = group.OrderByDescending(EstimateHotspotEffort).First();
                var benefit = Math.Min(1.0, 0.4 + 0.1 * group.Count() + worst.LocalComplexity / 100.0);
                return new OptimizationAction
                {
                    ActionId = Guid.NewGuid().ToString(),
                    Title = $"Reduce complexity (lines {worst.StartLine}-{worst.EndLine})",
                    Description = string.Join("; ", group.Select(h => h.Description)) + ". " + worst.Suggestion,
                    ActionType = OptimizationActionType.ReduceComplexity,
                    Priority = worst.LocalComplexity > 2 * CognitiveComplexityThreshold ? 4 : 3,
                    EstimatedEffortHours = EstimateHotspotEffort(worst),
                    ExpectedBenefit = benefit,
                    FilePath = filePath,
                    StartLine = worst.StartLine,
                    EndLine = worst.EndLine,
                    Dependencies = new List<string>(),
                    RefactoringSuggestions = new List<RefactoringSuggestion>(),
                    IsRecommended = true
                };
            })
            .ToList();
    }
}
