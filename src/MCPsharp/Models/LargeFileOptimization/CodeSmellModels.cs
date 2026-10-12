namespace MCPsharp.Models.LargeFileOptimization;

/// <summary>
/// A code smell detected in the code
/// </summary>
public class CodeSmell
{
    public required string SmellType { get; init; }
    public required string Description { get; init; }
    public required CodeSmellSeverity Severity { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string AffectedCode { get; init; }
    public required string Suggestion { get; init; }
    public required List<RefactoringPattern> RefactoringPatterns { get; init; } = new();
    public required double ImpactScore { get; init; }
}

/// <summary>
/// Severity levels for code smells
/// </summary>
public enum CodeSmellSeverity
{
    Info,
    Minor,
    Major,
    Critical,
    Blocker
}

/// <summary>
/// Refactoring pattern to fix a code smell
/// </summary>
public class RefactoringPattern
{
    public required string PatternName { get; init; }
    public required string Description { get; init; }
    public required List<string> Steps { get; init; } = new();
    public required double Applicability { get; init; }
    public required int EstimatedEffort { get; init; }
}

/// <summary>
/// A refactoring suggestion with before/after examples
/// </summary>
public class RefactoringSuggestion
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required RefactoringType Type { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required CodeExample BeforeAfter { get; init; }
    public required double Confidence { get; init; }
    public required int EstimatedEffortHours { get; init; }
    public required double ExpectedBenefit { get; init; }
    public required int Priority { get; init; }
    public required List<string> Benefits { get; init; } = new();
    public required List<string> Risks { get; init; } = new();
}

/// <summary>
/// Types of refactoring
/// </summary>
public enum RefactoringType
{
    ExtractMethod,
    ExtractClass,
    ExtractInterface,
    MoveMethod,
    MoveField,
    RenameMethod,
    ReplaceConditionalWithPolymorphism,
    ReplaceMagicNumber,
    IntroduceParameterObject,
    DecomposeMethod,
    SimplifyConditional,
    ExtractStrategy,
    ReplaceTypeCodeWithSubclass
}

/// <summary>
/// Before and after code example
/// </summary>
public class CodeExample
{
    public required string BeforeCode { get; init; }
    public required string AfterCode { get; init; }
    public required string Explanation { get; init; }
    public required List<string> Changes { get; init; } = new();
}
