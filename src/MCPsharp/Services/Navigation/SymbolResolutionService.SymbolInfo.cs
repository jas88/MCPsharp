using Microsoft.CodeAnalysis;
using MCPsharp.Models.Navigation;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Symbol location, kind and documentation helpers.
/// </summary>
public partial class SymbolResolutionService
{
    private static NavigationLocation? GetDeclarationLocation(ISymbol symbol, SymbolResolutionOptions options)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null)
        {
            // Check if symbol is in metadata and we should include it
            if (options.IncludeMetadata && symbol.Locations.Any(l => l.IsInMetadata))
            {
                // Return a special marker for metadata symbols
                return new NavigationLocation
                {
                    FilePath = $"[metadata] {symbol.ContainingAssembly?.Name ?? "Unknown"}",
                    Line = -1,
                    Column = -1,
                    Symbol = CreateNavigationSymbolInfo(symbol)
                };
            }
            return null;
        }

        var lineSpan = location.GetLineSpan();
        return new NavigationLocation
        {
            FilePath = lineSpan.Path,
            Line = lineSpan.StartLinePosition.Line,
            Column = lineSpan.StartLinePosition.Character,
            Symbol = CreateNavigationSymbolInfo(symbol),
            IsPartial = IsPartialSymbol(symbol),
            IsPrimary = IsPrimaryLocation(symbol, location)
        };
    }

    private static List<NavigationLocation> GetAllSymbolLocations(ISymbol symbol)
    {
        var locations = new List<NavigationLocation>();

        foreach (var location in symbol.Locations.Where(l => l.IsInSource))
        {
            var lineSpan = location.GetLineSpan();
            locations.Add(new NavigationLocation
            {
                FilePath = lineSpan.Path,
                Line = lineSpan.StartLinePosition.Line,
                Column = lineSpan.StartLinePosition.Character,
                Symbol = CreateNavigationSymbolInfo(symbol),
                IsPartial = IsPartialSymbol(symbol),
                IsPrimary = IsPrimaryLocation(symbol, location)
            });
        }

        return locations.OrderBy(l => l.FilePath).ThenBy(l => l.Line).ToList();
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
        // Add property-specific information
        else if (symbol is IPropertySymbol property)
        {
            var modifiers = new List<string>();
            if (property.IsReadOnly)
                modifiers.Add("readonly");
            if (property.IsWriteOnly)
                modifiers.Add("writeonly");

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
                ReturnType = property.Type.ToDisplayString(),
                Modifiers = modifiers
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
            INamedTypeSymbol type => type.TypeKind switch
            {
                TypeKind.Class => "class",
                TypeKind.Interface => "interface",
                TypeKind.Struct => "struct",
                TypeKind.Enum => "enum",
                TypeKind.Delegate => "delegate",
                _ => "type"
            },
            IMethodSymbol method => method.MethodKind switch
            {
                MethodKind.Constructor => "constructor",
                MethodKind.Destructor => "destructor",
                MethodKind.UserDefinedOperator => "operator",
                MethodKind.Conversion => "conversion",
                MethodKind.PropertyGet => "getter",
                MethodKind.PropertySet => "setter",
                _ => "method"
            },
            IPropertySymbol => "property",
            IFieldSymbol => "field",
            IEventSymbol => "event",
            INamespaceSymbol => "namespace",
            IParameterSymbol => "parameter",
            ILocalSymbol => "local",
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

        // Simple extraction of summary tag content
        var summaryStart = xml.IndexOf("<summary>", StringComparison.Ordinal);
        var summaryEnd = xml.IndexOf("</summary>", StringComparison.Ordinal);
        if (summaryStart >= 0 && summaryEnd > summaryStart)
        {
            var summary = xml.Substring(summaryStart + 9, summaryEnd - summaryStart - 9);
            // Clean up whitespace and remove extra indentation
            return summary.Trim().Replace("\n    ", " ").Replace("\n", " ");
        }

        return null;
    }

    private static bool IsPartialSymbol(ISymbol symbol)
    {
        return symbol.Locations.Count(l => l.IsInSource) > 1;
    }

    private static bool IsPrimaryLocation(ISymbol symbol, Location location)
    {
        // For partial symbols, the primary location is usually the first one
        // or the one with the most complete definition
        var sourceLocations = symbol.Locations.Where(l => l.IsInSource).ToList();
        if (sourceLocations.Count <= 1)
        {
            return true;
        }

        // The first source location is treated as primary for every symbol kind
        // (including partial methods, where it is usually the defining declaration)
        return sourceLocations[0] == location;
    }

    private static int GetTextPosition(Microsoft.CodeAnalysis.Text.SourceText text, int line, int column)
    {
        if (line < 0 || line >= text.Lines.Count)
        {
            return -1;
        }

        var textLine = text.Lines[line];
        if (column < 0 || column > textLine.Span.Length)
        {
            return -1;
        }

        return textLine.Start + column;
    }
}
