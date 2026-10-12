using MCPsharp.Models;

namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Mutable accumulator for a <see cref="BlockExtractionAnalysis"/>
/// </summary>
internal sealed class ExtractionAnalysisBuilder(string filePath, int startLine, ExtractionAnalysisMode mode)
{
    public List<ExtractionVariable> Parameters { get; } = [];

    public List<ExtractionVariable> ReturnValues { get; } = [];

    public List<string> CapturedVariables { get; } = [];

    public List<string> ReferencedNonPublicMembers { get; } = [];

    public List<string> UnsafeReasons { get; } = [];

    public List<string> Warnings { get; } = [];

    public bool RequiresInstanceAccess { get; set; }

    public bool ContainsReturnStatement { get; set; }

    public bool ContainsJumpOutOfBlock { get; set; }

    public string? SuggestedSignature { get; set; }

    public BlockExtractionAnalysis Build() => new()
    {
        FilePath = filePath,
        StartLine = startLine,
        Mode = mode,
        IsSafe = UnsafeReasons.Count == 0,
        Parameters = Parameters,
        ReturnValues = ReturnValues,
        CapturedVariables = CapturedVariables,
        ReferencedNonPublicMembers = ReferencedNonPublicMembers,
        RequiresInstanceAccess = RequiresInstanceAccess,
        ContainsReturnStatement = ContainsReturnStatement,
        ContainsJumpOutOfBlock = ContainsJumpOutOfBlock,
        UnsafeReasons = UnsafeReasons,
        Warnings = Warnings,
        SuggestedSignature = SuggestedSignature
    };

    public string FormatSignature(string returnType)
    {
        var modifier = RequiresInstanceAccess ? string.Empty : "static ";
        var parameters = string.Join(", ", Parameters.Select(p => $"{p.Type ?? "var"} {p.Name}"));
        return $"{modifier}{returnType} {ExtractionDependencyAnalyzer.ExtractedMethodName}({parameters})";
    }

    public static string ReturnTypeFor(IReadOnlyList<ExtractionVariable> returnValues) => returnValues.Count switch
    {
        0 => "void",
        1 => returnValues[0].Type ?? "var",
        _ => $"({string.Join(", ", returnValues.Select(v => $"{v.Type ?? "var"} {v.Name}"))})"
    };

    public static BlockExtractionAnalysis NotApplicable(string filePath, int startLine, string elementKind)
    {
        var builder = new ExtractionAnalysisBuilder(filePath, startLine, ExtractionAnalysisMode.NotApplicable);
        builder.UnsafeReasons.Add($"A {elementKind} cannot be extracted into a method");
        return builder.Build();
    }

    public static BlockExtractionAnalysis Empty(string filePath, int startLine, ExtractionAnalysisMode mode)
    {
        var builder = new ExtractionAnalysisBuilder(filePath, startLine, mode)
        {
            SuggestedSignature = $"static void {ExtractionDependencyAnalyzer.ExtractedMethodName}()"
        };
        return builder.Build();
    }
}
