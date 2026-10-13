namespace MCPsharp.Models.DuplicateDetection;

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
