using Microsoft.Extensions.Logging;

namespace MCPsharp.Models;

/// <summary>
/// Result of duplicate code detection
/// </summary>
public class DuplicateDetectionResult
{
    /// <summary>
    /// List of duplicate code groups found
    /// </summary>
    public required IReadOnlyList<DuplicateGroup> DuplicateGroups { get; init; }

    /// <summary>
    /// Metrics about the duplication analysis
    /// </summary>
    public required DuplicationMetrics Metrics { get; init; }

    /// <summary>
    /// Refactoring suggestions for the duplicates
    /// </summary>
    public IReadOnlyList<RefactoringSuggestion>? RefactoringSuggestions { get; init; }

    /// <summary>
    /// Hotspots analysis
    /// </summary>
    public DuplicateHotspotsResult? Hotspots { get; init; }

    /// <summary>
    /// Analysis duration
    /// </summary>
    public required TimeSpan AnalysisDuration { get; init; }

    /// <summary>
    /// Number of files analyzed
    /// </summary>
    public required int FilesAnalyzed { get; init; }

    /// <summary>
    /// Any warnings or issues during analysis
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Whether the analysis was completed successfully
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if analysis failed
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Group of duplicate code blocks
/// </summary>
public class DuplicateGroup
{
    /// <summary>
    /// Unique identifier for this duplicate group
    /// </summary>
    public required string GroupId { get; init; }

    /// <summary>
    /// List of duplicate code blocks in this group
    /// </summary>
    public required IReadOnlyList<CodeBlock> CodeBlocks { get; init; }

    /// <summary>
    /// Similarity score for this group (0.0-1.0, 1.0 = identical)
    /// </summary>
    public required double SimilarityScore { get; init; }

    /// <summary>
    /// Type of duplication
    /// </summary>
    public required DuplicateType DuplicationType { get; init; }

    /// <summary>
    /// Number of lines of duplicated code
    /// </summary>
    public required int LineCount { get; init; }

    /// <summary>
    /// Estimated complexity of the duplicated code
    /// </summary>
    public required int Complexity { get; init; }

    /// <summary>
    /// Whether this is an exact duplicate (100% identical)
    /// </summary>
    public required bool IsExactDuplicate { get; init; }

    /// <summary>
    /// Refactoring suggestions specific to this group
    /// </summary>
    public IReadOnlyList<RefactoringSuggestion>? RefactoringSuggestions { get; init; }

    /// <summary>
    /// Impact assessment for this duplication
    /// </summary>
    public required DuplicationImpact Impact { get; init; }

    /// <summary>
    /// Metadata about the duplication
    /// </summary>
    public required DuplicateMetadata Metadata { get; init; }
}

/// <summary>
/// Represents a block of code
/// </summary>
public class CodeBlock
{
    /// <summary>
    /// File path containing this code block
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Starting line number (1-based)
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// Ending line number (1-based)
    /// </summary>
    public required int EndLine { get; init; }

    /// <summary>
    /// Starting column number (1-based)
    /// </summary>
    public required int StartColumn { get; init; }

    /// <summary>
    /// Ending column number (1-based)
    /// </summary>
    public required int EndColumn { get; init; }

    /// <summary>
    /// The actual source code
    /// </summary>
    public required string SourceCode { get; init; }

    /// <summary>
    /// Normalized source code for comparison
    /// </summary>
    public required string NormalizedCode { get; init; }

    /// <summary>
    /// Hash of the normalized code for fast comparison
    /// </summary>
    public required string CodeHash { get; init; }

    /// <summary>
    /// Type of code element
    /// </summary>
    public required CodeElementType ElementType { get; init; }

    /// <summary>
    /// Name of the code element (method, class, etc.)
    /// </summary>
    public required string ElementName { get; init; }

    /// <summary>
    /// Containing type/class name
    /// </summary>
    public string? ContainingType { get; init; }

    /// <summary>
    /// Namespace of the code element
    /// </summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// Accessibility level
    /// </summary>
    public required Accessibility Accessibility { get; init; }

    /// <summary>
    /// Whether this code block is generated
    /// </summary>
    public required bool IsGenerated { get; init; }

    /// <summary>
    /// Whether this code block is in a test file
    /// </summary>
    public required bool IsTestCode { get; init; }

    /// <summary>
    /// Complexity metrics for this code block
    /// </summary>
    public required ComplexityMetrics Complexity { get; init; }

    /// <summary>
    /// Tokens extracted from the code block
    /// </summary>
    public IReadOnlyList<CodeToken>? Tokens { get; init; }

    /// <summary>
    /// AST structure information
    /// </summary>
    public required AstStructure AstStructure { get; init; }

    /// <summary>
    /// Context lines before and after the code block
    /// </summary>
    public CodeContext? Context { get; init; }
}

/// <summary>
/// Context information around a code block
/// </summary>
public class CodeContext
{
    /// <summary>
    /// Lines before the code block
    /// </summary>
    public required IReadOnlyList<string> BeforeLines { get; init; }

    /// <summary>
    /// Lines after the code block
    /// </summary>
    public required IReadOnlyList<string> AfterLines { get; init; }

    /// <summary>
    /// Number of lines before
    /// </summary>
    public required int BeforeLineCount { get; init; }

    /// <summary>
    /// Number of lines after
    /// </summary>
    public required int AfterLineCount { get; init; }
}

/// <summary>
/// Token extracted from source code
/// </summary>
public class CodeToken
{
    /// <summary>
    /// Token type
    /// </summary>
    public required TokenType Type { get; init; }

    /// <summary>
    /// Token text
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Starting position
    /// </summary>
    public required int StartPosition { get; init; }

    /// <summary>
    /// Token length
    /// </summary>
    public required int Length { get; init; }

    /// <summary>
    /// Line number
    /// </summary>
    public required int Line { get; init; }

    /// <summary>
    /// Column number
    /// </summary>
    public required int Column { get; init; }
}

/// <summary>
/// AST structure information for code comparison
/// </summary>
public class AstStructure
{
    /// <summary>
    /// Structural hash for fast comparison
    /// </summary>
    public required string StructuralHash { get; init; }

    /// <summary>
    /// Node types in the AST (depth-first order)
    /// </summary>
    public required IReadOnlyList<string> NodeTypes { get; init; }

    /// <summary>
    /// AST depth
    /// </summary>
    public required int Depth { get; init; }

    /// <summary>
    /// Number of nodes in the AST
    /// </summary>
    public required int NodeCount { get; init; }

    /// <summary>
    /// Complexity score based on AST structure
    /// </summary>
    public required double StructuralComplexity { get; init; }

    /// <summary>
    /// Control flow patterns identified
    /// </summary>
    public required IReadOnlyList<ControlFlowPattern> ControlFlowPatterns { get; init; }

    /// <summary>
    /// Data flow patterns identified
    /// </summary>
    public required IReadOnlyList<DataFlowPattern> DataFlowPatterns { get; init; }
}

/// <summary>
/// Duplication metrics and statistics
/// </summary>
public class DuplicationMetrics
{
    /// <summary>
    /// Total number of duplicate groups found
    /// </summary>
    public required int TotalDuplicateGroups { get; init; }

    /// <summary>
    /// Number of exact duplicate groups
    /// </summary>
    public required int ExactDuplicateGroups { get; init; }

    /// <summary>
    /// Number of near-miss duplicate groups
    /// </summary>
    public required int NearMissDuplicateGroups { get; init; }

    /// <summary>
    /// Total lines of duplicated code
    /// </summary>
    public required int TotalDuplicateLines { get; init; }

    /// <summary>
    /// Percentage of code that is duplicated
    /// </summary>
    public required double DuplicationPercentage { get; init; }

    /// <summary>
    /// Number of files containing duplicates
    /// </summary>
    public required int FilesWithDuplicates { get; init; }

    /// <summary>
    /// Duplication by type
    /// </summary>
    public required IReadOnlyDictionary<DuplicateType, int> DuplicationByType { get; init; }

    /// <summary>
    /// Duplication by file
    /// </summary>
    public required IReadOnlyDictionary<string, FileDuplicationMetrics> DuplicationByFile { get; init; }

    /// <summary>
    /// Duplication by similarity range
    /// </summary>
    public required IReadOnlyDictionary<string, int> DuplicationBySimilarity { get; init; }

    /// <summary>
    /// Average similarity score across all duplicates
    /// </summary>
    public required double AverageSimilarity { get; init; }

    /// <summary>
    /// Highest similarity score found
    /// </summary>
    public required double MaxSimilarity { get; init; }

    /// <summary>
    /// Lowest similarity score found
    /// </summary>
    public required double MinSimilarity { get; init; }

    /// <summary>
    /// Estimated cost of duplication in maintenance hours
    /// </summary>
    public required double EstimatedMaintenanceCost { get; init; }

    /// <summary>
    /// Estimated savings from refactoring
    /// </summary>
    public required double EstimatedRefactoringSavings { get; init; }
}

/// <summary>
/// File-specific duplication metrics
/// </summary>
public class FileDuplicationMetrics
{
    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Number of duplicate groups in this file
    /// </summary>
    public required int DuplicateGroups { get; init; }

    /// <summary>
    /// Number of lines duplicated in this file
    /// </summary>
    public required int DuplicateLines { get; init; }

    /// <summary>
    /// Percentage of file that is duplicated
    /// </summary>
    public required double DuplicationPercentage { get; init; }

    /// <summary>
    /// Types of duplication found in this file
    /// </summary>
    public required IReadOnlyList<DuplicateType> DuplicationTypes { get; init; }

    /// <summary>
    /// Average complexity of duplicated code in this file
    /// </summary>
    public required double AverageComplexity { get; init; }
}

/// <summary>
/// Refactoring suggestion for duplicate code
/// </summary>
public class RefactoringSuggestion
{
    /// <summary>
    /// Unique identifier for this suggestion
    /// </summary>
    public required string SuggestionId { get; init; }

    /// <summary>
    /// Type of refactoring suggested
    /// </summary>
    public required RefactoringType RefactoringType { get; init; }

    /// <summary>
    /// Title of the refactoring suggestion
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Detailed description of the refactoring
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Duplicate groups this refactoring would address
    /// </summary>
    public required IReadOnlyList<string> DuplicateGroupIds { get; init; }

    /// <summary>
    /// Priority level of this refactoring
    /// </summary>
    public required RefactoringPriority Priority { get; init; }

    /// <summary>
    /// Estimated effort required (in hours)
    /// </summary>
    public required double EstimatedEffort { get; init; }

    /// <summary>
    /// Estimated benefit from this refactoring
    /// </summary>
    public required double EstimatedBenefit { get; init; }

    /// <summary>
    /// Risk level of this refactoring
    /// </summary>
    public required RefactoringRisk Risk { get; init; }

    /// <summary>
    /// Whether this refactoring would introduce breaking changes
    /// </summary>
    public required bool IsBreakingChange { get; init; }

    /// <summary>
    /// Steps to implement this refactoring
    /// </summary>
    public required IReadOnlyList<RefactoringStep> ImplementationSteps { get; init; }

    /// <summary>
    /// Prerequisites for this refactoring
    /// </summary>
    public required IReadOnlyList<string> Prerequisites { get; init; }

    /// <summary>
    /// Potential side effects
    /// </summary>
    public required IReadOnlyList<string> SideEffects { get; init; }

    /// <summary>
    /// Validation results for this refactoring
    /// </summary>
    public RefactoringValidationResult? ValidationResult { get; init; }

    /// <summary>
    /// Metadata about this suggestion
    /// </summary>
    public required RefactoringMetadata Metadata { get; init; }
}

/// <summary>
/// Step in a refactoring implementation
/// </summary>
public class RefactoringStep
{
    /// <summary>
    /// Step description
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Type of operation
    /// </summary>
    public required RefactoringOperationType OperationType { get; init; }

    /// <summary>
    /// Target file paths
    /// </summary>
    public required IReadOnlyList<string> TargetFiles { get; init; }

    /// <summary>
    /// Estimated time for this step (in minutes)
    /// </summary>
    public required int EstimatedMinutes { get; init; }

    /// <summary>
    /// Whether this step is optional
    /// </summary>
    public required bool IsOptional { get; init; }

    /// <summary>
    /// Dependencies on other steps
    /// </summary>
    public required IReadOnlyList<int> Dependencies { get; init; }
}

/// <summary>
/// Result of code similarity comparison
/// </summary>
public class CodeSimilarityResult
{
    /// <summary>
    /// Overall similarity score (0.0-1.0)
    /// </summary>
    public required double OverallSimilarity { get; init; }

    /// <summary>
    /// Structural similarity score
    /// </summary>
    public required double StructuralSimilarity { get; init; }

    /// <summary>
    /// Token similarity score
    /// </summary>
    public required double TokenSimilarity { get; init; }

    /// <summary>
    /// Semantic similarity score
    /// </summary>
    public required double SemanticSimilarity { get; init; }

    /// <summary>
    /// Differences between the code blocks
    /// </summary>
    public required IReadOnlyList<CodeDifference> Differences { get; init; }

    /// <summary>
    /// Common patterns found
    /// </summary>
    public required IReadOnlyList<CommonPattern> CommonPatterns { get; init; }

    /// <summary>
    /// Whether the code blocks are considered duplicates
    /// </summary>
    public required bool IsDuplicate { get; init; }

    /// <summary>
    /// Duplicate type if they are duplicates
    /// </summary>
    public DuplicateType? DuplicateType { get; init; }
}

/// <summary>
/// Difference between two code blocks
/// </summary>
public class CodeDifference
{
    /// <summary>
    /// Type of difference
    /// </summary>
    public required DifferenceType Type { get; init; }

    /// <summary>
    /// Description of the difference
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Location in first code block
    /// </summary>
    public required CodeLocation Location1 { get; init; }

    /// <summary>
    /// Location in second code block
    /// </summary>
    public required CodeLocation Location2 { get; init; }

    /// <summary>
    /// Text from first code block
    /// </summary>
    public required string Text1 { get; init; }

    /// <summary>
    /// Text from second code block
    /// </summary>
    public required string Text2 { get; init; }

    /// <summary>
    /// Impact of this difference (0.0-1.0)
    /// </summary>
    public required double Impact { get; init; }
}

/// <summary>
/// Location in source code
/// </summary>
public class CodeLocation
{
    /// <summary>
    /// Line number (1-based)
    /// </summary>
    public required int Line { get; init; }

    /// <summary>
    /// Column number (1-based)
    /// </summary>
    public required int Column { get; init; }

    /// <summary>
    /// Length of the location
    /// </summary>
    public required int Length { get; init; }
}

/// <summary>
/// Common pattern found in duplicate code
/// </summary>
public class CommonPattern
{
    /// <summary>
    /// Pattern type
    /// </summary>
    public required PatternType Type { get; init; }

    /// <summary>
    /// Pattern description
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Frequency of this pattern
    /// </summary>
    public required int Frequency { get; init; }

    /// <summary>
    /// Confidence score for this pattern
    /// </summary>
    public required double Confidence { get; init; }
}

/// <summary>
/// Duplicate hotspots analysis result
/// </summary>
public class DuplicateHotspotsResult
{
    /// <summary>
    /// Files with highest duplication rates
    /// </summary>
    public required IReadOnlyList<FileHotspot> FileHotspots { get; init; }

    /// <summary>
    /// Classes with highest duplication rates
    /// </summary>
    public required IReadOnlyList<ClassHotspot> ClassHotspots { get; init; }

    /// <summary>
    /// Methods with highest duplication rates
    /// </summary>
    public required IReadOnlyList<MethodHotspot> MethodHotspots { get; init; }

    /// <summary>
    /// Directories with highest duplication rates
    /// </summary>
    public required IReadOnlyList<DirectoryHotspot> DirectoryHotspots { get; init; }

    /// <summary>
    /// Trends in duplication over time (if historical data available)
    /// </summary>
    public IReadOnlyList<DuplicationTrend>? Trends { get; init; }
}

/// <summary>
/// File duplication hotspot
/// </summary>
public class FileHotspot
{
    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Duplication score
    /// </summary>
    public required double DuplicationScore { get; init; }

    /// <summary>
    /// Number of duplicate groups
    /// </summary>
    public required int DuplicateGroupCount { get; init; }

    /// <summary>
    /// Duplication percentage
    /// </summary>
    public required double DuplicationPercentage { get; init; }

    /// <summary>
    /// Risk level
    /// </summary>
    public required HotspotRiskLevel RiskLevel { get; init; }
}

/// <summary>
/// Class duplication hotspot
/// </summary>
public class ClassHotspot
{
    /// <summary>
    /// Class name
    /// </summary>
    public required string ClassName { get; init; }

    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Duplication score
    /// </summary>
    public required double DuplicationScore { get; init; }

    /// <summary>
    /// Number of duplicate groups
    /// </summary>
    public required int DuplicateGroupCount { get; init; }

    /// <summary>
    /// Risk level
    /// </summary>
    public required HotspotRiskLevel RiskLevel { get; init; }
}

/// <summary>
/// Method duplication hotspot
/// </summary>
public class MethodHotspot
{
    /// <summary>
    /// Method name
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// Containing class
    /// </summary>
    public required string ClassName { get; init; }

    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Duplication score
    /// </summary>
    public required double DuplicationScore { get; init; }

    /// <summary>
    /// Number of duplicate groups
    /// </summary>
    public required int DuplicateGroupCount { get; init; }

    /// <summary>
    /// Risk level
    /// </summary>
    public required HotspotRiskLevel RiskLevel { get; init; }
}

/// <summary>
/// Directory duplication hotspot
/// </summary>
public class DirectoryHotspot
{
    /// <summary>
    /// Directory path
    /// </summary>
    public required string DirectoryPath { get; init; }

    /// <summary>
    /// Duplication score
    /// </summary>
    public required double DuplicationScore { get; init; }

    /// <summary>
    /// Number of files with duplicates
    /// </summary>
    public required int FilesWithDuplicates { get; init; }

    /// <summary>
    /// Total duplicate lines
    /// </summary>
    public required int TotalDuplicateLines { get; init; }

    /// <summary>
    /// Risk level
    /// </summary>
    public required HotspotRiskLevel RiskLevel { get; init; }
}

/// <summary>
/// Duplication trend over time
/// </summary>
public class DuplicationTrend
{
    /// <summary>
    /// Date of the measurement
    /// </summary>
    public required DateTime Date { get; init; }

    /// <summary>
    /// Duplication percentage
    /// </summary>
    public required double DuplicationPercentage { get; init; }

    /// <summary>
    /// Total duplicate lines
    /// </summary>
    public required int TotalDuplicateLines { get; init; }

    /// <summary>
    /// Number of duplicate groups
    /// </summary>
    public required int DuplicateGroupCount { get; init; }
}

/// <summary>
/// Result of refactoring validation
/// </summary>
public class RefactoringValidationResult
{
    /// <summary>
    /// Whether the refactoring is valid
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Validation issues found
    /// </summary>
    public required IReadOnlyList<ValidationIssue> Issues { get; init; }

    /// <summary>
    /// Impact on dependencies
    /// </summary>
    public required IReadOnlyList<DependencyImpact> DependencyImpacts { get; init; }

    /// <summary>
    /// Overall risk assessment
    /// </summary>
    public required RefactoringRisk OverallRisk { get; init; }

    /// <summary>
    /// Recommendations
    /// </summary>
    public required IReadOnlyList<string> Recommendations { get; init; }
}

/// <summary>
/// Impact on dependencies
/// </summary>
public class DependencyImpact
{
    /// <summary>
    /// Dependent file
    /// </summary>
    public required string DependentFile { get; init; }

    /// <summary>
    /// Impact type
    /// </summary>
    public required DependencyImpactType ImpactType { get; init; }

    /// <summary>
    /// Impact description
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Whether this is a breaking change
    /// </summary>
    public required bool IsBreakingChange { get; init; }
}

/// <summary>
/// Metadata for duplicate groups
/// </summary>
public class DuplicateMetadata
{
    /// <summary>
    /// When this duplication was first detected
    /// </summary>
    public required DateTime DetectedAt { get; init; }

    /// <summary>
    /// Detection algorithm used
    /// </summary>
    public required string DetectionAlgorithm { get; init; }

    /// <summary>
    /// Configuration used for detection
    /// </summary>
    public required string DetectionConfiguration { get; init; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public required Dictionary<string, object> AdditionalMetadata { get; init; }
}

/// <summary>
/// Metadata for refactoring suggestions
/// </summary>
public class RefactoringMetadata
{
    /// <summary>
    /// When this suggestion was generated
    /// </summary>
    public required DateTime GeneratedAt { get; init; }

    /// <summary>
    /// Algorithm used to generate the suggestion
    /// </summary>
    public required string GenerationAlgorithm { get; init; }

    /// <summary>
    /// Confidence score for this suggestion
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public required Dictionary<string, object> AdditionalMetadata { get; init; }
}

/// <summary>
/// Impact assessment for duplication
/// </summary>
public class DuplicationImpact
{
    /// <summary>
    /// Maintenance impact score (0.0-1.0)
    /// </summary>
    public required double MaintenanceImpact { get; init; }

    /// <summary>
    /// Readability impact score (0.0-1.0)
    /// </summary>
    public required double ReadabilityImpact { get; init; }

    /// <summary>
    /// Performance impact score (0.0-1.0)
    /// </summary>
    public required double PerformanceImpact { get; init; }

    /// <summary>
    /// Testability impact score (0.0-1.0)
    /// </summary>
    public required double TestabilityImpact { get; init; }

    /// <summary>
    /// Overall impact score (0.0-1.0)
    /// </summary>
    public required double OverallImpact { get; init; }

    /// <summary>
    /// Impact level
    /// </summary>
    public required ImpactLevel ImpactLevel { get; init; }
}

/// <summary>
/// Complexity metrics for code blocks
/// </summary>
public class ComplexityMetrics
{
    /// <summary>
    /// Cyclomatic complexity
    /// </summary>
    public required int CyclomaticComplexity { get; init; }

    /// <summary>
    /// Cognitive complexity
    /// </summary>
    public required int CognitiveComplexity { get; init; }

    /// <summary>
    /// Lines of code
    /// </summary>
    public required int LinesOfCode { get; init; }

    /// <summary>
    /// Logical lines of code
    /// </summary>
    public required int LogicalLinesOfCode { get; init; }

    /// <summary>
    /// Number of parameters
    /// </summary>
    public required int ParameterCount { get; init; }

    /// <summary>
    /// Nesting depth
    /// </summary>
    public required int NestingDepth { get; init; }

    /// <summary>
    /// Overall complexity score
    /// </summary>
    public required double OverallScore { get; init; }
}

// Enums

/// <summary>
/// Types of code duplication
/// </summary>
public enum DuplicateType
{
    /// <summary>
    /// Unknown type
    /// </summary>
    Unknown,

    /// <summary>
    /// Method duplication
    /// </summary>
    Method,

    /// <summary>
    /// Class duplication
    /// </summary>
    Class,

    /// <summary>
    /// Code block duplication
    /// </summary>
    CodeBlock,

    /// <summary>
    /// File duplication
    /// </summary>
    File,

    /// <summary>
    /// Property duplication
    /// </summary>
    Property,

    /// <summary>
    /// Constructor duplication
    /// </summary>
    Constructor
}

/// <summary>
/// Types of code elements
/// </summary>
public enum CodeElementType
{
    /// <summary>
    /// Unknown element type
    /// </summary>
    Unknown,

    /// <summary>
    /// Method
    /// </summary>
    Method,

    /// <summary>
    /// Class
    /// </summary>
    Class,

    /// <summary>
    /// Interface
    /// </summary>
    Interface,

    /// <summary>
    /// Struct
    /// </summary>
    Struct,

    /// <summary>
    /// Property
    /// </summary>
    Property,

    /// <summary>
    /// Constructor
    /// </summary>
    Constructor,

    /// <summary>
    /// Code block
    /// </summary>
    CodeBlock,

    /// <summary>
    /// Field
    /// </summary>
    Field,

    /// <summary>
    /// Event
    /// </summary>
    Event
}

/// <summary>
/// Accessibility levels
/// </summary>
public enum Accessibility
{
    /// <summary>
    /// Private
    /// </summary>
    Private,

    /// <summary>
    /// Internal
    /// </summary>
    Internal,

    /// <summary>
    /// Protected
    /// </summary>
    Protected,

    /// <summary>
    /// Public
    /// </summary>
    Public
}

/// <summary>
/// Token types
/// </summary>
public enum TokenType
{
    /// <summary>
    /// Unknown token
    /// </summary>
    Unknown,

    /// <summary>
    /// Identifier
    /// </summary>
    Identifier,

    /// <summary>
    /// Keyword
    /// </summary>
    Keyword,

    /// <summary>
    /// String literal
    /// </summary>
    StringLiteral,

    /// <summary>
    /// Numeric literal
    /// </summary>
    NumericLiteral,

    /// <summary>
    /// Operator
    /// </summary>
    Operator,

    /// <summary>
    /// Punctuation
    /// </summary>
    Punctuation,

    /// <summary>
    /// Comment
    /// </summary>
    Comment,

    /// <summary>
    /// Whitespace
    /// </summary>
    Whitespace
}

/// <summary>
/// Control flow patterns
/// </summary>
public enum ControlFlowPattern
{
    /// <summary>
    /// Sequential execution
    /// </summary>
    Sequential,

    /// <summary>
    /// Conditional (if/else)
    /// </summary>
    Conditional,

    /// <summary>
    /// Loop (for/while/do)
    /// </summary>
    Loop,

    /// <summary>
    /// Switch/case
    /// </summary>
    Switch,

    /// <summary>
    /// Try/catch/finally
    /// </summary>
    TryCatch,

    /// <summary>
    /// Return
    /// </summary>
    Return,

    /// <summary>
    /// Break/continue
    /// </summary>
    BreakContinue,

    /// <summary>
    /// Goto
    /// </summary>
    Goto
}

/// <summary>
/// Data flow patterns
/// </summary>
public enum DataFlowPattern
{
    /// <summary>
    /// Variable assignment
    /// </summary>
    Assignment,

    /// <summary>
    /// Variable usage
    /// </summary>
    Usage,

    /// <summary>
    /// Method call
    /// </summary>
    MethodCall,

    /// <summary>
    /// Property access
    /// </summary>
    PropertyAccess,

    /// <summary>
    /// Object creation
    /// </summary>
    ObjectCreation,

    /// <summary>
    /// Collection operation
    /// </summary>
    CollectionOperation,

    /// <summary>
    /// LINQ operation
    /// </summary>
    LinqOperation
}

/// <summary>
/// Types of refactoring
/// </summary>
public enum RefactoringType
{
    /// <summary>
    /// Extract method
    /// </summary>
    ExtractMethod,

    /// <summary>
    /// Extract class
    /// </summary>
    ExtractClass,

    /// <summary>
    /// Extract base class
    /// </summary>
    ExtractBaseClass,

    /// <summary>
    /// Template method pattern
    /// </summary>
    TemplateMethod,

    /// <summary>
    /// Strategy pattern
    /// </summary>
    StrategyPattern,

    /// <summary>
    /// Utility class
    /// </summary>
    UtilityClass,

    /// <summary>
    /// Composition over inheritance
    /// </summary>
    Composition,

    /// <summary>
    /// Parameterize method
    /// </summary>
    ParameterizeMethod,

    /// <summary>
    /// Replace conditional with polymorphism
    /// </summary>
    ReplaceConditionalWithPolymorphism,

    /// <summary>
    /// Replace magic number with constant
    /// </summary>
    ReplaceMagicNumberWithConstant,

    /// <summary>
    /// Introduce parameter object
    /// </summary>
    IntroduceParameterObject
}

/// <summary>
/// Refactoring priority levels
/// </summary>
public enum RefactoringPriority
{
    /// <summary>
    /// Low priority
    /// </summary>
    Low,

    /// <summary>
    /// Medium priority
    /// </summary>
    Medium,

    /// <summary>
    /// High priority
    /// </summary>
    High,

    /// <summary>
    /// Critical priority
    /// </summary>
    Critical
}

/// <summary>
/// Refactoring risk levels
/// </summary>
public enum RefactoringRisk
{
    /// <summary>
    /// Low risk
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk
    /// </summary>
    Medium,

    /// <summary>
    /// High risk
    /// </summary>
    High,

    /// <summary>
    /// Very high risk
    /// </summary>
    VeryHigh
}

/// <summary>
/// Refactoring operation types
/// </summary>
public enum RefactoringOperationType
{
    /// <summary>
    /// Create new file
    /// </summary>
    CreateFile,

    /// <summary>
    /// Modify existing file
    /// </summary>
    ModifyFile,

    /// <summary>
    /// Delete file
    /// </summary>
    DeleteFile,

    /// <summary>
    /// Move file
    /// </summary>
    MoveFile,

    /// <summary>
    /// Rename symbol
    /// </summary>
    RenameSymbol,

    /// <summary>
    /// Extract code
    /// </summary>
    ExtractCode,

    /// <summary>
    /// Inline code
    /// </summary>
    InlineCode,

    /// <summary>
    /// Change signature
    /// </summary>
    ChangeSignature
}

/// <summary>
/// Difference types
/// </summary>
public enum DifferenceType
{
    /// <summary>
    /// Whitespace difference
    /// </summary>
    Whitespace,

    /// <summary>
    /// Comment difference
    /// </summary>
    Comment,

    /// <summary>
    /// Identifier difference
    /// </summary>
    Identifier,

    /// <summary>
    /// Literal difference
    /// </summary>
    Literal,

    /// <summary>
    /// Structural difference
    /// </summary>
    Structural,

    /// <summary>
    /// Logical difference
    /// </summary>
    Logical,

    /// <summary>
    /// Type difference
    /// </summary>
    Type,

    /// <summary>
    /// Control flow difference
    /// </summary>
    ControlFlow
}

/// <summary>
/// Pattern types
/// </summary>
public enum PatternType
{
    /// <summary>
    /// Control flow pattern
    /// </summary>
    ControlFlow,

    /// <summary>
    /// Data flow pattern
    /// </summary>
    DataFlow,

    /// <summary>
    /// Structural pattern
    /// </summary>
    Structural,

    /// <summary>
    /// Design pattern
    /// </summary>
    DesignPattern,

    /// <summary>
    /// Anti-pattern
    /// </summary>
    AntiPattern
}

/// <summary>
/// Hotspot risk levels
/// </summary>
public enum HotspotRiskLevel
{
    /// <summary>
    /// Low risk
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk
    /// </summary>
    Medium,

    /// <summary>
    /// High risk
    /// </summary>
    High,

    /// <summary>
    /// Critical risk
    /// </summary>
    Critical
}


/// <summary>
/// Dependency impact types
/// </summary>
public enum DependencyImpactType
{
    /// <summary>
    /// No impact
    /// </summary>
    None,

    /// <summary>
    /// Minor impact
    /// </summary>
    Minor,

    /// <summary>
    /// Major impact
    /// </summary>
    Major,

    /// <summary>
    /// Breaking change
    /// </summary>
    BreakingChange
}

/// <summary>
/// Impact levels
/// </summary>
public enum ImpactLevel
{
    /// <summary>
    /// Negligible impact
    /// </summary>
    Negligible,

    /// <summary>
    /// Minor impact
    /// </summary>
    Minor,

    /// <summary>
    /// Moderate impact
    /// </summary>
    Moderate,

    /// <summary>
    /// Significant impact
    /// </summary>
    Significant,

    /// <summary>
    /// Critical impact
    /// </summary>
    Critical
}