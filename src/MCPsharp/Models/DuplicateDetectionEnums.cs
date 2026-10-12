namespace MCPsharp.Models;

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
