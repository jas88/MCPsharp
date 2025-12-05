namespace MCPsharp.Models.Database;

/// <summary>
/// Represents a cross-reference between symbols.
/// </summary>
public sealed class DbReference
{
    public long Id { get; set; }
    public long FromSymbolId { get; set; }
    public long ToSymbolId { get; set; }
    public required string ReferenceKind { get; set; }  // Call, Inheritance, Implementation, TypeUsage
    public long FileId { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
}
