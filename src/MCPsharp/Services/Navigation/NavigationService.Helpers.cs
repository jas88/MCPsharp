using Microsoft.CodeAnalysis;
using MCPsharp.Models.Navigation;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Construction of navigation results, locations and symbol info.
/// </summary>
public partial class NavigationService
{
    private static NavigationResult CreateExternalSymbolResult(ISymbol symbol, long elapsedMs)
    {
        return new NavigationResult
        {
            Success = false,
            IsExternal = true,
            ExternalInfo = new ExternalSymbolInfo
            {
                AssemblyName = symbol.ContainingAssembly?.Name,
                TypeName = symbol.ContainingType?.ToDisplayString(),
                MemberName = symbol.Name,
                Documentation = GetDocumentationSummary(symbol),
                CanDecompile = false // Could check if decompiler is available
            },
            Message = $"Symbol '{symbol.Name}' is defined in external assembly '{symbol.ContainingAssembly?.Name}'",
            ExecutionTimeMs = elapsedMs
        };
    }

    private static NavigationLocation? CreateNavigationLocation(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null)
        {
            return null;
        }

        var lineSpan = location.GetLineSpan();
        return new NavigationLocation
        {
            FilePath = lineSpan.Path,
            Line = lineSpan.StartLinePosition.Line,
            Column = lineSpan.StartLinePosition.Character,
            Symbol = CreateNavigationSymbolInfo(symbol),
            IsPartial = symbol.Locations.Count(l => l.IsInSource) > 1
        };
    }

    private static NavigationSymbolInfo CreateNavigationSymbolInfo(ISymbol symbol)
    {
        // Add method-specific information
        if (symbol is IMethodSymbol method)
        {
            return new NavigationSymbolInfo
            {
                Name = symbol.Name,
                Kind = GetSymbolKind(symbol),
                Signature = symbol.ToDisplayString(),
                ContainingType = symbol.ContainingType?.Name,
                Namespace = symbol.ContainingNamespace?.ToDisplayString(),
                Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                Documentation = GetDocumentationSummary(symbol),
                IsAbstract = symbol.IsAbstract,
                IsVirtual = symbol.IsVirtual,
                IsOverride = symbol.IsOverride,
                IsSealed = symbol.IsSealed,
                IsStatic = symbol.IsStatic,
                ReturnType = method.ReturnType.ToDisplayString(),
                Parameters = method.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}").ToList(),
                IsExplicitInterfaceImplementation = method.ExplicitInterfaceImplementations.Any()
            };
        }

        return new NavigationSymbolInfo
        {
            Name = symbol.Name,
            Kind = GetSymbolKind(symbol),
            Signature = symbol.ToDisplayString(),
            ContainingType = symbol.ContainingType?.Name,
            Namespace = symbol.ContainingNamespace?.ToDisplayString(),
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            Documentation = GetDocumentationSummary(symbol),
            IsAbstract = symbol.IsAbstract,
            IsVirtual = symbol.IsVirtual,
            IsOverride = symbol.IsOverride,
            IsSealed = symbol.IsSealed,
            IsStatic = symbol.IsStatic
        };
    }

    private static string GetSymbolKind(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol type => type.TypeKind.ToString().ToLowerInvariant(),
            IMethodSymbol => "method",
            IPropertySymbol => "property",
            IFieldSymbol => "field",
            IEventSymbol => "event",
            _ => symbol.Kind.ToString().ToLowerInvariant()
        };
    }

    private static string? GetDocumentationSummary(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        var summaryStart = xml.IndexOf("<summary>", StringComparison.Ordinal);
        var summaryEnd = xml.IndexOf("</summary>", StringComparison.Ordinal);
        if (summaryStart >= 0 && summaryEnd > summaryStart)
        {
            return xml.Substring(summaryStart + 9, summaryEnd - summaryStart - 9).Trim();
        }

        return null;
    }

    private async Task<NavigationLocation> AddCodeContextAsync(NavigationLocation location)
    {
        try
        {
            var document = _workspace.GetDocument(location.FilePath);
            if (document == null)
            {
                return location;
            }

            var text = await document.GetTextAsync();
            var lines = text.Lines;

            if (location.Line >= 0 && location.Line < lines.Count)
            {
                var targetLine = lines[location.Line];
                var before = new List<string>();
                var after = new List<string>();

                // Get 2 lines before
                for (int i = Math.Max(0, location.Line - 2); i < location.Line; i++)
                {
                    before.Add(lines[i].ToString());
                }

                // Get 2 lines after
                for (int i = location.Line + 1; i <= Math.Min(lines.Count - 1, location.Line + 2); i++)
                {
                    after.Add(lines[i].ToString());
                }

                // Create new location with context
                return new NavigationLocation
                {
                    FilePath = location.FilePath,
                    Line = location.Line,
                    Column = location.Column,
                    Symbol = location.Symbol,
                    IsPartial = location.IsPartial,
                    IsPrimary = location.IsPrimary,
                    Context = new CodeContext
                    {
                        Before = before,
                        Target = targetLine.ToString(),
                        After = after,
                        StartLine = Math.Max(0, location.Line - 2),
                        EndLine = Math.Min(lines.Count - 1, location.Line + 2)
                    }
                };
            }
        }
        catch (Exception ex)
        {
            LogAddCodeContextFailed(_logger, ex, location.FilePath);
        }

        return location;
    }

    private static List<NavigationLocation> GetAlternativeLocations(
        IEnumerable<ISymbol> symbols)
    {
        var locations = new List<NavigationLocation>();

        foreach (var symbol in symbols)
        {
            var location = CreateNavigationLocation(symbol);
            if (location != null)
            {
                locations.Add(location);
            }
        }

        return locations;
    }

    private static string GetSourceLocationKey(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        return location == null
            ? symbol.ToDisplayString()
            : $"{location.SourceTree?.FilePath}:{location.SourceSpan.Start}";
    }

    /// <summary>
    /// Comparer for navigation locations to avoid duplicates
    /// </summary>
    private sealed class NavigationLocationComparer : IEqualityComparer<NavigationLocation>
    {
        public bool Equals(NavigationLocation? x, NavigationLocation? y)
        {
            if (x == null || y == null)
                return false;
            return x.FilePath == y.FilePath && x.Line == y.Line && x.Column == y.Column;
        }

        public int GetHashCode(NavigationLocation obj)
        {
            return HashCode.Combine(obj.FilePath, obj.Line, obj.Column);
        }
    }
}
