namespace MCPsharp.Models.LargeFileOptimization;

/// <summary>
/// Complexity metrics for code analysis
/// </summary>
public class ComplexityMetrics
{
    public required int CyclomaticComplexity { get; init; }
    public required int CognitiveComplexity { get; init; }
    public required int HalsteadVolume { get; init; }
    public required int HalsteadDifficulty { get; init; }
    public required double MaintainabilityIndex { get; init; }
    public required int MaximumNestingDepth { get; init; }
    public required int NumberOfDecisionPoints { get; init; }
    public required ComplexityLevel ComplexityLevel { get; init; }
    public required List<ComplexityHotspot> Hotspots { get; init; } = new();
}

/// <summary>
/// Levels of complexity
/// </summary>
public enum ComplexityLevel
{
    Simple,     // 1-10
    Moderate,   // 11-20
    Complex,    // 21-50
    VeryComplex // > 50
}

/// <summary>
/// Hotspot of complexity in code
/// </summary>
public class ComplexityHotspot
{
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string HotspotType { get; init; }
    public required int LocalComplexity { get; init; }
    public required string Description { get; init; }
    public required string Suggestion { get; init; }
}
