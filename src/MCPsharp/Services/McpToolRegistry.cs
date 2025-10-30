using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services;

/// <summary>
/// Registry and executor for MCP tools
/// </summary>
public class McpToolRegistry
{
    private readonly ProjectContextManager _projectContext;
    private readonly List<McpTool> _tools;
    private FileOperationsService? _fileOperations;
    private RoslynWorkspace? _workspace;
    private SymbolQueryService? _symbolQuery;
    private ClassStructureService? _classStructure;
    private SemanticEditService? _semanticEdit;
    private ReferenceFinderService? _referenceFinder;
    private ProjectParserService? _projectParser;

    // Phase 2 services (optional)
    private readonly IWorkflowAnalyzerService? _workflowAnalyzer;
    private readonly IConfigAnalyzerService? _configAnalyzer;
    private readonly IImpactAnalyzerService? _impactAnalyzer;
    private readonly IFeatureTracerService? _featureTracer;

    public McpToolRegistry(
        ProjectContextManager projectContext,
        RoslynWorkspace? workspace = null,
        IWorkflowAnalyzerService? workflowAnalyzer = null,
        IConfigAnalyzerService? configAnalyzer = null,
        IImpactAnalyzerService? impactAnalyzer = null,
        IFeatureTracerService? featureTracer = null)
    {
        _projectContext = projectContext;
        _workspace = workspace;
        _workflowAnalyzer = workflowAnalyzer;
        _configAnalyzer = configAnalyzer;
        _impactAnalyzer = impactAnalyzer;
        _featureTracer = featureTracer;
        _tools = RegisterTools();
    }

    /// <summary>
    /// Get all available MCP tools
    /// </summary>
    public List<McpTool> GetTools() => _tools;

    /// <summary>
    /// Execute a tool by name with the provided arguments
    /// </summary>
    public async Task<ToolCallResult> ExecuteTool(ToolCallRequest request, CancellationToken ct = default)
    {
        try
        {
            return request.Name switch
            {
                "project_open" => await ExecuteProjectOpen(request.Arguments),
                "project_info" => ExecuteProjectInfo(),
                "file_list" => ExecuteFileList(request.Arguments),
                "file_read" => await ExecuteFileRead(request.Arguments, ct),
                "file_write" => await ExecuteFileWrite(request.Arguments, ct),
                "file_edit" => await ExecuteFileEdit(request.Arguments, ct),
                "find_symbol" => await ExecuteFindSymbol(request.Arguments),
                "get_symbol_info" => await ExecuteGetSymbolInfo(request.Arguments),
                "get_class_structure" => await ExecuteGetClassStructure(request.Arguments),
                "add_class_property" => await ExecuteAddClassProperty(request.Arguments),
                "add_class_method" => await ExecuteAddClassMethod(request.Arguments),
                "find_references" => await ExecuteFindReferences(request.Arguments),
                "find_implementations" => await ExecuteFindImplementations(request.Arguments),
                "parse_project" => await ExecuteParseProject(request.Arguments),
                // Phase 2 tools
                "get_workflows" => await ExecuteGetWorkflows(request.Arguments),
                "parse_workflow" => await ExecuteParseWorkflow(request.Arguments),
                "validate_workflow_consistency" => await ExecuteValidateWorkflowConsistency(request.Arguments),
                "get_config_schema" => await ExecuteGetConfigSchema(request.Arguments),
                "merge_configs" => await ExecuteMergeConfigs(request.Arguments),
                "analyze_impact" => await ExecuteAnalyzeImpact(request.Arguments),
                "trace_feature" => await ExecuteTraceFeature(request.Arguments),
                _ => new ToolCallResult
                {
                    Success = false,
                    Error = $"Unknown tool: {request.Name}"
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Tool execution failed: {ex.Message}"
            };
        }
    }

    private List<McpTool> RegisterTools()
    {
        return new List<McpTool>
        {
            new McpTool
            {
                Name = "project_open",
                Description = "Open a project directory for file operations",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "path",
                        Type = "string",
                        Description = "Absolute path to the project directory",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "project_info",
                Description = "Get information about the currently open project",
                InputSchema = JsonSchemaHelper.CreateSchema()
            },
            new McpTool
            {
                Name = "file_list",
                Description = "List files in the project, optionally filtered by glob pattern",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "pattern",
                        Type = "string",
                        Description = "Optional glob pattern (e.g., '**/*.cs', 'src/**/*.json')",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "include_hidden",
                        Type = "boolean",
                        Description = "Whether to include hidden files",
                        Required = false,
                        Default = false
                    }
                )
            },
            new McpTool
            {
                Name = "file_read",
                Description = "Read the contents of a file",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "path",
                        Type = "string",
                        Description = "Path to the file (relative to project root or absolute)",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "file_write",
                Description = "Write content to a file (creates if doesn't exist, overwrites if exists)",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "path",
                        Type = "string",
                        Description = "Path to the file (relative to project root or absolute)",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "content",
                        Type = "string",
                        Description = "Content to write to the file",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "create_directories",
                        Type = "boolean",
                        Description = "Whether to create parent directories if they don't exist",
                        Required = false,
                        Default = true
                    }
                )
            },
            new McpTool
            {
                Name = "file_edit",
                Description = "Apply text edits to a file",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "path",
                        Type = "string",
                        Description = "Path to the file (relative to project root or absolute)",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "edits",
                        Type = "array",
                        Description = "Array of edit operations to apply",
                        Required = true,
                        Items = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object>
                            {
                                ["type"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string",
                                    ["enum"] = new[] { "replace", "insert", "delete" }
                                }
                            }
                        }
                    }
                )
            },
            new McpTool
            {
                Name = "find_symbol",
                Description = "Find symbols (classes, methods, properties) by name",
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
                Description = "Get complete structure of a class including all members",
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
                        Description = "Optional array of parameters",
                        Required = false
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
                Description = "Find all references to a symbol",
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
            // ===== Phase 2 Tools =====
            new McpTool
            {
                Name = "get_workflows",
                Description = "Get all GitHub Actions workflows in a project",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectRoot",
                        Type = "string",
                        Description = "Root directory of the project",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "parse_workflow",
                Description = "Parse a GitHub Actions workflow file",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "workflowPath",
                        Type = "string",
                        Description = "Path to the workflow YAML file",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "validate_workflow_consistency",
                Description = "Validate GitHub Actions workflow against project configuration",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "workflowPath",
                        Type = "string",
                        Description = "Path to the workflow YAML file",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "get_config_schema",
                Description = "Get the schema of a configuration file (JSON/YAML)",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "configPath",
                        Type = "string",
                        Description = "Path to the configuration file",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "merge_configs",
                Description = "Merge multiple configuration files",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "configPaths",
                        Type = "array",
                        Description = "Array of configuration file paths to merge",
                        Required = true,
                        Items = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        }
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_impact",
                Description = "Analyze the impact of a code change across the project",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "filePath",
                        Type = "string",
                        Description = "Path to the file being changed",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "changeType",
                        Type = "string",
                        Description = "Type of change (add, modify, delete, rename)",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "symbolName",
                        Type = "string",
                        Description = "Optional symbol name being changed",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "trace_feature",
                Description = "Trace a feature across multiple files in the project",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "featureName",
                        Type = "string",
                        Description = "Name of the feature to trace (alternative to entryPoint)",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "entryPoint",
                        Type = "string",
                        Description = "Entry point to start feature discovery (alternative to featureName)",
                        Required = false
                    }
                )
            }
        };
    }

    /// <summary>
    /// Ensure Roslyn workspace is initialized
    /// </summary>
    private async Task EnsureWorkspaceInitializedAsync()
    {
        if (_workspace != null && _projectContext.GetProjectContext() != null)
        {
            var context = _projectContext.GetProjectContext()!;
            if (!_workspace.IsInitialized)
            {
                await _workspace.InitializeAsync(context.RootPath);

                // Initialize services
                _symbolQuery = new SymbolQueryService(_workspace);
                _classStructure = new ClassStructureService(_workspace);
                _semanticEdit = new SemanticEditService(_workspace, _classStructure);
                _referenceFinder = new ReferenceFinderService(_workspace);
                _projectParser = new ProjectParserService();
            }
        }
    }

    private async Task<ToolCallResult> ExecuteProjectOpen(JsonDocument arguments)
    {
        var path = arguments.RootElement.GetProperty("path").GetString();
        if (string.IsNullOrEmpty(path))
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Path is required"
            };
        }

        try
        {
            _projectContext.OpenProject(path);
            _fileOperations = new FileOperationsService(path);

            // Initialize workspace if available
            if (_workspace != null)
            {
                await _workspace.InitializeAsync(path);
                _symbolQuery = new SymbolQueryService(_workspace);
                _classStructure = new ClassStructureService(_workspace);
                _semanticEdit = new SemanticEditService(_workspace, _classStructure);
                _referenceFinder = new ReferenceFinderService(_workspace);
                _projectParser = new ProjectParserService();
            }

            var context = _projectContext.GetProjectContext();
            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    Path = context?.RootPath ?? path,
                    Name = System.IO.Path.GetFileName(path)
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private ToolCallResult ExecuteProjectInfo()
    {
        var info = _projectContext.GetProjectInfo();
        if (info == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "No project is currently open"
            };
        }

        return new ToolCallResult
        {
            Success = true,
            Result = info
        };
    }

    private ToolCallResult ExecuteFileList(JsonDocument arguments)
    {
        if (_fileOperations == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "No project is open"
            };
        }

        string? pattern = null;
        bool includeHidden = false;

        if (arguments.RootElement.TryGetProperty("pattern", out var patternElement))
        {
            pattern = patternElement.GetString();
        }

        if (arguments.RootElement.TryGetProperty("include_hidden", out var hiddenElement))
        {
            includeHidden = hiddenElement.GetBoolean();
        }

        var result = _fileOperations.ListFiles(pattern, includeHidden);
        return new ToolCallResult
        {
            Success = true,
            Result = new
            {
                Files = result.Files.Select(f => new
                {
                    f.Path,
                    f.RelativePath,
                    f.Size,
                    f.LastModified,
                    f.IsHidden
                }),
                result.TotalFiles,
                result.Pattern
            }
        };
    }

    private async Task<ToolCallResult> ExecuteFileRead(JsonDocument arguments, CancellationToken ct)
    {
        if (_fileOperations == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "No project is open"
            };
        }

        var path = arguments.RootElement.GetProperty("path").GetString();
        if (string.IsNullOrEmpty(path))
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Path is required"
            };
        }

        var result = await _fileOperations.ReadFileAsync(path, ct);
        return new ToolCallResult
        {
            Success = result.Success,
            Result = result.Success ? new
            {
                result.Path,
                result.Content,
                result.Encoding,
                result.LineCount,
                result.Size
            } : null,
            Error = result.Error
        };
    }

    private async Task<ToolCallResult> ExecuteFileWrite(JsonDocument arguments, CancellationToken ct)
    {
        if (_fileOperations == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "No project is open"
            };
        }

        var path = arguments.RootElement.GetProperty("path").GetString();
        var content = arguments.RootElement.GetProperty("content").GetString();

        if (string.IsNullOrEmpty(path))
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Path is required"
            };
        }

        if (content == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Content is required"
            };
        }

        bool createDirectories = true;
        if (arguments.RootElement.TryGetProperty("create_directories", out var createDirElement))
        {
            createDirectories = createDirElement.GetBoolean();
        }

        var result = await _fileOperations.WriteFileAsync(path, content, createDirectories, ct);
        return new ToolCallResult
        {
            Success = result.Success,
            Result = result.Success ? new
            {
                result.Path,
                result.BytesWritten,
                result.Created
            } : null,
            Error = result.Error
        };
    }

    private async Task<ToolCallResult> ExecuteFileEdit(JsonDocument arguments, CancellationToken ct)
    {
        if (_fileOperations == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "No project is open"
            };
        }

        var path = arguments.RootElement.GetProperty("path").GetString();
        if (string.IsNullOrEmpty(path))
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Path is required"
            };
        }

        var editsArray = arguments.RootElement.GetProperty("edits");
        var edits = new List<TextEdit>();

        foreach (var editElement in editsArray.EnumerateArray())
        {
            var type = editElement.GetProperty("type").GetString();
            TextEdit edit = type switch
            {
                "replace" => new ReplaceEdit
                {
                    StartLine = editElement.GetProperty("start_line").GetInt32(),
                    StartColumn = editElement.GetProperty("start_column").GetInt32(),
                    EndLine = editElement.GetProperty("end_line").GetInt32(),
                    EndColumn = editElement.GetProperty("end_column").GetInt32(),
                    NewText = editElement.GetProperty("new_text").GetString() ?? ""
                },
                "insert" => (TextEdit)new InsertEdit
                {
                    Line = editElement.GetProperty("line").GetInt32(),
                    Column = editElement.GetProperty("column").GetInt32(),
                    Text = editElement.GetProperty("text").GetString() ?? ""
                },
                "delete" => (TextEdit)new DeleteEdit
                {
                    StartLine = editElement.GetProperty("start_line").GetInt32(),
                    StartColumn = editElement.GetProperty("start_column").GetInt32(),
                    EndLine = editElement.GetProperty("end_line").GetInt32(),
                    EndColumn = editElement.GetProperty("end_column").GetInt32()
                },
                _ => throw new ArgumentException($"Unknown edit type: {type}")
            };
            edits.Add(edit);
        }

        var result = await _fileOperations.EditFileAsync(path, edits, ct);
        return new ToolCallResult
        {
            Success = result.Success,
            Result = result.Success ? new
            {
                result.Path,
                result.EditsApplied,
                result.NewContent
            } : null,
            Error = result.Error
        };
    }

    private async Task<ToolCallResult> ExecuteFindSymbol(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_symbolQuery == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var name = arguments.RootElement.GetProperty("name").GetString();
        if (string.IsNullOrEmpty(name))
        {
            return new ToolCallResult { Success = false, Error = "Name is required" };
        }

        string? kind = null;
        if (arguments.RootElement.TryGetProperty("kind", out var kindElement))
        {
            kind = kindElement.GetString();
        }

        var results = await _symbolQuery.FindSymbolsAsync(name, kind);
        return new ToolCallResult
        {
            Success = true,
            Result = new { Symbols = results, TotalResults = results.Count }
        };
    }

    private async Task<ToolCallResult> ExecuteGetSymbolInfo(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_symbolQuery == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var filePath = arguments.RootElement.GetProperty("filePath").GetString();
        var line = arguments.RootElement.GetProperty("line").GetInt32();
        var column = arguments.RootElement.GetProperty("column").GetInt32();

        if (string.IsNullOrEmpty(filePath))
        {
            return new ToolCallResult { Success = false, Error = "FilePath is required" };
        }

        var result = await _symbolQuery.GetSymbolAtLocationAsync(filePath, line, column);
        if (result == null)
        {
            return new ToolCallResult { Success = false, Error = "Symbol not found at location" };
        }

        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteGetClassStructure(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_classStructure == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var className = arguments.RootElement.GetProperty("className").GetString();
        if (string.IsNullOrEmpty(className))
        {
            return new ToolCallResult { Success = false, Error = "ClassName is required" };
        }

        string? filePath = null;
        if (arguments.RootElement.TryGetProperty("filePath", out var filePathElement))
        {
            filePath = filePathElement.GetString();
        }

        var result = await _classStructure.GetClassStructureAsync(className, filePath);
        if (result == null)
        {
            return new ToolCallResult { Success = false, Error = $"Class '{className}' not found" };
        }

        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteAddClassProperty(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_semanticEdit == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var className = arguments.RootElement.GetProperty("className").GetString();
        var propertyName = arguments.RootElement.GetProperty("propertyName").GetString();
        var propertyType = arguments.RootElement.GetProperty("propertyType").GetString();

        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(propertyType))
        {
            return new ToolCallResult { Success = false, Error = "ClassName, propertyName, and propertyType are required" };
        }

        var accessibility = "public";
        if (arguments.RootElement.TryGetProperty("accessibility", out var accessElement))
        {
            accessibility = accessElement.GetString() ?? "public";
        }

        var hasGetter = true;
        if (arguments.RootElement.TryGetProperty("hasGetter", out var getterElement))
        {
            hasGetter = getterElement.GetBoolean();
        }

        var hasSetter = true;
        if (arguments.RootElement.TryGetProperty("hasSetter", out var setterElement))
        {
            hasSetter = setterElement.GetBoolean();
        }

        var result = await _semanticEdit.AddPropertyAsync(className, propertyName, propertyType, accessibility, hasGetter, hasSetter);
        return new ToolCallResult
        {
            Success = result.Success,
            Result = result.Success ? (object)result : null,
            Error = result.Error
        };
    }

    private async Task<ToolCallResult> ExecuteAddClassMethod(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_semanticEdit == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var className = arguments.RootElement.GetProperty("className").GetString();
        var methodName = arguments.RootElement.GetProperty("methodName").GetString();
        var returnType = arguments.RootElement.GetProperty("returnType").GetString();

        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(methodName) || string.IsNullOrEmpty(returnType))
        {
            return new ToolCallResult { Success = false, Error = "ClassName, methodName, and returnType are required" };
        }

        var accessibility = "public";
        if (arguments.RootElement.TryGetProperty("accessibility", out var accessElement))
        {
            accessibility = accessElement.GetString() ?? "public";
        }

        string? body = null;
        if (arguments.RootElement.TryGetProperty("body", out var bodyElement))
        {
            body = bodyElement.GetString();
        }

        List<ParameterStructure>? parameters = null;
        if (arguments.RootElement.TryGetProperty("parameters", out var paramsElement))
        {
            parameters = new List<ParameterStructure>();
            foreach (var param in paramsElement.EnumerateArray())
            {
                parameters.Add(new ParameterStructure
                {
                    Name = param.GetProperty("name").GetString() ?? "",
                    Type = param.GetProperty("type").GetString() ?? ""
                });
            }
        }

        var result = await _semanticEdit.AddMethodAsync(className, methodName, returnType, parameters, accessibility, body);
        return new ToolCallResult
        {
            Success = result.Success,
            Result = result.Success ? (object)result : null,
            Error = result.Error
        };
    }

    private async Task<ToolCallResult> ExecuteFindReferences(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_referenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        string? symbolName = null;
        if (arguments.RootElement.TryGetProperty("symbolName", out var symbolElement))
        {
            symbolName = symbolElement.GetString();
        }

        string? filePath = null;
        int? line = null;
        int? column = null;

        if (arguments.RootElement.TryGetProperty("filePath", out var filePathElement))
        {
            filePath = filePathElement.GetString();
        }
        if (arguments.RootElement.TryGetProperty("line", out var lineElement))
        {
            line = lineElement.GetInt32();
        }
        if (arguments.RootElement.TryGetProperty("column", out var columnElement))
        {
            column = columnElement.GetInt32();
        }

        if (symbolName == null && (filePath == null || line == null || column == null))
        {
            return new ToolCallResult { Success = false, Error = "Either symbolName or (filePath, line, column) is required" };
        }

        var result = await _referenceFinder.FindReferencesAsync(symbolName, filePath, line, column);
        if (result == null)
        {
            return new ToolCallResult { Success = false, Error = "Symbol not found" };
        }

        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteFindImplementations(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_referenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var symbolName = arguments.RootElement.GetProperty("symbolName").GetString();
        if (string.IsNullOrEmpty(symbolName))
        {
            return new ToolCallResult { Success = false, Error = "SymbolName is required" };
        }

        var results = await _referenceFinder.FindImplementationsAsync(symbolName);
        return new ToolCallResult
        {
            Success = true,
            Result = new { Implementations = results, TotalImplementations = results.Count }
        };
    }

    private async Task<ToolCallResult> ExecuteParseProject(JsonDocument arguments)
    {
        // ProjectParser doesn't need workspace initialization
        _projectParser ??= new ProjectParserService();

        var projectPath = arguments.RootElement.GetProperty("projectPath").GetString();
        if (string.IsNullOrEmpty(projectPath))
        {
            return new ToolCallResult { Success = false, Error = "ProjectPath is required" };
        }

        var result = await _projectParser.ParseProjectAsync(projectPath);
        if (result == null)
        {
            return new ToolCallResult { Success = false, Error = $"Failed to parse project at '{projectPath}'" };
        }

        return new ToolCallResult { Success = true, Result = result };
    }

    // ===== Phase 2 Tool Execution Methods =====

    private async Task<ToolCallResult> ExecuteGetWorkflows(JsonDocument arguments)
    {
        if (_workflowAnalyzer == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "This tool requires Phase 2 features. WorkflowAnalyzerService is not available."
            };
        }

        var projectRoot = arguments.RootElement.GetProperty("projectRoot").GetString();
        if (string.IsNullOrEmpty(projectRoot))
        {
            return new ToolCallResult { Success = false, Error = "ProjectRoot is required" };
        }

        try
        {
            var workflows = await _workflowAnalyzer.GetAllWorkflowsAsync(projectRoot);
            return new ToolCallResult
            {
                Success = true,
                Result = new { Workflows = workflows, TotalWorkflows = workflows.Count }
            };
        }
        catch (NotImplementedException ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteParseWorkflow(JsonDocument arguments)
    {
        if (_workflowAnalyzer == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "This tool requires Phase 2 features. WorkflowAnalyzerService is not available."
            };
        }

        var workflowPath = arguments.RootElement.GetProperty("workflowPath").GetString();
        if (string.IsNullOrEmpty(workflowPath))
        {
            return new ToolCallResult { Success = false, Error = "WorkflowPath is required" };
        }

        try
        {
            var workflowDetails = await _workflowAnalyzer.ParseWorkflowAsync(workflowPath);
            return new ToolCallResult { Success = true, Result = workflowDetails };
        }
        catch (NotImplementedException ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteValidateWorkflowConsistency(JsonDocument arguments)
    {
        if (_workflowAnalyzer == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "This tool requires Phase 2 features. WorkflowAnalyzerService is not available."
            };
        }

        var workflowPath = arguments.RootElement.GetProperty("workflowPath").GetString();
        var projectPath = arguments.RootElement.GetProperty("projectPath").GetString();

        if (string.IsNullOrEmpty(workflowPath) || string.IsNullOrEmpty(projectPath))
        {
            return new ToolCallResult { Success = false, Error = "WorkflowPath and projectPath are required" };
        }

        try
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workflow validation is not yet fully implemented"
            };
        }
        catch (NotImplementedException ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteGetConfigSchema(JsonDocument arguments)
    {
        if (_configAnalyzer == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "This tool requires Phase 2 features. ConfigAnalyzerService is not available."
            };
        }

        var configPath = arguments.RootElement.GetProperty("configPath").GetString();
        if (string.IsNullOrEmpty(configPath))
        {
            return new ToolCallResult { Success = false, Error = "ConfigPath is required" };
        }

        try
        {
            var schema = await _configAnalyzer.GetConfigSchemaAsync(configPath);
            return new ToolCallResult { Success = true, Result = schema };
        }
        catch (NotImplementedException ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteMergeConfigs(JsonDocument arguments)
    {
        if (_configAnalyzer == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "This tool requires Phase 2 features. ConfigAnalyzerService is not available."
            };
        }

        var configPathsElement = arguments.RootElement.GetProperty("configPaths");
        var configPaths = new List<string>();

        foreach (var pathElement in configPathsElement.EnumerateArray())
        {
            var path = pathElement.GetString();
            if (!string.IsNullOrEmpty(path))
            {
                configPaths.Add(path);
            }
        }

        if (configPaths.Count == 0)
        {
            return new ToolCallResult { Success = false, Error = "At least one config path is required" };
        }

        try
        {
            var mergedConfig = await _configAnalyzer.MergeConfigsAsync(configPaths.ToArray());
            return new ToolCallResult { Success = true, Result = mergedConfig };
        }
        catch (NotImplementedException ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeImpact(JsonDocument arguments)
    {
        if (_impactAnalyzer == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "This tool requires Phase 2 features. ImpactAnalyzerService is not available."
            };
        }

        var filePath = arguments.RootElement.GetProperty("filePath").GetString();
        var changeType = arguments.RootElement.GetProperty("changeType").GetString();

        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(changeType))
        {
            return new ToolCallResult { Success = false, Error = "FilePath and changeType are required" };
        }

        string? symbolName = null;
        if (arguments.RootElement.TryGetProperty("symbolName", out var symbolElement))
        {
            symbolName = symbolElement.GetString();
        }

        try
        {
            var change = new CodeChange
            {
                FilePath = filePath,
                ChangeType = changeType,
                SymbolName = symbolName ?? ""  // CodeChange requires SymbolName, use empty string if not provided
            };

            var impact = await _impactAnalyzer.AnalyzeImpactAsync(change);
            return new ToolCallResult { Success = true, Result = impact };
        }
        catch (NotImplementedException ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteTraceFeature(JsonDocument arguments)
    {
        if (_featureTracer == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "This tool requires Phase 2 features. FeatureTracerService is not available."
            };
        }

        string? featureName = null;
        string? entryPoint = null;

        if (arguments.RootElement.TryGetProperty("featureName", out var featureElement))
        {
            featureName = featureElement.GetString();
        }

        if (arguments.RootElement.TryGetProperty("entryPoint", out var entryElement))
        {
            entryPoint = entryElement.GetString();
        }

        if (string.IsNullOrEmpty(featureName) && string.IsNullOrEmpty(entryPoint))
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Either featureName or entryPoint is required"
            };
        }

        try
        {
            FeatureMap featureMap;
            if (!string.IsNullOrEmpty(featureName))
            {
                featureMap = await _featureTracer.TraceFeatureAsync(featureName);
            }
            else
            {
                featureMap = await _featureTracer.DiscoverFeatureComponentsAsync(entryPoint!);
            }

            return new ToolCallResult { Success = true, Result = featureMap };
        }
        catch (NotImplementedException ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }
}
