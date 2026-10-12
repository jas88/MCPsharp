using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for analyzing class structures
/// </summary>
public class ClassStructureService
{
    private readonly RoslynWorkspace _workspace;

    public ClassStructureService(RoslynWorkspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// Find a source-declared class by name in any project of the solution.
    /// When <paramref name="filePath"/> is given, only a class declared in that file is accepted.
    /// </summary>
    public INamedTypeSymbol? FindClassSymbol(string className, string? filePath = null)
    {
        var candidates = _workspace.FindSourceSymbols(className, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .Where(t => t.TypeKind == TypeKind.Class);

        return filePath == null
            ? candidates.FirstOrDefault()
            : candidates.FirstOrDefault(t => t.Locations.Any(l =>
                l.IsInSource && l.GetLineSpan().Path.Equals(filePath, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Get complete structure of a class
    /// </summary>
    public async Task<ClassStructure?> GetClassStructureAsync(string className, string? filePath = null)
    {
        var classSymbol = FindClassSymbol(className, filePath);
        if (classSymbol == null)
        {
            return null;
        }

        // Get properties
        var properties = new List<PropertyStructure>();
        foreach (var prop in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            var location = prop.Locations.FirstOrDefault();
            properties.Add(new PropertyStructure
            {
                Name = prop.Name,
                Type = prop.Type.ToDisplayString(),
                Accessibility = prop.DeclaredAccessibility.ToString().ToLowerInvariant(),
                HasGetter = prop.GetMethod != null,
                HasSetter = prop.SetMethod != null,
                Line = location?.GetLineSpan().StartLinePosition.Line ?? 0
            });
        }

        // Get methods
        var methods = new List<MethodStructure>();
        foreach (var method in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            // Skip special methods
            if (method.MethodKind != MethodKind.Ordinary && method.MethodKind != MethodKind.Constructor)
            {
                continue;
            }

            var location = method.Locations.FirstOrDefault();
            methods.Add(new MethodStructure
            {
                Name = method.Name,
                ReturnType = method.ReturnType.ToDisplayString(),
                Accessibility = method.DeclaredAccessibility.ToString().ToLowerInvariant(),
                IsOverride = method.IsOverride,
                IsVirtual = method.IsVirtual,
                IsAbstract = method.IsAbstract,
                IsStatic = method.IsStatic,
                Parameters = method.Parameters.Select(p => new ParameterStructure
                {
                    Name = p.Name,
                    Type = p.Type.ToDisplayString()
                }).ToList(),
                Line = location?.GetLineSpan().StartLinePosition.Line ?? 0
            });
        }

        // Get fields
        var fields = new List<FieldStructure>();
        foreach (var field in classSymbol.GetMembers().OfType<IFieldSymbol>())
        {
            var location = field.Locations.FirstOrDefault();
            fields.Add(new FieldStructure
            {
                Name = field.Name,
                Type = field.Type.ToDisplayString(),
                Accessibility = field.DeclaredAccessibility.ToString().ToLowerInvariant(),
                IsReadOnly = field.IsReadOnly,
                IsConst = field.IsConst,
                IsStatic = field.IsStatic,
                Line = location?.GetLineSpan().StartLinePosition.Line ?? 0
            });
        }

        // Get class location
        var classLocation = classSymbol.Locations.FirstOrDefault();
        LocationInfo? locationInfo = null;
        if (classLocation != null && classLocation.IsInSource)
        {
            var syntaxRef = classSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef != null)
            {
                var syntax = await syntaxRef.GetSyntaxAsync();
                if (syntax is ClassDeclarationSyntax classDecl)
                {
                    var startLine = classDecl.GetLocation().GetLineSpan().StartLinePosition.Line;
                    var endLine = classDecl.GetLocation().GetLineSpan().EndLinePosition.Line;
                    locationInfo = new LocationInfo { StartLine = startLine, EndLine = endLine };
                }
            }
        }

        return new ClassStructure
        {
            Name = classSymbol.Name,
            Namespace = classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
            Kind = "class",
            Accessibility = classSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            IsAbstract = classSymbol.IsAbstract,
            IsSealed = classSymbol.IsSealed,
            IsStatic = classSymbol.IsStatic,
            BaseTypes = classSymbol.BaseType != null && classSymbol.BaseType.SpecialType != SpecialType.System_Object
                ? new List<string> { classSymbol.BaseType.ToDisplayString() }
                : new List<string>(),
            Interfaces = classSymbol.Interfaces.Select(i => i.ToDisplayString()).ToList(),
            Properties = properties,
            Methods = methods,
            Fields = fields,
            Location = locationInfo
        };
    }
}
