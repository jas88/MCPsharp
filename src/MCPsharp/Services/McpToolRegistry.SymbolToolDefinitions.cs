using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Symbol, class structure and reference tool definitions
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Symbol, class structure, reference and project parsing tool definitions
    /// </summary>
    private static List<McpTool> SymbolToolDefinitions() => new()
    {
        new McpTool
        {
            Name = "find_symbol",
            Description = "Search for symbols (classes, methods, properties) by name using the semantic index. PREFERRED over reading files directly - returns structured symbol data with location info. Supports partial matching and kind filtering (class, method, property, field, etc.). Use this to locate code elements before examining them in detail.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "name",
                    Type = "string",
                    Description = "Symbol name to search for",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "kind",
                    Type = "string",
                    Description = "Optional symbol kind filter (class, method, property, etc.)",
                    Required = false
                }
            )
        },
        new McpTool
        {
            Name = "get_symbol_info",
            Description = "Get detailed information about a symbol at a specific location",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "filePath",
                    Type = "string",
                    Description = "Path to the file containing the symbol",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "line",
                    Type = "integer",
                    Description = "Line number (0-indexed)",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "column",
                    Type = "integer",
                    Description = "Column number (0-indexed)",
                    Required = true
                }
            )
        },
        new McpTool
        {
            Name = "get_class_structure",
            Description = "Get complete structure of a class including all members, inheritance, and implementations. PREFERRED over reading the file directly - returns structured member data with types, accessibility, and relationships. Use to understand a class before modifying it or to analyze its design.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "className",
                    Type = "string",
                    Description = "Name of the class to analyze",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "filePath",
                    Type = "string",
                    Description = "Optional file path if multiple classes have the same name",
                    Required = false
                }
            )
        },
        new McpTool
        {
            Name = "add_class_property",
            Description = "Add a property to an existing class",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "className",
                    Type = "string",
                    Description = "Name of the class",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "propertyName",
                    Type = "string",
                    Description = "Name of the property to add",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "propertyType",
                    Type = "string",
                    Description = "Type of the property (e.g., 'string', 'int', 'List<string>')",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "accessibility",
                    Type = "string",
                    Description = "Accessibility modifier (public, private, protected, internal)",
                    Required = false,
                    Default = "public"
                },
                new PropertyDefinition
                {
                    Name = "hasGetter",
                    Type = "boolean",
                    Description = "Whether property has a getter",
                    Required = false,
                    Default = true
                },
                new PropertyDefinition
                {
                    Name = "hasSetter",
                    Type = "boolean",
                    Description = "Whether property has a setter",
                    Required = false,
                    Default = true
                }
            )
        },
        new McpTool
        {
            Name = "add_class_method",
            Description = "Add a method to an existing class",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "className",
                    Type = "string",
                    Description = "Name of the class",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "methodName",
                    Type = "string",
                    Description = "Name of the method to add",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "returnType",
                    Type = "string",
                    Description = "Return type of the method",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "parameters",
                    Type = "array",
                    Description = "Optional array of parameters, each with a name and a type",
                    Required = false,
                    Items = MethodParameterItems
                },
                new PropertyDefinition
                {
                    Name = "accessibility",
                    Type = "string",
                    Description = "Accessibility modifier (public, private, protected, internal)",
                    Required = false,
                    Default = "public"
                },
                new PropertyDefinition
                {
                    Name = "body",
                    Type = "string",
                    Description = "Optional method body",
                    Required = false
                }
            )
        },
        new McpTool
        {
            Name = "find_references",
            Description = "Find all references to a symbol across the codebase using semantic analysis. More accurate than text search as it understands symbol identity, not just text matches. Use for refactoring impact analysis or understanding how code is used. Preferred over grep for tracking symbol usage.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "symbolName",
                    Type = "string",
                    Description = "Symbol name to find references for (alternative to location-based search)",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "filePath",
                    Type = "string",
                    Description = "File path for location-based search",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "line",
                    Type = "integer",
                    Description = "Line number for location-based search",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "column",
                    Type = "integer",
                    Description = "Column number for location-based search",
                    Required = false
                }
            )
        },
        new McpTool
        {
            Name = "find_implementations",
            Description = "Find all implementations of an interface or abstract class",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "symbolName",
                    Type = "string",
                    Description = "Name of the interface or abstract class",
                    Required = true
                }
            )
        },
        new McpTool
        {
            Name = "parse_project",
            Description = "Parse a .csproj file and return project information",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "projectPath",
                    Type = "string",
                    Description = "Path to the .csproj file",
                    Required = true
                }
            )
        },
    };
}
