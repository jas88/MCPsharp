using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace MCPsharp.Models.Roslyn;

/// <summary>
/// Represents the location of a symbol in source code
/// </summary>
public class SymbolLocation
{
    /// <summary>
    /// The document containing the symbol
    /// </summary>
    public Document? Document { get; init; }

    /// <summary>
    /// File path of the document
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Source location in the document
    /// </summary>
    public Location? Location { get; init; }

    /// <summary>
    /// Line number (0-indexed)
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// Column number (0-indexed)
    /// </summary>
    public int Column { get; init; }

    /// <summary>
    /// Text span of the symbol
    /// </summary>
    public TextSpan? TextSpan { get; init; }
}