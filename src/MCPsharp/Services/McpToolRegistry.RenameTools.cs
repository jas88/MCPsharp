using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services;

/// <summary>
/// Symbol rename tool
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Rename tool definition
    /// </summary>
    private static List<McpTool> RenameToolDefinitions() => new()
    {
        new McpTool
        {
            Name = "rename_symbol",
            Description = "Rename a symbol across all files in the project with conflict detection and preview mode",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "file_path",
                    Type = "string",
                    Description = "File containing the symbol (helps locate the symbol precisely)",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "line",
                    Type = "integer",
                    Description = "Line number of the symbol (1-based)",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "column",
                    Type = "integer",
                    Description = "Column position of the symbol (0-based)",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "symbol_name",
                    Type = "string",
                    Description = "Current name of the symbol to rename",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "new_name",
                    Type = "string",
                    Description = "New name for the symbol",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "symbol_kind",
                    Type = "string",
                    Description = "Type of symbol (class, interface, method, property, field, parameter, namespace, local, type_parameter, any)",
                    Required = false,
                    Default = "any"
                },
                new PropertyDefinition
                {
                    Name = "containing_type",
                    Type = "string",
                    Description = "For members, the containing type name (helps disambiguate)",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "rename_in_comments",
                    Type = "boolean",
                    Description = "Also rename occurrences in comments",
                    Required = false,
                    Default = true
                },
                new PropertyDefinition
                {
                    Name = "rename_in_strings",
                    Type = "boolean",
                    Description = "Also rename occurrences in string literals (use with caution)",
                    Required = false,
                    Default = false
                },
                new PropertyDefinition
                {
                    Name = "rename_overloads",
                    Type = "boolean",
                    Description = "For methods, rename all overloads with the same name",
                    Required = false,
                    Default = false
                },
                new PropertyDefinition
                {
                    Name = "preview",
                    Type = "boolean",
                    Description = "Preview changes without applying them",
                    Required = false,
                    Default = true
                },
                new PropertyDefinition
                {
                    Name = "force_public_api_change",
                    Type = "boolean",
                    Description = "Allow renaming public API symbols (breaking change)",
                    Required = false,
                    Default = false
                },
                new PropertyDefinition
                {
                    Name = "handle_conflicts",
                    Type = "string",
                    Description = "How to handle conflicts (abort, prompt, auto_resolve)",
                    Required = false,
                    Default = "abort"
                }
            )
        },
    };

    private async Task<ToolCallResult> ExecuteRenameSymbol(JsonDocument arguments, CancellationToken ct = default)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_renameSymbol == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        // Parse required parameters
        if (!arguments.RootElement.TryGetProperty("symbol_name", out var symbolNameElement) ||
            string.IsNullOrEmpty(symbolNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "symbol_name is required" };
        }

        if (!arguments.RootElement.TryGetProperty("new_name", out var newNameElement) ||
            string.IsNullOrEmpty(newNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "new_name is required" };
        }

        var symbolName = symbolNameElement.GetString()!;
        var newName = newNameElement.GetString()!;

        // Parse optional parameters
        string? filePath = null;
        if (arguments.RootElement.TryGetProperty("file_path", out var filePathElement))
        {
            filePath = filePathElement.GetString();
        }

        int? line = null;
        if (arguments.RootElement.TryGetProperty("line", out var lineElement))
        {
            line = lineElement.GetInt32();
        }

        int? column = null;
        if (arguments.RootElement.TryGetProperty("column", out var columnElement))
        {
            column = columnElement.GetInt32();
        }

        string? containingType = null;
        if (arguments.RootElement.TryGetProperty("containing_type", out var containingTypeElement))
        {
            containingType = containingTypeElement.GetString();
        }

        // Parse symbol kind
        var symbolKind = SymbolKind.Any;
        if (arguments.RootElement.TryGetProperty("symbol_kind", out var symbolKindElement))
        {
            var symbolKindStr = symbolKindElement.GetString();
            if (!string.IsNullOrEmpty(symbolKindStr))
            {
                symbolKind = symbolKindStr.ToLowerInvariant() switch
                {
                    "class" => SymbolKind.Class,
                    "interface" => SymbolKind.Interface,
                    "method" => SymbolKind.Method,
                    "property" => SymbolKind.Property,
                    "field" => SymbolKind.Field,
                    "parameter" => SymbolKind.Parameter,
                    "namespace" => SymbolKind.Namespace,
                    "local" => SymbolKind.Local,
                    "type_parameter" => SymbolKind.TypeParameter,
                    _ => SymbolKind.Any
                };
            }
        }

        // Parse boolean options
        var renameInComments = true;
        if (arguments.RootElement.TryGetProperty("rename_in_comments", out var commentsElement))
        {
            renameInComments = commentsElement.GetBoolean();
        }

        var renameInStrings = false;
        if (arguments.RootElement.TryGetProperty("rename_in_strings", out var stringsElement))
        {
            renameInStrings = stringsElement.GetBoolean();
        }

        var renameOverloads = false;
        if (arguments.RootElement.TryGetProperty("rename_overloads", out var overloadsElement))
        {
            renameOverloads = overloadsElement.GetBoolean();
        }

        var previewOnly = true; // Default to preview mode for safety
        if (arguments.RootElement.TryGetProperty("preview", out var previewElement))
        {
            previewOnly = previewElement.GetBoolean();
        }

        var forcePublicApiChange = false;
        if (arguments.RootElement.TryGetProperty("force_public_api_change", out var forceElement))
        {
            forcePublicApiChange = forceElement.GetBoolean();
        }

        // Parse conflict handling
        var handleConflicts = ConflictHandling.Abort;
        if (arguments.RootElement.TryGetProperty("handle_conflicts", out var conflictElement))
        {
            var conflictStr = conflictElement.GetString();
            if (!string.IsNullOrEmpty(conflictStr))
            {
                handleConflicts = conflictStr.ToLowerInvariant() switch
                {
                    "prompt" => ConflictHandling.Prompt,
                    "auto_resolve" => ConflictHandling.AutoResolve,
                    _ => ConflictHandling.Abort
                };
            }
        }

        // Create rename request
        var request = new RenameRequest
        {
            OldName = symbolName,
            NewName = newName,
            SymbolKind = symbolKind,
            ContainingType = containingType,
            FilePath = filePath,
            Line = line,
            Column = column,
            RenameInComments = renameInComments,
            RenameInStrings = renameInStrings,
            RenameOverloads = renameOverloads,
            PreviewOnly = previewOnly,
            ForcePublicApiChange = forcePublicApiChange,
            HandleConflicts = handleConflicts
        };

        // Execute rename
        var result = await _renameSymbol.RenameSymbolAsync(request, ct);

        if (!result.Success)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = result.Errors.Count > 0
                    ? string.Join("; ", result.Errors)
                    : "Rename operation failed"
            };
        }

        // Build response
        var response = new
        {
            success = result.Success,
            renamed_count = result.RenamedCount,
            files_modified = result.FilesModified,
            conflicts = result.Conflicts.Select(c => new
            {
                type = c.Type.ToString(),
                severity = c.Severity.ToString(),
                description = c.Description,
                location = c.Location != null ? new
                {
                    file = c.Location.SourceTree?.FilePath,
                    line = c.Location.GetLineSpan().StartLinePosition.Line + 1,
                    column = c.Location.GetLineSpan().StartLinePosition.Character
                } : null
            }),
            preview = result.Preview?.Select(fc => new
            {
                file_path = fc.FilePath,
                changes = fc.Changes.Select(tc => new
                {
                    start = tc.Span.Start,
                    length = tc.Span.Length,
                    new_text = tc.NewText
                })
            }),
            preview_mode = previewOnly
        };

        return new ToolCallResult
        {
            Success = true,
            Result = response
        };
    }
}
