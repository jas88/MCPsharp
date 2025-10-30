using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for performing semantic edits to C# code
/// </summary>
public class SemanticEditService
{
    private readonly RoslynWorkspace _workspace;
    private readonly ClassStructureService _classStructure;

    public SemanticEditService(RoslynWorkspace workspace, ClassStructureService classStructure)
    {
        _workspace = workspace;
        _classStructure = classStructure;
    }

    /// <summary>
    /// Add a property to a class
    /// </summary>
    public async Task<EditResult> AddPropertyAsync(
        string className,
        string propertyName,
        string propertyType,
        string accessibility = "public",
        bool hasGetter = true,
        bool hasSetter = true,
        string? filePath = null)
    {
        try
        {
            var structure = await _classStructure.GetClassStructureAsync(className, filePath);
            if (structure == null)
            {
                return new EditResult { Success = false, Error = $"Class '{className}' not found" };
            }

            // Find the class declaration
            var compilation = _workspace.GetCompilation();
            if (compilation == null)
            {
                return new EditResult { Success = false, Error = "Workspace not initialized" };
            }

            var classSymbol = compilation.GetSymbolsWithName(className, SymbolFilter.Type)
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(t => t.TypeKind == TypeKind.Class);

            if (classSymbol == null)
            {
                return new EditResult { Success = false, Error = $"Class '{className}' not found" };
            }

            var location = classSymbol.Locations.FirstOrDefault();
            if (location == null || !location.IsInSource)
            {
                return new EditResult { Success = false, Error = $"Class '{className}' location not found" };
            }

            var document = _workspace.GetDocument(location.GetLineSpan().Path);
            if (document == null)
            {
                return new EditResult { Success = false, Error = "Document not found" };
            }

            var syntaxTree = await document.GetSyntaxTreeAsync();
            var root = await syntaxTree!.GetRootAsync();
            var classDecl = root.FindToken(location.SourceSpan.Start).Parent?
                .AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();

            if (classDecl == null)
            {
                return new EditResult { Success = false, Error = "Class declaration not found" };
            }

            // Find insertion point (after last property or before first method)
            var lastProperty = classDecl.Members.OfType<PropertyDeclarationSyntax>().LastOrDefault();
            var firstMethod = classDecl.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault();

            int insertLine;
            if (lastProperty != null)
            {
                insertLine = lastProperty.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
            }
            else if (firstMethod != null)
            {
                insertLine = firstMethod.GetLocation().GetLineSpan().StartLinePosition.Line;
            }
            else
            {
                // Insert after opening brace
                insertLine = classDecl.OpenBraceToken.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
            }

            // Generate property code
            var getter = hasGetter ? "get; " : "";
            var setter = hasSetter ? "set; " : "";
            var indentation = DetectIndentation(classDecl);
            var generatedCode = $"{indentation}{accessibility} {propertyType} {propertyName} {{ {getter}{setter}}}";

            return new EditResult
            {
                Success = true,
                ClassName = className,
                MemberName = propertyName,
                InsertedAt = new LocationInfo { StartLine = insertLine, EndLine = insertLine },
                GeneratedCode = generatedCode
            };
        }
        catch (Exception ex)
        {
            return new EditResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Add a method to a class
    /// </summary>
    public async Task<EditResult> AddMethodAsync(
        string className,
        string methodName,
        string returnType,
        List<ParameterStructure>? parameters = null,
        string accessibility = "public",
        string? body = null,
        string? filePath = null)
    {
        try
        {
            var structure = await _classStructure.GetClassStructureAsync(className, filePath);
            if (structure == null)
            {
                return new EditResult { Success = false, Error = $"Class '{className}' not found" };
            }

            var compilation = _workspace.GetCompilation();
            if (compilation == null)
            {
                return new EditResult { Success = false, Error = "Workspace not initialized" };
            }

            var classSymbol = compilation.GetSymbolsWithName(className, SymbolFilter.Type)
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(t => t.TypeKind == TypeKind.Class);

            if (classSymbol == null)
            {
                return new EditResult { Success = false, Error = $"Class '{className}' not found" };
            }

            var location = classSymbol.Locations.FirstOrDefault();
            if (location == null || !location.IsInSource)
            {
                return new EditResult { Success = false, Error = $"Class '{className}' location not found" };
            }

            var document = _workspace.GetDocument(location.GetLineSpan().Path);
            if (document == null)
            {
                return new EditResult { Success = false, Error = "Document not found" };
            }

            var syntaxTree = await document.GetSyntaxTreeAsync();
            var root = await syntaxTree!.GetRootAsync();
            var classDecl = root.FindToken(location.SourceSpan.Start).Parent?
                .AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();

            if (classDecl == null)
            {
                return new EditResult { Success = false, Error = "Class declaration not found" };
            }

            // Insert before closing brace
            int insertLine = classDecl.CloseBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line;

            // Generate method code
            var paramList = parameters != null && parameters.Any()
                ? string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"))
                : "";

            var indentation = DetectIndentation(classDecl);
            var methodBody = body ?? $"throw new NotImplementedException();";
            var generatedCode = $"{indentation}{accessibility} {returnType} {methodName}({paramList})\n" +
                              $"{indentation}{{\n" +
                              $"{indentation}    {methodBody}\n" +
                              $"{indentation}}}";

            return new EditResult
            {
                Success = true,
                ClassName = className,
                MemberName = methodName,
                InsertedAt = new LocationInfo { StartLine = insertLine, EndLine = insertLine },
                GeneratedCode = generatedCode
            };
        }
        catch (Exception ex)
        {
            return new EditResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Add a field to a class
    /// </summary>
    public async Task<EditResult> AddFieldAsync(
        string className,
        string fieldName,
        string fieldType,
        string accessibility = "private",
        bool isReadOnly = false,
        string? filePath = null)
    {
        try
        {
            var structure = await _classStructure.GetClassStructureAsync(className, filePath);
            if (structure == null)
            {
                return new EditResult { Success = false, Error = $"Class '{className}' not found" };
            }

            var compilation = _workspace.GetCompilation();
            if (compilation == null)
            {
                return new EditResult { Success = false, Error = "Workspace not initialized" };
            }

            var classSymbol = compilation.GetSymbolsWithName(className, SymbolFilter.Type)
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(t => t.TypeKind == TypeKind.Class);

            if (classSymbol == null)
            {
                return new EditResult { Success = false, Error = $"Class '{className}' not found" };
            }

            var location = classSymbol.Locations.FirstOrDefault();
            if (location == null || !location.IsInSource)
            {
                return new EditResult { Success = false, Error = $"Class '{className}' location not found" };
            }

            var document = _workspace.GetDocument(location.GetLineSpan().Path);
            if (document == null)
            {
                return new EditResult { Success = false, Error = "Document not found" };
            }

            var syntaxTree = await document.GetSyntaxTreeAsync();
            var root = await syntaxTree!.GetRootAsync();
            var classDecl = root.FindToken(location.SourceSpan.Start).Parent?
                .AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();

            if (classDecl == null)
            {
                return new EditResult { Success = false, Error = "Class declaration not found" };
            }

            // Find insertion point (after opening brace or before first member)
            var firstMember = classDecl.Members.FirstOrDefault();
            int insertLine;

            if (firstMember != null)
            {
                insertLine = firstMember.GetLocation().GetLineSpan().StartLinePosition.Line;
            }
            else
            {
                insertLine = classDecl.OpenBraceToken.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
            }

            // Generate field code
            var indentation = DetectIndentation(classDecl);
            var readonlyModifier = isReadOnly ? "readonly " : "";
            var generatedCode = $"{indentation}{accessibility} {readonlyModifier}{fieldType} {fieldName};";

            return new EditResult
            {
                Success = true,
                ClassName = className,
                MemberName = fieldName,
                InsertedAt = new LocationInfo { StartLine = insertLine, EndLine = insertLine },
                GeneratedCode = generatedCode
            };
        }
        catch (Exception ex)
        {
            return new EditResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Generate property code with proper formatting
    /// </summary>
    public string GeneratePropertyCode(
        string name,
        string type,
        string accessibility,
        bool hasGetter,
        bool hasSetter,
        string indentation)
    {
        var getter = hasGetter ? "get; " : "";
        var setter = hasSetter ? "set; " : "";
        return $"{indentation}{accessibility} {type} {name} {{ {getter}{setter}}}";
    }

    /// <summary>
    /// Generate method code with proper formatting
    /// </summary>
    public string GenerateMethodCode(
        string name,
        string returnType,
        IEnumerable<ParameterStructure>? parameters,
        string accessibility,
        string? body,
        string indentation)
    {
        var isVoid = returnType == "void";
        var paramList = parameters != null && parameters.Any()
            ? string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"))
            : "";

        var methodBody = body ?? (isVoid ? "// TODO: Implement" : "throw new NotImplementedException();");

        return $"{indentation}{accessibility} {returnType} {name}({paramList})\n" +
               $"{indentation}{{\n" +
               $"{indentation}    {methodBody}\n" +
               $"{indentation}}}";
    }

    private string DetectIndentation(ClassDeclarationSyntax classDecl)
    {
        // Simple indentation detection - use 4 spaces or existing indentation
        var firstMember = classDecl.Members.FirstOrDefault();
        if (firstMember != null)
        {
            var text = firstMember.GetLeadingTrivia().ToString();
            if (text.Contains('\t'))
            {
                return "\t";
            }
            var spaces = text.TakeWhile(c => c == ' ').Count();
            return new string(' ', spaces > 0 ? spaces : 4);
        }
        return "    "; // Default to 4 spaces
    }
}
