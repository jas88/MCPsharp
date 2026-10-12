using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Compares symbols that may come from different compilations of the same solution.
/// A type declared in project A and seen from project B (through a project reference) can be
/// represented by a distinct symbol instance (e.g. a retargeting symbol), so plain
/// <see cref="SymbolEqualityComparer.Default"/> is not sufficient for cross-project matching.
/// </summary>
public static class CrossProjectSymbolComparer
{
    /// <summary>
    /// True when both symbols denote the same declaration, even if obtained from different compilations.
    /// </summary>
    public static bool AreEquivalent(ISymbol? x, ISymbol? y)
    {
        if (x == null || y == null)
        {
            return x == null && y == null;
        }

        if (SymbolEqualityComparer.Default.Equals(x, y))
        {
            return true;
        }

        var xDefinition = x.OriginalDefinition;
        var yDefinition = y.OriginalDefinition;
        if (SymbolEqualityComparer.Default.Equals(xDefinition, yDefinition))
        {
            return true;
        }

        if (xDefinition.Kind != yDefinition.Kind)
        {
            return false;
        }

        var xId = xDefinition.GetDocumentationCommentId();
        return xId != null &&
               xId == yDefinition.GetDocumentationCommentId() &&
               string.Equals(xDefinition.ContainingAssembly?.Name, yDefinition.ContainingAssembly?.Name, StringComparison.Ordinal);
    }
}
