namespace MCPsharp.Models.DuplicateDetection;

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
