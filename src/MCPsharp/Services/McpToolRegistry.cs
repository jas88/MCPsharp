using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Roslyn;
using MCPsharp.Models.Streaming;
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

    // Advanced reverse search services
    private ICallerAnalysisService? _callerAnalysis;
    private ICallChainService? _callChain;
    private ITypeUsageService? _typeUsage;
    private AdvancedReferenceFinderService? _advancedReferenceFinder;

    // Phase 2 services (optional)
    private readonly IWorkflowAnalyzerService? _workflowAnalyzer;
    private readonly IConfigAnalyzerService? _configAnalyzer;
    private readonly IImpactAnalyzerService? _impactAnalyzer;
    private readonly IFeatureTracerService? _featureTracer;

    // Bulk edit service
    private readonly IBulkEditService? _bulkEditService;

    // Streaming processor services
    private readonly IStreamingFileProcessor? _streamingProcessor;
    private readonly IProgressTracker? _progressTracker;
    private readonly ITempFileManager? _tempFileManager;

    public McpToolRegistry(
        ProjectContextManager projectContext,
        RoslynWorkspace? workspace = null,
        IWorkflowAnalyzerService? workflowAnalyzer = null,
        IConfigAnalyzerService? configAnalyzer = null,
        IImpactAnalyzerService? impactAnalyzer = null,
        IFeatureTracerService? featureTracer = null,
        IBulkEditService? bulkEditService = null,
        IStreamingFileProcessor? streamingProcessor = null,
        IProgressTracker? progressTracker = null,
        ITempFileManager? tempFileManager = null)
    {
        _projectContext = projectContext;
        _workspace = workspace;
        _workflowAnalyzer = workflowAnalyzer;
        _configAnalyzer = configAnalyzer;
        _impactAnalyzer = impactAnalyzer;
        _featureTracer = featureTracer;
        _bulkEditService = bulkEditService;
        _streamingProcessor = streamingProcessor;
        _progressTracker = progressTracker;
        _tempFileManager = tempFileManager;
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
                // Advanced reverse search tools
                "find_callers" => await ExecuteFindCallers(request.Arguments),
                "find_call_chains" => await ExecuteFindCallChains(request.Arguments),
                "find_type_usages" => await ExecuteFindTypeUsages(request.Arguments),
                "analyze_call_patterns" => await ExecuteAnalyzeCallPatterns(request.Arguments),
                "analyze_inheritance" => await ExecuteAnalyzeInheritance(request.Arguments),
                "find_circular_dependencies" => await ExecuteFindCircularDependencies(request.Arguments),
                "find_unused_methods" => await ExecuteFindUnusedMethods(request.Arguments),
                "analyze_call_graph" => await ExecuteAnalyzeCallGraph(request.Arguments),
                "find_recursive_calls" => await ExecuteFindRecursiveCalls(request.Arguments),
                "analyze_type_dependencies" => await ExecuteAnalyzeTypeDependencies(request.Arguments),
                // Phase 2 tools
                "get_workflows" => await ExecuteGetWorkflows(request.Arguments),
                "parse_workflow" => await ExecuteParseWorkflow(request.Arguments),
                "validate_workflow_consistency" => await ExecuteValidateWorkflowConsistency(request.Arguments),
                "get_config_schema" => await ExecuteGetConfigSchema(request.Arguments),
                "merge_configs" => await ExecuteMergeConfigs(request.Arguments),
                "analyze_impact" => await ExecuteAnalyzeImpact(request.Arguments),
                "trace_feature" => await ExecuteTraceFeature(request.Arguments),
                // Bulk edit tools
                "bulk_replace" => await ExecuteBulkReplace(request.Arguments, ct),
                "conditional_edit" => await ExecuteConditionalEdit(request.Arguments, ct),
                "batch_refactor" => await ExecuteBatchRefactor(request.Arguments, ct),
                "multi_file_edit" => await ExecuteMultiFileEdit(request.Arguments, ct),
                "preview_bulk_changes" => await ExecutePreviewBulkChanges(request.Arguments, ct),
                "rollback_bulk_edit" => await ExecuteRollbackBulkEdit(request.Arguments, ct),
                "validate_bulk_edit" => await ExecuteValidateBulkEdit(request.Arguments, ct),
                "get_available_rollbacks" => await ExecuteGetAvailableRollbacks(request.Arguments, ct),
                "estimate_bulk_impact" => await ExecuteEstimateBulkImpact(request.Arguments, ct),
                "get_bulk_file_statistics" => await ExecuteGetBulkFileStatistics(request.Arguments, ct),
                // Streaming processor tools
                "stream_process_file" => await ExecuteStreamProcessFile(request.Arguments, ct),
                "bulk_transform" => await ExecuteBulkTransform(request.Arguments, ct),
                "get_stream_progress" => await ExecuteGetStreamProgress(request.Arguments),
                "cancel_stream_operation" => await ExecuteCancelStreamOperation(request.Arguments),
                "resume_stream_operation" => await ExecuteResumeStreamOperation(request.Arguments, ct),
                "list_stream_operations" => await ExecuteListStreamOperations(request.Arguments),
                "cleanup_stream_operations" => await ExecuteCleanupStreamOperations(request.Arguments),
                "get_available_processors" => await ExecuteGetAvailableProcessors(request.Arguments),
                "estimate_stream_processing" => await ExecuteEstimateStreamProcessing(request.Arguments),
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
            // ===== Advanced Reverse Search Tools =====
            new McpTool
            {
                Name = "find_callers",
                Description = "Find all callers of a specific method (who calls this method)",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "methodName",
                        Type = "string",
                        Description = "Name of the method to analyze",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "containingType",
                        Type = "string",
                        Description = "Optional containing type to disambiguate overloaded methods",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "includeIndirect",
                        Type = "boolean",
                        Description = "Include indirect calls through interfaces/base classes",
                        Required = false,
                        Default = true
                    }
                )
            },
            new McpTool
            {
                Name = "find_call_chains",
                Description = "Find call chains leading to or from a specific method",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "methodName",
                        Type = "string",
                        Description = "Name of the method to analyze",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "containingType",
                        Type = "string",
                        Description = "Optional containing type to disambiguate methods",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "direction",
                        Type = "string",
                        Description = "Direction of analysis (backward=who calls this, forward=what this calls)",
                        Required = false,
                        Default = "backward"
                    },
                    new PropertyDefinition
                    {
                        Name = "maxDepth",
                        Type = "integer",
                        Description = "Maximum depth of call chain analysis",
                        Required = false,
                        Default = 10
                    }
                )
            },
            new McpTool
            {
                Name = "find_type_usages",
                Description = "Find all usages of a specific type across the codebase",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "typeName",
                        Type = "string",
                        Description = "Name of the type to analyze",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_call_patterns",
                Description = "Analyze call patterns for a specific method",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "methodName",
                        Type = "string",
                        Description = "Name of the method to analyze",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "containingType",
                        Type = "string",
                        Description = "Optional containing type to disambiguate methods",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_inheritance",
                Description = "Analyze inheritance relationships for a specific type",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "typeName",
                        Type = "string",
                        Description = "Name of the type to analyze",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "find_circular_dependencies",
                Description = "Find circular dependencies in call chains",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "namespaceFilter",
                        Type = "string",
                        Description = "Optional namespace filter to limit analysis scope",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "find_unused_methods",
                Description = "Find methods that are never called (potential dead code)",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "namespaceFilter",
                        Type = "string",
                        Description = "Optional namespace filter to limit analysis scope",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_call_graph",
                Description = "Analyze the call graph for a specific type or namespace",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "typeName",
                        Type = "string",
                        Description = "Optional type name to limit analysis to a specific type",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "namespaceName",
                        Type = "string",
                        Description = "Optional namespace name to limit analysis scope",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "find_recursive_calls",
                Description = "Find recursive call chains for a specific method",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "methodName",
                        Type = "string",
                        Description = "Name of the method to analyze for recursion",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "containingType",
                        Type = "string",
                        Description = "Optional containing type to disambiguate methods",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "maxDepth",
                        Type = "integer",
                        Description = "Maximum depth to search for recursive calls",
                        Required = false,
                        Default = 20
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_type_dependencies",
                Description = "Analyze type dependencies for a specific type",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "typeName",
                        Type = "string",
                        Description = "Name of the type to analyze dependencies for",
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
            },
            // ===== Bulk Edit Tools =====
            new McpTool
            {
                Name = "bulk_replace",
                Description = "Perform regex search and replace across multiple files in parallel",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "files",
                        Type = "array",
                        Description = "Array of file patterns (glob patterns or absolute paths)",
                        Required = true,
                        Items = new Dictionary<string, object> { ["type"] = "string" }
                    },
                    new PropertyDefinition
                    {
                        Name = "regexPattern",
                        Type = "string",
                        Description = "Regular expression pattern to search for",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "replacement",
                        Type = "string",
                        Description = "Replacement text or pattern (can use regex groups)",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "maxParallelism",
                        Type = "integer",
                        Description = "Maximum number of files to process in parallel",
                        Required = false,
                        Default = Environment.ProcessorCount
                    },
                    new PropertyDefinition
                    {
                        Name = "createBackups",
                        Type = "boolean",
                        Description = "Whether to create backups before editing",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "previewMode",
                        Type = "boolean",
                        Description = "Preview changes without applying them",
                        Required = false,
                        Default = false
                    },
                    new PropertyDefinition
                    {
                        Name = "excludedFiles",
                        Type = "array",
                        Description = "File patterns to exclude from processing",
                        Required = false,
                        Items = new Dictionary<string, object> { ["type"] = "string" }
                    }
                )
            },
            new McpTool
            {
                Name = "conditional_edit",
                Description = "Edit files based on content conditions (e.g., file contains specific text)",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "files",
                        Type = "array",
                        Description = "Array of file patterns (glob patterns or absolute paths)",
                        Required = true,
                        Items = new Dictionary<string, object> { ["type"] = "string" }
                    },
                    new PropertyDefinition
                    {
                        Name = "conditionType",
                        Type = "string",
                        Description = "Type of condition to check (FileContains, FileMatches, FileSize, etc.)",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "pattern",
                        Type = "string",
                        Description = "Pattern to match against for the condition",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "edits",
                        Type = "array",
                        Description = "Array of edit operations to apply when condition is met",
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
                    },
                    new PropertyDefinition
                    {
                        Name = "negate",
                        Type = "boolean",
                        Description = "Whether to negate the condition",
                        Required = false,
                        Default = false
                    }
                )
            },
            new McpTool
            {
                Name = "batch_refactor",
                Description = "Perform pattern-based code refactoring across multiple files",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "files",
                        Type = "array",
                        Description = "Array of file patterns (glob patterns or absolute paths)",
                        Required = true,
                        Items = new Dictionary<string, object> { ["type"] = "string" }
                    },
                    new PropertyDefinition
                    {
                        Name = "refactorType",
                        Type = "string",
                        Description = "Type of refactoring (RenameSymbol, ChangeMethodSignature, ExtractMethod, etc.)",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "targetPattern",
                        Type = "string",
                        Description = "Target pattern to match for refactoring",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "replacementPattern",
                        Type = "string",
                        Description = "Replacement pattern for refactoring",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "multi_file_edit",
                Description = "Perform coordinated edits across multiple files with dependency management",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operations",
                        Type = "array",
                        Description = "Array of multi-file edit operations",
                        Required = true,
                        Items = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object>
                            {
                                ["filePattern"] = new Dictionary<string, object> { ["type"] = "string" },
                                ["priority"] = new Dictionary<string, object> { ["type"] = "integer" },
                                ["edits"] = new Dictionary<string, object>
                                {
                                    ["type"] = "array",
                                    ["items"] = new Dictionary<string, object> { ["type"] = "object" }
                                }
                            },
                            ["required"] = new[] { "filePattern", "edits" }
                        }
                    }
                )
            },
            new McpTool
            {
                Name = "preview_bulk_changes",
                Description = "Preview bulk edit changes without applying them, with impact analysis",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operationType",
                        Type = "string",
                        Description = "Type of bulk operation to preview",
                        Required = true,
                        Enum = new[] { "bulk_replace", "conditional_edit", "batch_refactor", "multi_file_edit" }
                    },
                    new PropertyDefinition
                    {
                        Name = "files",
                        Type = "array",
                        Description = "Array of file patterns (glob patterns or absolute paths)",
                        Required = true,
                        Items = new Dictionary<string, object> { ["type"] = "string" }
                    },
                    new PropertyDefinition
                    {
                        Name = "regexPattern",
                        Type = "string",
                        Description = "Regex pattern (for bulk_replace operations)",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "replacement",
                        Type = "string",
                        Description = "Replacement text (for bulk_replace operations)",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "conditionType",
                        Type = "string",
                        Description = "Condition type (for conditional_edit operations)",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "pattern",
                        Type = "string",
                        Description = "Condition pattern (for conditional_edit operations)",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "operations",
                        Type = "array",
                        Description = "Multi-file operations (for multi_file_edit operations)",
                        Required = false,
                        Items = new Dictionary<string, object> { ["type"] = "object" }
                    }
                )
            },
            new McpTool
            {
                Name = "rollback_bulk_edit",
                Description = "Rollback a previous bulk edit operation using created backups",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "rollbackId",
                        Type = "string",
                        Description = "ID of the rollback session to use",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "validate_bulk_edit",
                Description = "Validate a bulk edit request before execution to identify potential issues",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operationType",
                        Type = "string",
                        Description = "Type of bulk operation to validate",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "files",
                        Type = "array",
                        Description = "Array of file patterns (glob patterns or absolute paths)",
                        Required = true,
                        Items = new Dictionary<string, object> { ["type"] = "string" }
                    },
                    new PropertyDefinition
                    {
                        Name = "regexPattern",
                        Type = "string",
                        Description = "Regex pattern to validate (for bulk_replace operations)",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "conditionType",
                        Type = "string",
                        Description = "Condition type to validate (for conditional_edit operations)",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "get_available_rollbacks",
                Description = "Get list of available rollback sessions that can be used to undo changes",
                InputSchema = JsonSchemaHelper.CreateSchema()
            },
            new McpTool
            {
                Name = "estimate_bulk_impact",
                Description = "Estimate the impact and complexity of a bulk edit operation before execution",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operationType",
                        Type = "string",
                        Description = "Type of bulk operation to analyze",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "files",
                        Type = "array",
                        Description = "Array of file patterns (glob patterns or absolute paths)",
                        Required = true,
                        Items = new Dictionary<string, object> { ["type"] = "string" }
                    }
                )
            },
            new McpTool
            {
                Name = "get_bulk_file_statistics",
                Description = "Get statistics about files for planning bulk operations (sizes, counts, types)",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "files",
                        Type = "array",
                        Description = "Array of file patterns (glob patterns or absolute paths)",
                        Required = true,
                        Items = new Dictionary<string, object> { ["type"] = "string" }
                    }
                )
            },
            // Streaming processor tools
            new McpTool
            {
                Name = "stream_process_file",
                Description = "Process a file using streaming operations with configurable chunk size and progress tracking",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "filePath",
                        Type = "string",
                        Description = "Path to the input file to process",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "outputPath",
                        Type = "string",
                        Description = "Path for the output file",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "processorType",
                        Type = "string",
                        Description = "Type of processor to use (LineProcessor, RegexProcessor, CsvProcessor, BinaryProcessor)",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "processorOptions",
                        Type = "object",
                        Description = "Processor-specific configuration options",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "chunkSize",
                        Type = "integer",
                        Description = "Size of chunks in bytes (default: 65536)",
                        Required = false,
                        Default = 65536
                    },
                    new PropertyDefinition
                    {
                        Name = "createCheckpoint",
                        Type = "boolean",
                        Description = "Whether to create checkpoints for resume capability",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "enableCompression",
                        Type = "boolean",
                        Description = "Whether to enable compression for intermediate results",
                        Required = false,
                        Default = false
                    }
                )
            },
            new McpTool
            {
                Name = "bulk_transform",
                Description = "Transform multiple files in bulk with parallel processing",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "inputFiles",
                        Type = "array",
                        Description = "Array of input files or directories",
                        Required = true,
                        Items = new Dictionary<string, object> { ["type"] = "string" }
                    },
                    new PropertyDefinition
                    {
                        Name = "outputDirectory",
                        Type = "string",
                        Description = "Directory for output files",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "processorType",
                        Type = "string",
                        Description = "Type of processor to use",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "processorOptions",
                        Type = "object",
                        Description = "Processor-specific configuration options",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "chunkSize",
                        Type = "integer",
                        Description = "Size of chunks in bytes",
                        Required = false,
                        Default = 65536
                    },
                    new PropertyDefinition
                    {
                        Name = "maxDegreeOfParallelism",
                        Type = "integer",
                        Description = "Maximum number of parallel operations",
                        Required = false,
                        Default = 4
                    },
                    new PropertyDefinition
                    {
                        Name = "preserveDirectoryStructure",
                        Type = "boolean",
                        Description = "Whether to preserve directory structure in output",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "filePattern",
                        Type = "string",
                        Description = "File pattern for directory processing (e.g., '*.csv')",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "recursive",
                        Type = "boolean",
                        Description = "Whether to process directories recursively",
                        Required = false,
                        Default = false
                    }
                )
            },
            new McpTool
            {
                Name = "get_stream_progress",
                Description = "Get progress information for a streaming operation",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operationId",
                        Type = "string",
                        Description = "ID of the streaming operation",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "cancel_stream_operation",
                Description = "Cancel a running streaming operation",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operationId",
                        Type = "string",
                        Description = "ID of the operation to cancel",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "resume_stream_operation",
                Description = "Resume a previously cancelled or failed operation from checkpoint",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operationId",
                        Type = "string",
                        Description = "ID of the operation to resume",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "list_stream_operations",
                Description = "List all active and recent streaming operations",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "maxCount",
                        Type = "integer",
                        Description = "Maximum number of operations to return",
                        Required = false,
                        Default = 50
                    }
                )
            },
            new McpTool
            {
                Name = "cleanup_stream_operations",
                Description = "Clean up temporary files and completed operations",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "olderThanHours",
                        Type = "integer",
                        Description = "Clean up operations older than this many hours (default: 24)",
                        Required = false,
                        Default = 24
                    }
                )
            },
            new McpTool
            {
                Name = "get_available_processors",
                Description = "Get list of available stream processors and their capabilities",
                InputSchema = JsonSchemaHelper.CreateSchema()
            },
            new McpTool
            {
                Name = "estimate_stream_processing",
                Description = "Estimate processing time for a file with given configuration",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "filePath",
                        Type = "string",
                        Description = "Path to the file to estimate processing for",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "processorType",
                        Type = "string",
                        Description = "Type of processor to use",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "processorOptions",
                        Type = "object",
                        Description = "Processor-specific configuration options",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "chunkSize",
                        Type = "integer",
                        Description = "Size of chunks in bytes",
                        Required = false,
                        Default = 65536
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

                // Initialize advanced reverse search services
                _callerAnalysis = new CallerAnalysisService(_workspace, _symbolQuery);
                _callChain = new CallChainService(_workspace, _symbolQuery, _callerAnalysis);
                _typeUsage = new TypeUsageService(_workspace, _symbolQuery);
                _advancedReferenceFinder = new AdvancedReferenceFinderService(_workspace, _symbolQuery, _callerAnalysis, _callChain, _typeUsage);
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

    // ===== Advanced Reverse Search Tool Execution Methods =====

    private async Task<ToolCallResult> ExecuteFindCallers(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var methodName = arguments.RootElement.GetProperty("methodName").GetString();
        if (string.IsNullOrEmpty(methodName))
        {
            return new ToolCallResult { Success = false, Error = "MethodName is required" };
        }

        string? containingType = null;
        if (arguments.RootElement.TryGetProperty("containingType", out var typeElement))
        {
            containingType = typeElement.GetString();
        }

        var includeIndirect = true;
        if (arguments.RootElement.TryGetProperty("includeIndirect", out var indirectElement))
        {
            includeIndirect = indirectElement.GetBoolean();
        }

        var result = await _advancedReferenceFinder.FindCallersAsync(methodName, containingType, includeIndirect);
        if (result == null)
        {
            return new ToolCallResult { Success = false, Error = $"No callers found for method '{methodName}'" };
        }

        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteFindCallChains(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var methodName = arguments.RootElement.GetProperty("methodName").GetString();
        if (string.IsNullOrEmpty(methodName))
        {
            return new ToolCallResult { Success = false, Error = "MethodName is required" };
        }

        string? containingType = null;
        if (arguments.RootElement.TryGetProperty("containingType", out var typeElement))
        {
            containingType = typeElement.GetString();
        }

        var direction = "backward";
        if (arguments.RootElement.TryGetProperty("direction", out var directionElement))
        {
            direction = directionElement.GetString() ?? "backward";
        }

        var maxDepth = 10;
        if (arguments.RootElement.TryGetProperty("maxDepth", out var depthElement))
        {
            maxDepth = depthElement.GetInt32();
        }

        var callDirection = direction.ToLower() switch
        {
            "forward" => CallDirection.Forward,
            "both" => CallDirection.Both,
            _ => CallDirection.Backward
        };

        var result = await _advancedReferenceFinder.FindCallChainsAsync(methodName, containingType, callDirection, maxDepth);
        if (result == null)
        {
            return new ToolCallResult { Success = false, Error = $"No call chains found for method '{methodName}'" };
        }

        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteFindTypeUsages(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var typeName = arguments.RootElement.GetProperty("typeName").GetString();
        if (string.IsNullOrEmpty(typeName))
        {
            return new ToolCallResult { Success = false, Error = "TypeName is required" };
        }

        var result = await _advancedReferenceFinder.FindTypeUsagesAsync(typeName);
        if (result == null)
        {
            return new ToolCallResult { Success = false, Error = $"No usages found for type '{typeName}'" };
        }

        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteAnalyzeCallPatterns(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var methodName = arguments.RootElement.GetProperty("methodName").GetString();
        if (string.IsNullOrEmpty(methodName))
        {
            return new ToolCallResult { Success = false, Error = "MethodName is required" };
        }

        string? containingType = null;
        if (arguments.RootElement.TryGetProperty("containingType", out var typeElement))
        {
            containingType = typeElement.GetString();
        }

        var result = await _advancedReferenceFinder.AnalyzeCallPatternsAsync(methodName, containingType);
        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteAnalyzeInheritance(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var typeName = arguments.RootElement.GetProperty("typeName").GetString();
        if (string.IsNullOrEmpty(typeName))
        {
            return new ToolCallResult { Success = false, Error = "TypeName is required" };
        }

        var result = await _advancedReferenceFinder.AnalyzeInheritanceAsync(typeName);
        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteFindCircularDependencies(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        string? namespaceFilter = null;
        if (arguments.RootElement.TryGetProperty("namespaceFilter", out var nsElement))
        {
            namespaceFilter = nsElement.GetString();
        }

        var result = await _advancedReferenceFinder.FindCircularDependenciesAsync(namespaceFilter);
        return new ToolCallResult { Success = true, Result = new { CircularDependencies = result, TotalCount = result.Count } };
    }

    private async Task<ToolCallResult> ExecuteFindUnusedMethods(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        string? namespaceFilter = null;
        if (arguments.RootElement.TryGetProperty("namespaceFilter", out var nsElement))
        {
            namespaceFilter = nsElement.GetString();
        }

        var result = await _advancedReferenceFinder.FindUnusedMethodsAsync(namespaceFilter);
        return new ToolCallResult { Success = true, Result = new { UnusedMethods = result, TotalCount = result.Count } };
    }

    private async Task<ToolCallResult> ExecuteAnalyzeCallGraph(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        string? typeName = null;
        if (arguments.RootElement.TryGetProperty("typeName", out var typeElement))
        {
            typeName = typeElement.GetString();
        }

        string? namespaceName = null;
        if (arguments.RootElement.TryGetProperty("namespaceName", out var nsElement))
        {
            namespaceName = nsElement.GetString();
        }

        var result = await _advancedReferenceFinder.AnalyzeCallGraphAsync(typeName, namespaceName);
        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteFindRecursiveCalls(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var methodName = arguments.RootElement.GetProperty("methodName").GetString();
        if (string.IsNullOrEmpty(methodName))
        {
            return new ToolCallResult { Success = false, Error = "MethodName is required" };
        }

        string? containingType = null;
        if (arguments.RootElement.TryGetProperty("containingType", out var typeElement))
        {
            containingType = typeElement.GetString();
        }

        var maxDepth = 20;
        if (arguments.RootElement.TryGetProperty("maxDepth", out var depthElement))
        {
            maxDepth = depthElement.GetInt32();
        }

        var result = await _advancedReferenceFinder.FindRecursiveCallChainsAsync(methodName, containingType, maxDepth);
        return new ToolCallResult { Success = true, Result = new { RecursiveCalls = result, TotalCount = result.Count } };
    }

    private async Task<ToolCallResult> ExecuteAnalyzeTypeDependencies(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        var typeName = arguments.RootElement.GetProperty("typeName").GetString();
        if (string.IsNullOrEmpty(typeName))
        {
            return new ToolCallResult { Success = false, Error = "TypeName is required" };
        }

        var result = await _advancedReferenceFinder.AnalyzeTypeDependenciesAsync(typeName);
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

    // ===== Bulk Edit Tool Execution Methods =====

    private async Task<ToolCallResult> ExecuteBulkReplace(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            // Parse files array
            var filesArray = arguments.RootElement.GetProperty("files");
            var files = new List<string>();
            foreach (var fileElement in filesArray.EnumerateArray())
            {
                var file = fileElement.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    files.Add(file);
                }
            }

            var regexPattern = arguments.RootElement.GetProperty("regexPattern").GetString();
            var replacement = arguments.RootElement.GetProperty("replacement").GetString();

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            if (string.IsNullOrEmpty(regexPattern))
            {
                return new ToolCallResult { Success = false, Error = "Regex pattern is required" };
            }

            if (string.IsNullOrEmpty(replacement))
            {
                return new ToolCallResult { Success = false, Error = "Replacement text is required" };
            }

            // Parse optional parameters
            var options = new BulkEditOptions();
            if (arguments.RootElement.TryGetProperty("maxParallelism", out var maxParallelElement))
            {
                options.MaxParallelism = maxParallelElement.GetInt32();
            }
            if (arguments.RootElement.TryGetProperty("createBackups", out var backupsElement))
            {
                options.CreateBackups = backupsElement.GetBoolean();
            }
            if (arguments.RootElement.TryGetProperty("previewMode", out var previewElement))
            {
                options.PreviewMode = previewElement.GetBoolean();
            }
            if (arguments.RootElement.TryGetProperty("excludedFiles", out var excludedElement))
            {
                var excludedFiles = new List<string>();
                foreach (var excludedFileElement in excludedElement.EnumerateArray())
                {
                    var excludedFile = excludedFileElement.GetString();
                    if (!string.IsNullOrEmpty(excludedFile))
                    {
                        excludedFiles.Add(excludedFile);
                    }
                }
                options.ExcludedFiles = excludedFiles;
            }

            var result = await _bulkEditService.BulkReplaceAsync(files, regexPattern, replacement, options, cancellationToken);
            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteConditionalEdit(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            // Parse files array
            var filesArray = arguments.RootElement.GetProperty("files");
            var files = new List<string>();
            foreach (var fileElement in filesArray.EnumerateArray())
            {
                var file = fileElement.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    files.Add(file);
                }
            }

            var conditionTypeStr = arguments.RootElement.GetProperty("conditionType").GetString();
            var pattern = arguments.RootElement.GetProperty("pattern").GetString();

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            if (string.IsNullOrEmpty(conditionTypeStr))
            {
                return new ToolCallResult { Success = false, Error = "Condition type is required" };
            }

            if (string.IsNullOrEmpty(pattern))
            {
                return new ToolCallResult { Success = false, Error = "Pattern is required" };
            }

            // Parse condition type
            if (!Enum.TryParse<BulkConditionType>(conditionTypeStr, true, out var conditionType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid condition type: {conditionTypeStr}" };
            }

            var condition = new BulkEditCondition
            {
                ConditionType = conditionType,
                Pattern = pattern,
                Negate = arguments.RootElement.TryGetProperty("negate", out var negateElement) && negateElement.GetBoolean()
            };

            // Parse edits
            var editsArray = arguments.RootElement.GetProperty("edits");
            var edits = new List<TextEdit>();
            foreach (var editElement in editsArray.EnumerateArray())
            {
                var editType = editElement.GetProperty("type").GetString();
                TextEdit edit = editType switch
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
                    _ => throw new ArgumentException($"Unknown edit type: {editType}")
                };
                edits.Add(edit);
            }

            var result = await _bulkEditService.ConditionalEditAsync(files, condition, edits, cancellationToken: cancellationToken);
            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteBatchRefactor(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            // Parse files array
            var filesArray = arguments.RootElement.GetProperty("files");
            var files = new List<string>();
            foreach (var fileElement in filesArray.EnumerateArray())
            {
                var file = fileElement.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    files.Add(file);
                }
            }

            var refactorTypeStr = arguments.RootElement.GetProperty("refactorType").GetString();
            var targetPattern = arguments.RootElement.GetProperty("targetPattern").GetString();
            var replacementPattern = arguments.RootElement.GetProperty("replacementPattern").GetString();

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            if (string.IsNullOrEmpty(refactorTypeStr))
            {
                return new ToolCallResult { Success = false, Error = "Refactor type is required" };
            }

            if (string.IsNullOrEmpty(targetPattern))
            {
                return new ToolCallResult { Success = false, Error = "Target pattern is required" };
            }

            if (string.IsNullOrEmpty(replacementPattern))
            {
                return new ToolCallResult { Success = false, Error = "Replacement pattern is required" };
            }

            // Parse refactor type
            if (!Enum.TryParse<BulkRefactorType>(refactorTypeStr, true, out var refactorType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid refactor type: {refactorTypeStr}" };
            }

            var refactorPattern = new BulkRefactorPattern
            {
                RefactorType = refactorType,
                TargetPattern = targetPattern,
                ReplacementPattern = replacementPattern
            };

            var result = await _bulkEditService.BatchRefactorAsync(files, refactorPattern, cancellationToken: cancellationToken);
            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteMultiFileEdit(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            // Parse operations array
            var operationsArray = arguments.RootElement.GetProperty("operations");
            var operations = new List<MultiFileEditOperation>();
            foreach (var operationElement in operationsArray.EnumerateArray())
            {
                var filePattern = operationElement.GetProperty("filePattern").GetString();
                if (string.IsNullOrEmpty(filePattern))
                {
                    return new ToolCallResult { Success = false, Error = "File pattern is required for each operation" };
                }

                // Parse edits for this operation
                var editsArray = operationElement.GetProperty("edits");
                var edits = new List<TextEdit>();
                foreach (var editElement in editsArray.EnumerateArray())
                {
                    var editType = editElement.GetProperty("type").GetString();
                    TextEdit edit = editType switch
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
                        _ => throw new ArgumentException($"Unknown edit type: {editType}")
                    };
                    edits.Add(edit);
                }

                var operation = new MultiFileEditOperation
                {
                    FilePattern = filePattern,
                    Edits = edits,
                    Priority = operationElement.TryGetProperty("priority", out var priorityElement) ? priorityElement.GetInt32() : 0
                };

                operations.Add(operation);
            }

            if (operations.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one operation is required" };
            }

            var result = await _bulkEditService.MultiFileEditAsync(operations, cancellationToken: cancellationToken);
            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecutePreviewBulkChanges(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var operationTypeStr = arguments.RootElement.GetProperty("operationType").GetString();
            if (string.IsNullOrEmpty(operationTypeStr))
            {
                return new ToolCallResult { Success = false, Error = "Operation type is required" };
            }

            if (!Enum.TryParse<BulkEditOperationType>(operationTypeStr, true, out var operationType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid operation type: {operationTypeStr}" };
            }

            // Parse files array
            var filesArray = arguments.RootElement.GetProperty("files");
            var files = new List<string>();
            foreach (var fileElement in filesArray.EnumerateArray())
            {
                var file = fileElement.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    files.Add(file);
                }
            }

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            // Build bulk edit request based on operation type
            var request = new BulkEditRequest
            {
                OperationType = operationType,
                Files = files,
                Options = new BulkEditOptions { PreviewMode = true }
            };

            // Add operation-specific parameters
            var regexPattern = arguments.RootElement.TryGetProperty("regexPattern", out var regexElement) ? regexElement.GetString() : null;
            var replacementText = arguments.RootElement.TryGetProperty("replacement", out var replacementElement) ? replacementElement.GetString() : null;
            BulkEditCondition? condition = null;

            if (arguments.RootElement.TryGetProperty("conditionType", out var conditionTypeElement))
            {
                var conditionTypeStr = conditionTypeElement.GetString();
                if (Enum.TryParse<BulkConditionType>(conditionTypeStr, true, out var conditionType) &&
                    arguments.RootElement.TryGetProperty("pattern", out var patternElement))
                {
                    condition = new BulkEditCondition
                    {
                        ConditionType = conditionType,
                        Pattern = patternElement.GetString() ?? ""
                    };
                }
            }

            request = request with
            {
                RegexPattern = regexPattern,
                ReplacementText = replacementText,
                Condition = condition
            };

            var result = await _bulkEditService.PreviewBulkChangesAsync(request, cancellationToken);
            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteRollbackBulkEdit(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var rollbackId = arguments.RootElement.GetProperty("rollbackId").GetString();
            if (string.IsNullOrEmpty(rollbackId))
            {
                return new ToolCallResult { Success = false, Error = "Rollback ID is required" };
            }

            var result = await _bulkEditService.RollbackBulkEditAsync(rollbackId, cancellationToken);
            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteValidateBulkEdit(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var operationTypeStr = arguments.RootElement.GetProperty("operationType").GetString();
            if (string.IsNullOrEmpty(operationTypeStr))
            {
                return new ToolCallResult { Success = false, Error = "Operation type is required" };
            }

            if (!Enum.TryParse<BulkEditOperationType>(operationTypeStr, true, out var operationType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid operation type: {operationTypeStr}" };
            }

            // Parse files array
            var filesArray = arguments.RootElement.GetProperty("files");
            var files = new List<string>();
            foreach (var fileElement in filesArray.EnumerateArray())
            {
                var file = fileElement.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    files.Add(file);
                }
            }

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            // Build bulk edit request for validation
            var request = new BulkEditRequest
            {
                OperationType = operationType,
                Files = files
            };

            // Add operation-specific parameters
            var regexPattern = arguments.RootElement.TryGetProperty("regexPattern", out var regexElement) ? regexElement.GetString() : null;
            request = request with { RegexPattern = regexPattern };

            var result = await _bulkEditService.ValidateBulkEditAsync(request, cancellationToken);
            return new ToolCallResult { Success = result.IsValid, Result = result };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteGetAvailableRollbacks(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var rollbacks = await _bulkEditService.GetAvailableRollbacksAsync(cancellationToken);
            return new ToolCallResult { Success = true, Result = new { Rollbacks = rollbacks, TotalCount = rollbacks.Count } };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteEstimateBulkImpact(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var operationTypeStr = arguments.RootElement.GetProperty("operationType").GetString();
            if (string.IsNullOrEmpty(operationTypeStr))
            {
                return new ToolCallResult { Success = false, Error = "Operation type is required" };
            }

            if (!Enum.TryParse<BulkEditOperationType>(operationTypeStr, true, out var operationType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid operation type: {operationTypeStr}" };
            }

            // Parse files array
            var filesArray = arguments.RootElement.GetProperty("files");
            var files = new List<string>();
            foreach (var fileElement in filesArray.EnumerateArray())
            {
                var file = fileElement.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    files.Add(file);
                }
            }

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            // Build bulk edit request for impact estimation
            var request = new BulkEditRequest
            {
                OperationType = operationType,
                Files = files
            };

            var result = await _bulkEditService.EstimateImpactAsync(request, cancellationToken);
            return new ToolCallResult { Success = true, Result = result };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteGetBulkFileStatistics(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            // Parse files array
            var filesArray = arguments.RootElement.GetProperty("files");
            var files = new List<string>();
            foreach (var fileElement in filesArray.EnumerateArray())
            {
                var file = fileElement.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    files.Add(file);
                }
            }

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            var result = await _bulkEditService.GetFileStatisticsAsync(files, cancellationToken);
            return new ToolCallResult { Success = true, Result = result };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    // Streaming processor execution methods

    private async Task<ToolCallResult> ExecuteStreamProcessFile(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_streamingProcessor == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "StreamingFileProcessor is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var filePath = arguments.RootElement.GetProperty("filePath").GetString();
            var outputPath = arguments.RootElement.GetProperty("outputPath").GetString();
            var processorTypeStr = arguments.RootElement.GetProperty("processorType").GetString();

            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(outputPath) || string.IsNullOrEmpty(processorTypeStr))
            {
                return new ToolCallResult { Success = false, Error = "filePath, outputPath, and processorType are required" };
            }

            if (!Enum.TryParse<StreamProcessorType>(processorTypeStr, true, out var processorType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid processor type: {processorTypeStr}" };
            }

            var request = new StreamProcessRequest
            {
                FilePath = filePath,
                OutputPath = outputPath,
                ProcessorType = processorType,
                ChunkSize = arguments.RootElement.TryGetProperty("chunkSize", out var chunkSizeEl) ? chunkSizeEl.GetInt64() : 65536,
                CreateCheckpoint = arguments.RootElement.TryGetProperty("createCheckpoint", out var checkpointEl) ? checkpointEl.GetBoolean() : true,
                EnableCompression = arguments.RootElement.TryGetProperty("enableCompression", out var compressionEl) ? compressionEl.GetBoolean() : false
            };

            // Parse processor options
            if (arguments.RootElement.TryGetProperty("processorOptions", out var optionsEl))
            {
                request.ProcessorOptions = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsEl.GetRawText()) ?? new Dictionary<string, object>();
            }

            var result = await _streamingProcessor.ProcessFileAsync(request, cancellationToken);

            return new ToolCallResult
            {
                Success = result.Success,
                Result = result.Success ? (object)new
                {
                    operationId = result.OperationId,
                    outputPath = result.OutputPath,
                    bytesProcessed = result.BytesProcessed,
                    chunksProcessed = result.ChunksProcessed,
                    linesProcessed = result.LinesProcessed,
                    processingTime = result.ProcessingTime.TotalSeconds,
                    processingSpeed = result.ProcessingSpeedBytesPerSecond,
                    outputFiles = result.OutputFiles,
                    temporaryFiles = result.TemporaryFiles
                } : null,
                Error = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteBulkTransform(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_streamingProcessor == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "StreamingFileProcessor is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var inputFiles = new List<string>();
            foreach (var fileEl in arguments.RootElement.GetProperty("inputFiles").EnumerateArray())
            {
                var file = fileEl.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    inputFiles.Add(file);
                }
            }

            var outputDirectory = arguments.RootElement.GetProperty("outputDirectory").GetString();
            var processorTypeStr = arguments.RootElement.GetProperty("processorType").GetString();

            if (inputFiles.Count == 0 || string.IsNullOrEmpty(outputDirectory) || string.IsNullOrEmpty(processorTypeStr))
            {
                return new ToolCallResult { Success = false, Error = "inputFiles, outputDirectory, and processorType are required" };
            }

            if (!Enum.TryParse<StreamProcessorType>(processorTypeStr, true, out var processorType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid processor type: {processorTypeStr}" };
            }

            var request = new BulkTransformRequest
            {
                InputFiles = inputFiles,
                OutputDirectory = outputDirectory,
                ProcessorType = processorType,
                ChunkSize = arguments.RootElement.TryGetProperty("chunkSize", out var chunkSizeEl) ? chunkSizeEl.GetInt64() : 65536,
                MaxDegreeOfParallelism = arguments.RootElement.TryGetProperty("maxDegreeOfParallelism", out var parallelEl) ? parallelEl.GetInt32() : 4,
                PreserveDirectoryStructure = arguments.RootElement.TryGetProperty("preserveDirectoryStructure", out var preserveEl) ? preserveEl.GetBoolean() : true,
                FilePattern = arguments.RootElement.TryGetProperty("filePattern", out var patternEl) ? patternEl.GetString() : null,
                Recursive = arguments.RootElement.TryGetProperty("recursive", out var recursiveEl) ? recursiveEl.GetBoolean() : false,
                EnableParallelProcessing = true
            };

            // Parse processor options
            if (arguments.RootElement.TryGetProperty("processorOptions", out var optionsEl))
            {
                request.ProcessorOptions = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsEl.GetRawText()) ?? new Dictionary<string, object>();
            }

            var results = await _streamingProcessor.BulkTransformAsync(request, null, cancellationToken);

            var summary = new
            {
                totalFiles = results.Count,
                successfulFiles = results.Count(r => r.Success),
                failedFiles = results.Count(r => !r.Success),
                totalBytesProcessed = results.Where(r => r.Success).Sum(r => r.BytesProcessed),
                totalChunksProcessed = results.Where(r => r.Success).Sum(r => r.ChunksProcessed),
                totalProcessingTime = TimeSpan.FromSeconds(results.Where(r => r.Success).Sum(r => r.ProcessingTime.TotalSeconds)),
                results = results.Select(r => new
                {
                    operationId = r.OperationId,
                    success = r.Success,
                    outputPath = r.OutputPath,
                    bytesProcessed = r.BytesProcessed,
                    processingTime = r.ProcessingTime.TotalSeconds,
                    error = r.ErrorMessage
                }).ToList()
            };

            return new ToolCallResult { Success = true, Result = summary };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteGetStreamProgress(JsonDocument arguments)
    {
        if (_streamingProcessor == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "StreamingFileProcessor is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var operationId = arguments.RootElement.GetProperty("operationId").GetString();
            if (string.IsNullOrEmpty(operationId))
            {
                return new ToolCallResult { Success = false, Error = "operationId is required" };
            }

            var progress = await _streamingProcessor.GetProgressAsync(operationId);
            if (progress == null)
            {
                return new ToolCallResult { Success = false, Error = $"Operation {operationId} not found" };
            }

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    operationId = operationId,
                    bytesProcessed = progress.BytesProcessed,
                    totalBytes = progress.TotalBytes,
                    chunksProcessed = progress.ChunksProcessed,
                    totalChunks = progress.TotalChunks,
                    linesProcessed = progress.LinesProcessed,
                    itemsProcessed = progress.ItemsProcessed,
                    progressPercentage = Math.Round(progress.ProgressPercentage, 2),
                    estimatedTimeRemaining = progress.EstimatedTimeRemaining.TotalSeconds,
                    processingSpeedBytesPerSecond = progress.ProcessingSpeedBytesPerSecond,
                    currentPhase = progress.CurrentPhase,
                    metadata = progress.Metadata,
                    lastUpdated = progress.LastUpdated
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteCancelStreamOperation(JsonDocument arguments)
    {
        if (_streamingProcessor == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "StreamingFileProcessor is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var operationId = arguments.RootElement.GetProperty("operationId").GetString();
            if (string.IsNullOrEmpty(operationId))
            {
                return new ToolCallResult { Success = false, Error = "operationId is required" };
            }

            var success = await _streamingProcessor.CancelOperationAsync(operationId);

            return new ToolCallResult
            {
                Success = success,
                Result = success ? (object)new { operationId, cancelled = true } : null,
                Error = success ? null : $"Failed to cancel operation {operationId} or operation not found"
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteResumeStreamOperation(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_streamingProcessor == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "StreamingFileProcessor is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var operationId = arguments.RootElement.GetProperty("operationId").GetString();
            if (string.IsNullOrEmpty(operationId))
            {
                return new ToolCallResult { Success = false, Error = "operationId is required" };
            }

            var result = await _streamingProcessor.ResumeOperationAsync(operationId, cancellationToken);

            if (result == null)
            {
                return new ToolCallResult { Success = false, Error = $"Failed to resume operation {operationId} or no checkpoint available" };
            }

            return new ToolCallResult
            {
                Success = result.Success,
                Result = result.Success ? (object)new
                {
                    operationId = result.OperationId,
                    outputPath = result.OutputPath,
                    bytesProcessed = result.BytesProcessed,
                    chunksProcessed = result.ChunksProcessed,
                    linesProcessed = result.LinesProcessed,
                    processingTime = result.ProcessingTime.TotalSeconds,
                    processingSpeed = result.ProcessingSpeedBytesPerSecond,
                    resumed = true
                } : null,
                Error = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteListStreamOperations(JsonDocument arguments)
    {
        if (_streamingProcessor == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "StreamingFileProcessor is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var maxCount = arguments.RootElement.TryGetProperty("maxCount", out var maxCountEl) ? maxCountEl.GetInt32() : 50;

            var operations = await _streamingProcessor.ListOperationsAsync(maxCount);

            var operationList = operations.Select(op => new
            {
                operationId = op.OperationId,
                name = op.Name,
                status = op.Status.ToString(),
                createdAt = op.CreatedAt,
                startedAt = op.StartedAt,
                completedAt = op.CompletedAt,
                lastCheckpointAt = op.LastCheckpointAt,
                filePath = op.Request.FilePath,
                outputPath = op.Request.OutputPath,
                processorType = op.Request.ProcessorType.ToString(),
                errorMessage = op.ErrorMessage,
                hasCheckpoint = op.LastCheckpoint != null
            }).ToList();

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    operations = operationList,
                    totalCount = operationList.Count
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteCleanupStreamOperations(JsonDocument arguments)
    {
        if (_streamingProcessor == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "StreamingFileProcessor is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var olderThanHours = arguments.RootElement.TryGetProperty("olderThanHours", out var hoursEl) ? hoursEl.GetInt32() : 24;
            var olderThan = TimeSpan.FromHours(olderThanHours);

            await _streamingProcessor.CleanupAsync(olderThan);

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    cleanedUp = true,
                    olderThanHours = olderThanHours,
                    message = $"Cleaned up operations older than {olderThanHours} hours"
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteGetAvailableProcessors(JsonDocument arguments)
    {
        if (_streamingProcessor == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "StreamingFileProcessor is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var processors = await _streamingProcessor.GetAvailableProcessorsAsync();

            var processorList = processors.Select(p => new
            {
                name = p.Name,
                processorType = p.ProcessorType.ToString(),
                enableParallelProcessing = p.EnableParallelProcessing,
                maxDegreeOfParallelism = p.MaxDegreeOfParallelism,
                description = GetProcessorDescription(p.ProcessorType)
            }).ToList();

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    processors = processorList,
                    totalCount = processorList.Count
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteEstimateStreamProcessing(JsonDocument arguments)
    {
        if (_streamingProcessor == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "StreamingFileProcessor is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var filePath = arguments.RootElement.GetProperty("filePath").GetString();
            var processorTypeStr = arguments.RootElement.GetProperty("processorType").GetString();

            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(processorTypeStr))
            {
                return new ToolCallResult { Success = false, Error = "filePath and processorType are required" };
            }

            if (!Enum.TryParse<StreamProcessorType>(processorTypeStr, true, out var processorType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid processor type: {processorTypeStr}" };
            }

            var request = new StreamProcessRequest
            {
                FilePath = filePath,
                ProcessorType = processorType,
                ChunkSize = arguments.RootElement.TryGetProperty("chunkSize", out var chunkSizeEl) ? chunkSizeEl.GetInt64() : 65536
            };

            // Parse processor options
            if (arguments.RootElement.TryGetProperty("processorOptions", out var optionsEl))
            {
                request.ProcessorOptions = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsEl.GetRawText()) ?? new Dictionary<string, object>();
            }

            var estimatedTime = await _streamingProcessor.EstimateProcessingTimeAsync(request);

            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Exists ? fileInfo.Length : 0;

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    filePath = filePath,
                    fileSizeBytes = fileSize,
                    processorType = processorTypeStr,
                    estimatedProcessingTimeSeconds = estimatedTime.TotalSeconds,
                    estimatedProcessingTimeFormatted = FormatTimeSpan(estimatedTime),
                    chunkSize = request.ChunkSize,
                    estimatedChunks = fileSize > 0 ? (fileSize + request.ChunkSize - 1) / request.ChunkSize : 0
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private static string GetProcessorDescription(StreamProcessorType processorType)
    {
        return processorType switch
        {
            StreamProcessorType.LineProcessor => "Process text files line by line with filtering and transformation capabilities",
            StreamProcessorType.RegexProcessor => "Apply regex patterns for text matching and replacement operations",
            StreamProcessorType.CsvProcessor => "Process CSV/TSV files with column selection and filtering",
            StreamProcessorType.BinaryProcessor => "Process binary files with pattern search and replacement",
            _ => "Unknown processor type"
        };
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalSeconds < 60)
            return $"{timeSpan.TotalSeconds:F1} seconds";
        if (timeSpan.TotalMinutes < 60)
            return $"{timeSpan.TotalMinutes:F1} minutes";
        if (timeSpan.TotalHours < 24)
            return $"{timeSpan.TotalHours:F1} hours";
        return $"{timeSpan.TotalDays:F1} days";
    }
}
