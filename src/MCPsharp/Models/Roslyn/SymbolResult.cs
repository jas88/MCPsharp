using Microsoft.CodeAnalysis;
namespace MCPsharp.Models.Roslyn;

/// <summary>
/// Result of a symbol search query
/// </summary>
public class SymbolResult
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string File { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public string? ContainerName { get; init; }
}

/// <summary>
/// Detailed information about a symbol
/// </summary>
public class SymbolInfo
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Signature { get; init; }
    public string? Documentation { get; init; }
    public string? Namespace { get; init; }
    public List<string> BaseTypes { get; init; } = new();
    public List<MemberInfo> Members { get; init; } = new();
    public ISymbol? Symbol { get; init; }
}

/// <summary>
/// Information about a class member
/// </summary>
public class MemberInfo
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Type { get; init; }
    public required int Line { get; init; }
    public string? Accessibility { get; init; }
}
