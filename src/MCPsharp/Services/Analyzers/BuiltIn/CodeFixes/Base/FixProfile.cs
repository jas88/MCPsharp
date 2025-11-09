namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;

/// <summary>
/// Predefined profiles for grouping fixes by aggressiveness and safety
/// </summary>
public enum FixProfile
{
    /// <summary>
    /// Only safe, low-risk fixes (e.g., remove unused using statements)
    /// Can be applied automatically without user review
    /// </summary>
    Conservative,

    /// <summary>
    /// Moderate risk fixes (e.g., simplify expressions, async/await patterns)
    /// Recommended for most scenarios with preview
    /// </summary>
    Balanced,

    /// <summary>
    /// All fixes including structural changes (e.g., extract method, inline variable)
    /// Requires careful review
    /// </summary>
    Aggressive,

    /// <summary>
    /// Custom user-defined profile
    /// </summary>
    Custom
}
