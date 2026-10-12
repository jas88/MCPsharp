namespace MCPsharp.Models.DuplicateDetection;

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
