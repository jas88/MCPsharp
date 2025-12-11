namespace MCPsharp.Models.Database;

/// <summary>
/// Represents a code symbol (class, method, property, etc.) in the cache.
/// </summary>
public sealed class DbSymbol
{
    public long Id { get; set; }
    public long FileId { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }  // Class, Method, Property, Field, etc.
    public string? Namespace { get; set; }
    public string? ContainingType { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public required string Accessibility { get; set; }  // public, private, internal, etc.
    public string? Signature { get; set; }  // Full signature for methods
}
