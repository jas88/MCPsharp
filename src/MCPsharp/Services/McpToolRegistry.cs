using System.Text;
using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Roslyn;
using MCPsharp.Models.Streaming;
using MCPsharp.Models.Architecture;
using MCPsharp.Models.Consolidated;
using MCPsharp.Services.Roslyn;
using MCPsharp.Services.Phase3;
using MCPsharp.Services.Consolidated;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

/// <summary>
/// Registry and executor for MCP tools
/// </summary>
public partial class McpToolRegistry
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

    // Phase 3 services
    private IArchitectureValidatorService? _architectureValidator;
    private IDuplicateCodeDetectorService? _duplicateCodeDetector;
    private ISqlMigrationAnalyzerService? _sqlMigrationAnalyzer;
    private ILargeFileOptimizerService? _largeFileOptimizer;

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

    // Consolidated services
    private readonly FileOperationsService? _fileOperationsService;
    private readonly UniversalFileOperations? _universalFileOps;
    private readonly UnifiedAnalysisService? _unifiedAnalysis;
    private readonly BulkOperationsHub? _bulkOperationsHub;
    private readonly StreamProcessingController? _streamController;

    // Response processing for token limiting
    private readonly ResponseProcessor _responseProcessor;

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
        ITempFileManager? tempFileManager = null,
        ISqlMigrationAnalyzerService? sqlMigrationAnalyzer = null,
        ILargeFileOptimizerService? largeFileOptimizer = null,
        FileOperationsService? fileOperationsService = null,
        UniversalFileOperations? universalFileOps = null,
        UnifiedAnalysisService? unifiedAnalysis = null,
        BulkOperationsHub? bulkOperationsHub = null,
        StreamProcessingController? streamController = null,
        ILoggerFactory? loggerFactory = null)
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
        _sqlMigrationAnalyzer = sqlMigrationAnalyzer;
        _largeFileOptimizer = largeFileOptimizer;

        // Consolidated services
        _fileOperationsService = fileOperationsService;
        _universalFileOps = universalFileOps;
        _unifiedAnalysis = unifiedAnalysis;
        _bulkOperationsHub = bulkOperationsHub;
        _streamController = streamController;

        // Initialize response processor with configuration
        var responseConfig = ResponseConfiguration.LoadFromEnvironment();
        var responseLogger = loggerFactory?.CreateLogger<ResponseProcessor>();
        _responseProcessor = new ResponseProcessor(responseConfig, responseLogger);

        _tools = RegisterTools();
    }

    /// <summary>
    /// Get all available MCP tools
    /// </summary>
    public virtual List<McpTool> GetTools() => _tools;

    /// <summary>
    /// Execute a tool by name with the provided arguments
    /// </summary>
    public virtual async Task<ToolCallResult> ExecuteTool(ToolCallRequest request, CancellationToken ct = default)
    {
        try
        {
            var result = request.Name switch
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
                // Phase 3 architecture validation tools
                "validate_architecture" => await ExecuteValidateArchitecture(request.Arguments, ct),
                "detect_layer_violations" => await ExecuteDetectLayerViolations(request.Arguments, ct),
                "get_architecture_report" => await ExecuteGetArchitectureReport(request.Arguments, ct),
                "define_custom_architecture" => await ExecuteDefineCustomArchitecture(request.Arguments, ct),
                "analyze_circular_dependencies" => await ExecuteAnalyzeCircularDependencies(request.Arguments, ct),
                "generate_architecture_diagram" => await ExecuteGenerateArchitectureDiagram(request.Arguments, ct),
                "get_architecture_recommendations" => await ExecuteGetArchitectureRecommendations(request.Arguments, ct),
                "check_type_compliance" => await ExecuteCheckTypeCompliance(request.Arguments, ct),
                "get_predefined_architectures" => await ExecuteGetPredefinedArchitectures(request.Arguments, ct),
                // Duplicate Code Detection Tools
                "detect_duplicates" => await ExecuteDetectDuplicates(request.Arguments, ct),
                "find_exact_duplicates" => await ExecuteFindExactDuplicates(request.Arguments, ct),
                "find_near_duplicates" => await ExecuteFindNearDuplicates(request.Arguments, ct),
                "analyze_duplication_metrics" => await ExecuteAnalyzeDuplicationMetrics(request.Arguments, ct),
                "get_refactoring_suggestions" => await ExecuteGetRefactoringSuggestions(request.Arguments, ct),
                "get_duplicate_hotspots" => await ExecuteGetDuplicateHotspots(request.Arguments, ct),
                "compare_code_blocks" => await ExecuteCompareCodeBlocks(request.Arguments, ct),
                "validate_refactoring" => await ExecuteValidateRefactoring(request.Arguments, ct),
                // SQL Migration Analyzer Tools (Phase 3 - Coming Soon)
                "analyze_migrations" => await ExecutePhase3Placeholder(request.Arguments, ct, "analyze_migrations"),
                "detect_breaking_changes" => await ExecutePhase3Placeholder(request.Arguments, ct, "detect_breaking_changes"),
                "get_migration_history" => await ExecutePhase3Placeholder(request.Arguments, ct, "get_migration_history"),
                "get_migration_dependencies" => await ExecutePhase3Placeholder(request.Arguments, ct, "get_migration_dependencies"),
                "generate_migration_report" => await ExecutePhase3Placeholder(request.Arguments, ct, "generate_migration_report"),
                "validate_migrations" => await ExecutePhase3Placeholder(request.Arguments, ct, "validate_migrations"),
                // Large File Optimization Tools (Phase 3 - Coming Soon)
                "analyze_large_files" => await ExecutePhase3Placeholder(request.Arguments, ct, "analyze_large_files"),
                "optimize_large_class" => await ExecutePhase3Placeholder(request.Arguments, ct, "optimize_large_class"),
                "optimize_large_method" => await ExecutePhase3Placeholder(request.Arguments, ct, "optimize_large_method"),
                "get_complexity_metrics" => await ExecutePhase3Placeholder(request.Arguments, ct, "get_complexity_metrics"),
                "generate_optimization_plan" => await ExecutePhase3Placeholder(request.Arguments, ct, "generate_optimization_plan"),
                "detect_god_classes" => await ExecutePhase3Placeholder(request.Arguments, ct, "detect_god_classes"),
                "detect_god_methods" => await ExecutePhase3Placeholder(request.Arguments, ct, "detect_god_methods"),
                "analyze_code_smells" => await ExecutePhase3Placeholder(request.Arguments, ct, "analyze_code_smells"),
                "get_optimization_recommendations" => await ExecutePhase3Placeholder(request.Arguments, ct, "get_optimization_recommendations"),

            // Consolidated Service Tool Routes
            // Universal File Operations Tools
            "get_file_info" => await ExecuteGetFileInfo(request.Arguments, ct),
            "get_file_content" => await ExecuteGetFileContent(request.Arguments, ct),
            "execute_file_operation" => await ExecuteFileOperation(request.Arguments, ct),
            "execute_batch" => await ExecuteBatch(request.Arguments, ct),

            // Unified Analysis Service Tools
            "analyze_symbol" => await ExecuteAnalyzeSymbol(request.Arguments, ct),
            "analyze_type" => await ExecuteAnalyzeType(request.Arguments, ct),
            "analyze_file" => await ExecuteAnalyzeFile(request.Arguments, ct),
            "analyze_project" => await ExecuteAnalyzeProject(request.Arguments, ct),
            "analyze_architecture" => await ExecuteAnalyzeArchitecture(request.Arguments, ct),
            "analyze_dependencies" => await ExecuteAnalyzeDependenciesUnified(request.Arguments, ct),
            "analyze_quality" => await ExecuteAnalyzeQuality(request.Arguments, ct),

            // Bulk Operations Hub Tools
            "execute_bulk_operation" => await ExecuteBulkOperation(request.Arguments, ct),
            "preview_bulk_operation" => await ExecutePreviewBulkOperation(request.Arguments, ct),
            "get_bulk_progress" => await ExecuteGetBulkProgress(request.Arguments, ct),
            "manage_bulk_operation" => await ExecuteManageBulkOperation(request.Arguments, ct),

            // Stream Processing Controller Tools
            "process_stream" => await ExecuteProcessStream(request.Arguments, ct),
            "monitor_stream" => await ExecuteMonitorStream(request.Arguments, ct),
            "manage_stream" => await ExecuteManageStream(request.Arguments, ct),

            _ => new ToolCallResult
                {
                    Success = false,
                    Error = $"Unknown tool: {request.Name}"
                }
            };

            // Process successful responses to respect token limits
            if (result.Success && result.Result != null)
            {
                var processedResponse = _responseProcessor.ProcessResponse(result.Result, request.Name);

                // Create enhanced result with metadata if needed
                if (processedResponse.WasTruncated || processedResponse.Warning != null)
                {
                    var metadata = new Dictionary<string, object>
                    {
                        ["processed"] = true,
                        ["toolName"] = request.Name
                    };

                    if (processedResponse.WasTruncated)
                    {
                        metadata["truncated"] = true;
                        metadata["originalTokens"] = processedResponse.OriginalTokenCount;
                        metadata["processedTokens"] = processedResponse.EstimatedTokenCount;
                    }

                    if (processedResponse.Warning != null)
                    {
                        metadata["warning"] = processedResponse.Warning;
                    }

                    // Add any additional metadata from the processor
                    foreach (var kvp in processedResponse.Metadata)
                    {
                        metadata[kvp.Key] = kvp.Value;
                    }

                    result = new ToolCallResult
                    {
                        Success = true,
                        Result = new
                        {
                            content = processedResponse.Content,
                            metadata = metadata.Count > 1 ? metadata : null // Only include metadata if we have something to show
                        }
                    };
                }
                else
                {
                    // Response wasn't truncated, just return the processed content
                    result = new ToolCallResult
                    {
                        Success = true,
                        Result = processedResponse.Content
                    };
                }
            }

            return result;
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
            },
            // ===== Phase 3 Architecture Validation Tools =====
            new McpTool
            {
                Name = "validate_architecture",
                Description = "Validate project architecture against defined rules",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "definition",
                        Type = "object",
                        Description = "Optional custom architecture definition",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "detect_layer_violations",
                Description = "Find all architectural layer violations in a project",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "definition",
                        Type = "object",
                        Description = "Optional custom architecture definition",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_dependencies",
                Description = "Analyze dependency graph and relationships between layers",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "definition",
                        Type = "object",
                        Description = "Optional custom architecture definition",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "get_architecture_report",
                Description = "Generate comprehensive architecture compliance report",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "definition",
                        Type = "object",
                        Description = "Optional custom architecture definition",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "define_custom_architecture",
                Description = "Define or update custom architecture definition",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "definition",
                        Type = "object",
                        Description = "Architecture definition with layers and rules",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_circular_dependencies",
                Description = "Analyze circular dependencies between layers and components",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "definition",
                        Type = "object",
                        Description = "Optional custom architecture definition",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "generate_architecture_diagram",
                Description = "Generate architecture diagram data for visualization",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "definition",
                        Type = "object",
                        Description = "Optional custom architecture definition",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "get_architecture_recommendations",
                Description = "Get recommended fixes for architectural violations",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "violations",
                        Type = "array",
                        Description = "List of violations to get recommendations for",
                        Required = true,
                        Items = new Dictionary<string, object> { ["type"] = "object" }
                    }
                )
            },
            new McpTool
            {
                Name = "check_type_compliance",
                Description = "Check if a specific type follows architectural rules",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "typeName",
                        Type = "string",
                        Description = "Name of the type to check",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "definition",
                        Type = "object",
                        Description = "Architecture definition to check against",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "get_predefined_architectures",
                Description = "Get predefined architecture templates (Clean Architecture, Onion, N-Tier, Hexagonal)",
                InputSchema = JsonSchemaHelper.CreateSchema()
            },
            // ===== Phase 3 Duplicate Code Detection Tools =====
            new McpTool
            {
                Name = "detect_duplicates",
                Description = "Detect duplicate code blocks in the project with comprehensive analysis",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "options",
                        Type = "object",
                        Description = "Options for duplicate detection",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "find_exact_duplicates",
                Description = "Find exact duplicate code blocks (100% identical)",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "options",
                        Type = "object",
                        Description = "Options for detection",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "find_near_duplicates",
                Description = "Find near-miss duplicate code blocks with configurable similarity threshold",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "similarityThreshold",
                        Type = "number",
                        Description = "Similarity threshold (0.0-1.0)",
                        Required = false,
                        Default = 0.8
                    },
                    new PropertyDefinition
                    {
                        Name = "options",
                        Type = "object",
                        Description = "Options for detection",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_duplication_metrics",
                Description = "Analyze duplication metrics and statistics for the project",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "options",
                        Type = "object",
                        Description = "Options for analysis",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "get_refactoring_suggestions",
                Description = "Get refactoring suggestions for detected duplicate code",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "duplicates",
                        Type = "array",
                        Description = "List of duplicate groups to analyze",
                        Required = false,
                        Items = new Dictionary<string, object> { ["type"] = "object" }
                    },
                    new PropertyDefinition
                    {
                        Name = "options",
                        Type = "object",
                        Description = "Options for refactoring suggestions",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "get_duplicate_hotspots",
                Description = "Get a summary of duplicate code hotspots in the project",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "options",
                        Type = "object",
                        Description = "Options for analysis",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "compare_code_blocks",
                Description = "Compare two specific code blocks for similarity",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "codeBlock1",
                        Type = "object",
                        Description = "First code block to compare",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "codeBlock2",
                        Type = "object",
                        Description = "Second code block to compare",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "options",
                        Type = "object",
                        Description = "Options for comparison",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "validate_refactoring",
                Description = "Validate potential refactoring by checking if it would break dependencies",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Path to the project directory",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "suggestion",
                        Type = "object",
                        Description = "Refactoring suggestion to validate",
                        Required = true
                    }
                )
            },

            // ===== Consolidated Service Tools =====
            // Universal File Operations Tools
            new McpTool
            {
                Name = "get_file_info",
                Description = "Get comprehensive file/directory information",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "path",
                        Type = "string",
                        Description = "File or directory path",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "includeMetadata",
                        Type = "boolean",
                        Description = "Include detailed metadata",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "get_file_content",
                Description = "Get file content with advanced options",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "path",
                        Type = "string",
                        Description = "File path",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "encoding",
                        Type = "string",
                        Description = "Text encoding",
                        Required = false,
                        Default = "utf-8"
                    },
                    new PropertyDefinition
                    {
                        Name = "lineRange",
                        Type = "object",
                        Description = "Optional line range (start, end)",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "execute_file_operation",
                Description = "Execute file operations (copy, move, delete, create)",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operation",
                        Type = "string",
                        Description = "Operation type",
                        Required = true,
                        Enum = new[] { "copy", "move", "delete", "create_directory", "create_file" }
                    },
                    new PropertyDefinition
                    {
                        Name = "sourcePath",
                        Type = "string",
                        Description = "Source path",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "targetPath",
                        Type = "string",
                        Description = "Target path",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "content",
                        Type = "string",
                        Description = "Content for file creation",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "overwrite",
                        Type = "boolean",
                        Description = "Overwrite existing files",
                        Required = false,
                        Default = false
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "execute_batch",
                Description = "Execute batch file operations",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operations",
                        Type = "array",
                        Description = "Array of file operations",
                        Required = true,
                        Items = new Dictionary<string, object> { ["type"] = "object" }
                    },
                    new PropertyDefinition
                    {
                        Name = "continueOnError",
                        Type = "boolean",
                        Description = "Continue processing on errors",
                        Required = false,
                        Default = false
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },

            // Unified Analysis Service Tools
            new McpTool
            {
                Name = "analyze_symbol",
                Description = "Comprehensive symbol analysis",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "symbolName",
                        Type = "string",
                        Description = "Symbol name",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "filePath",
                        Type = "string",
                        Description = "File path for context",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "line",
                        Type = "number",
                        Description = "Line number for context",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "column",
                        Type = "number",
                        Description = "Column number for context",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "includeReferences",
                        Type = "boolean",
                        Description = "Include symbol references",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_type",
                Description = "Comprehensive type analysis",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "typeName",
                        Type = "string",
                        Description = "Type name",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "includeHierarchy",
                        Type = "boolean",
                        Description = "Include type hierarchy",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "includeMembers",
                        Type = "boolean",
                        Description = "Include type members",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_file",
                Description = "Comprehensive file analysis",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "filePath",
                        Type = "string",
                        Description = "File path",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "includeStructure",
                        Type = "boolean",
                        Description = "Include file structure",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "includeSymbols",
                        Type = "boolean",
                        Description = "Include symbol analysis",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_project",
                Description = "Comprehensive project analysis",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Project path (optional, uses current project if not specified)",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "includeDependencies",
                        Type = "boolean",
                        Description = "Include dependency analysis",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "includeMetrics",
                        Type = "boolean",
                        Description = "Include project metrics",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_architecture",
                Description = "Architecture analysis and validation",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Project path (optional, uses current project if not specified)",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "includeViolations",
                        Type = "boolean",
                        Description = "Include architecture violations",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "includeRecommendations",
                        Type = "boolean",
                        Description = "Include improvement recommendations",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_dependencies",
                Description = "Comprehensive dependency analysis",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "typeName",
                        Type = "string",
                        Description = "Type name for dependency analysis",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Project path for analysis",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "includeCircular",
                        Type = "boolean",
                        Description = "Include circular dependency analysis",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "analyze_quality",
                Description = "Code quality analysis",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "filePath",
                        Type = "string",
                        Description = "File path for quality analysis",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "projectPath",
                        Type = "string",
                        Description = "Project path for quality analysis",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "includeMetrics",
                        Type = "boolean",
                        Description = "Include quality metrics",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },

            // Bulk Operations Hub Tools
            new McpTool
            {
                Name = "execute_bulk_operation",
                Description = "Execute bulk operations with advanced features",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operationType",
                        Type = "string",
                        Description = "Operation type",
                        Required = true,
                        Enum = new[] { "bulk_replace", "conditional_edit", "batch_refactor", "multi_file_edit" }
                    },
                    new PropertyDefinition
                    {
                        Name = "files",
                        Type = "array",
                        Description = "Files to process",
                        Required = true,
                        Items = new Dictionary<string, object> { ["type"] = "string" }
                    },
                    new PropertyDefinition
                    {
                        Name = "operation",
                        Type = "object",
                        Description = "Operation details",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "options",
                        Type = "object",
                        Description = "Operation options",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "preview_bulk_operation",
                Description = "Preview bulk operations without executing",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operationType",
                        Type = "string",
                        Description = "Operation type",
                        Required = true,
                        Enum = new[] { "bulk_replace", "conditional_edit", "batch_refactor", "multi_file_edit" }
                    },
                    new PropertyDefinition
                    {
                        Name = "files",
                        Type = "array",
                        Description = "Files to preview",
                        Required = true,
                        Items = new Dictionary<string, object> { ["type"] = "string" }
                    },
                    new PropertyDefinition
                    {
                        Name = "operation",
                        Type = "object",
                        Description = "Operation details",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "maxPreviewFiles",
                        Type = "number",
                        Description = "Maximum files to preview",
                        Required = false,
                        Default = 10
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "get_bulk_progress",
                Description = "Get progress of bulk operations",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operationId",
                        Type = "string",
                        Description = "Operation ID",
                        Required = true
                    }
                )
            },
            new McpTool
            {
                Name = "manage_bulk_operation",
                Description = "Manage bulk operations (cancel, pause, resume)",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operationId",
                        Type = "string",
                        Description = "Operation ID",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "action",
                        Type = "string",
                        Description = "Management action",
                        Required = true,
                        Enum = new[] { "cancel", "pause", "resume", "retry" }
                    }
                )
            },

            // Stream Processing Controller Tools
            new McpTool
            {
                Name = "process_stream",
                Description = "Process large files with streaming",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "filePath",
                        Type = "string",
                        Description = "File path to process",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "processorType",
                        Type = "string",
                        Description = "Processor type",
                        Required = true,
                        Enum = new[] { "LineProcessor", "RegexProcessor", "CsvProcessor", "BinaryProcessor" }
                    },
                    new PropertyDefinition
                    {
                        Name = "outputPath",
                        Type = "string",
                        Description = "Output path",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "processorOptions",
                        Type = "object",
                        Description = "Processor-specific options",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "chunkSize",
                        Type = "number",
                        Description = "Chunk size in bytes",
                        Required = false,
                        Default = 65536
                    },
                    new PropertyDefinition
                    {
                        Name = "requestId",
                        Type = "string",
                        Description = "Optional request ID for tracking",
                        Required = false
                    }
                )
            },
            new McpTool
            {
                Name = "monitor_stream",
                Description = "Monitor stream processing progress",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operationId",
                        Type = "string",
                        Description = "Operation ID",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "includeDetails",
                        Type = "boolean",
                        Description = "Include detailed progress information",
                        Required = false,
                        Default = true
                    }
                )
            },
            new McpTool
            {
                Name = "manage_stream",
                Description = "Manage stream processing operations",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "operationId",
                        Type = "string",
                        Description = "Operation ID",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "action",
                        Type = "string",
                        Description = "Management action",
                        Required = false,
                        Enum = new[] { "cancel", "pause", "resume", "cleanup" }
                    },
                    new PropertyDefinition
                    {
                        Name = "cleanupOlderThanHours",
                        Type = "number",
                        Description = "Cleanup operations older than specified hours",
                        Required = false,
                        Default = 24
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

                // Initialize Phase 3 services
                _architectureValidator = new ArchitectureValidatorService(_workspace, _symbolQuery, _callerAnalysis, _typeUsage);
                _duplicateCodeDetector = new DuplicateCodeDetectorService(_workspace, _symbolQuery, _callerAnalysis, _typeUsage,
                    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<DuplicateCodeDetectorService>());
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

                // Initialize advanced reverse search services for project opening
                _callerAnalysis = new CallerAnalysisService(_workspace, _symbolQuery);
                _callChain = new CallChainService(_workspace, _symbolQuery, _callerAnalysis);
                _typeUsage = new TypeUsageService(_workspace, _symbolQuery);

                // Initialize Phase 3 services
                _architectureValidator = new ArchitectureValidatorService(_workspace, _symbolQuery, _callerAnalysis, _typeUsage);
                _duplicateCodeDetector = new DuplicateCodeDetectorService(_workspace, _symbolQuery, _callerAnalysis, _typeUsage,
                    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<DuplicateCodeDetectorService>());
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
                    StartLine = editElement.GetProperty("line").GetInt32(),
                    StartColumn = editElement.GetProperty("column").GetInt32(),
                    EndLine = editElement.GetProperty("line").GetInt32(),
                    EndColumn = editElement.GetProperty("column").GetInt32(),
                    NewText = editElement.GetProperty("text").GetString() ?? ""
                },
                "delete" => (TextEdit)new DeleteEdit
                {
                    StartLine = editElement.GetProperty("start_line").GetInt32(),
                    StartColumn = editElement.GetProperty("start_column").GetInt32(),
                    EndLine = editElement.GetProperty("end_line").GetInt32(),
                    EndColumn = editElement.GetProperty("end_column").GetInt32(),
                    NewText = ""
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

    // ===== Phase 3 Architecture Validation Tool Execution Methods =====

    private async Task<ToolCallResult> ExecuteValidateArchitecture(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments);
            var definitionElement = arguments.RootElement.GetPropertyOrNull("definition");
            MCPsharp.Models.Architecture.ArchitectureDefinition? definition = null;

            if (definitionElement?.ValueKind != JsonValueKind.Null)
            {
                // Parse architecture definition from JSON
                definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.Value.GetRawText());
            }

            var result = await _architectureValidator.ValidateArchitectureAsync(projectPath, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["isValid"] = result.IsValid,
                    ["compliancePercentage"] = result.CompliancePercentage,
                    ["totalTypesAnalyzed"] = result.TotalTypesAnalyzed,
                    ["compliantTypes"] = result.CompliantTypes,
                    ["violations"] = result.Violations,
                    ["warnings"] = result.Warnings,
                    ["layerStatistics"] = result.LayerStatistics,
                    ["analysisDuration"] = result.AnalysisDuration.TotalMilliseconds,
                    ["analyzedFiles"] = result.AnalyzedFiles
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error validating architecture: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteDetectLayerViolations(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments);
            var definitionElement = arguments.RootElement.GetPropertyOrNull("definition");
            MCPsharp.Models.Architecture.ArchitectureDefinition? definition = null;

            if (definitionElement?.ValueKind != JsonValueKind.Null)
            {
                definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.Value.GetRawText());
            }

            var violations = await _architectureValidator.DetectLayerViolationsAsync(projectPath, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["violations"] = violations,
                    ["totalViolations"] = violations.Count,
                    ["criticalViolations"] = violations.Count(v => v.Severity == ViolationSeverity.Critical),
                    ["majorViolations"] = violations.Count(v => v.Severity == ViolationSeverity.Major),
                    ["minorViolations"] = violations.Count(v => v.Severity == ViolationSeverity.Minor)
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error detecting layer violations: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeDependencies(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments);
            var definitionElement = arguments.RootElement.GetPropertyOrNull("definition");
            MCPsharp.Models.Architecture.ArchitectureDefinition? definition = null;

            if (definitionElement?.ValueKind != JsonValueKind.Null)
            {
                definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.Value.GetRawText());
            }

            var analysis = await _architectureValidator.AnalyzeDependenciesAsync(projectPath, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["nodes"] = analysis.Nodes,
                    ["edges"] = analysis.Edges,
                    ["circularDependencies"] = analysis.CircularDependencies,
                    ["layerDependencies"] = analysis.LayerDependencies,
                    ["dependencyMetrics"] = analysis.DependencyMetrics,
                    ["analysisDuration"] = analysis.AnalysisDuration.TotalMilliseconds
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error analyzing dependencies: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGetArchitectureReport(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments);
            var definitionElement = arguments.RootElement.GetPropertyOrNull("definition");
            MCPsharp.Models.Architecture.ArchitectureDefinition? definition = null;

            if (definitionElement?.ValueKind != JsonValueKind.Null)
            {
                definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.Value.GetRawText());
            }

            var report = await _architectureValidator.GetArchitectureReportAsync(projectPath, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["summary"] = report.Summary,
                    ["validationResult"] = report.ValidationResult,
                    ["dependencyAnalysis"] = report.DependencyAnalysis,
                    ["recommendations"] = report.Recommendations,
                    ["architectureUsed"] = report.ArchitectureUsed,
                    ["generatedAt"] = report.GeneratedAt,
                    ["projectPath"] = report.ProjectPath
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error generating architecture report: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteDefineCustomArchitecture(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var definitionElement = arguments.RootElement.GetProperty("definition");
            var definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.GetRawText());

            if (definition == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Invalid architecture definition provided"
                };
            }

            var result = await _architectureValidator.DefineCustomArchitectureAsync(definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["architecture"] = result
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error defining custom architecture: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeCircularDependencies(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments);
            var definitionElement = arguments.RootElement.GetPropertyOrNull("definition");
            MCPsharp.Models.Architecture.ArchitectureDefinition? definition = null;

            if (definitionElement?.ValueKind != JsonValueKind.Null)
            {
                definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.Value.GetRawText());
            }

            var circularDependencies = await _architectureValidator.AnalyzeCircularDependenciesAsync(projectPath, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["circularDependencies"] = circularDependencies,
                    ["totalCycles"] = circularDependencies.Count,
                    ["criticalCycles"] = circularDependencies.Count(c => c.Severity == ViolationSeverity.Critical),
                    ["majorCycles"] = circularDependencies.Count(c => c.Severity == ViolationSeverity.Major)
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error analyzing circular dependencies: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGenerateArchitectureDiagram(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments);
            var definitionElement = arguments.RootElement.GetPropertyOrNull("definition");
            MCPsharp.Models.Architecture.ArchitectureDefinition? definition = null;

            if (definitionElement?.ValueKind != JsonValueKind.Null)
            {
                definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.Value.GetRawText());
            }

            var diagram = await _architectureValidator.GenerateArchitectureDiagramAsync(projectPath, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["diagram"] = diagram
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error generating architecture diagram: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGetArchitectureRecommendations(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var violationsElement = arguments.RootElement.GetProperty("violations");
            var violations = JsonSerializer.Deserialize<List<LayerViolation>>(violationsElement.GetRawText());

            if (violations == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Invalid violations list provided"
                };
            }

            var recommendations = await _architectureValidator.GetRecommendationsAsync(violations, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["recommendations"] = recommendations,
                    ["totalRecommendations"] = recommendations.Count,
                    ["highPriorityRecommendations"] = recommendations.Count(r => r.Priority == RecommendationPriority.High || r.Priority == RecommendationPriority.Critical),
                    ["effortScore"] = recommendations.Sum(r => r.Effort),
                    ["impactScore"] = recommendations.Sum(r => r.Impact)
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error getting architecture recommendations: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteCheckTypeCompliance(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var typeName = arguments.RootElement.GetProperty("typeName").GetString();
            var definitionElement = arguments.RootElement.GetProperty("definition");
            var definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.GetRawText());

            if (string.IsNullOrEmpty(typeName) || definition == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Both typeName and definition are required"
                };
            }

            var compliance = await _architectureValidator.CheckTypeComplianceAsync(typeName, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["typeName"] = compliance.TypeName,
                    ["layer"] = compliance.Layer,
                    ["isCompliant"] = compliance.IsCompliant,
                    ["violations"] = compliance.Violations,
                    ["warnings"] = compliance.Warnings,
                    ["dependencies"] = compliance.Dependencies
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error checking type compliance: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGetPredefinedArchitectures(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var architectures = await _architectureValidator.GetPredefinedArchitecturesAsync(ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["architectures"] = architectures,
                    ["totalArchitectures"] = architectures.Count
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error getting predefined architectures: {ex.Message}"
            };
        }
    }

    // ===== Duplicate Code Detection Execution Methods =====

    private async Task<ToolCallResult> ExecuteDetectDuplicates(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var options = ParseDuplicateDetectionOptions(arguments);

            var result = await _duplicateCodeDetector.DetectDuplicatesAsync(projectPath, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["duplicateGroups"] = result.DuplicateGroups,
                    ["metrics"] = result.Metrics,
                    ["refactoringSuggestions"] = result.RefactoringSuggestions,
                    ["hotspots"] = result.Hotspots,
                    ["analysisDuration"] = result.AnalysisDuration.TotalMilliseconds,
                    ["filesAnalyzed"] = result.FilesAnalyzed,
                    ["warnings"] = result.Warnings
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error detecting duplicates: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteFindExactDuplicates(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var options = ParseDuplicateDetectionOptions(arguments);

            var result = await _duplicateCodeDetector.FindExactDuplicatesAsync(projectPath, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["duplicateGroups"] = result,
                    ["totalGroups"] = result.Count,
                    ["totalDuplicates"] = result.Sum(g => g.CodeBlocks.Count)
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error finding exact duplicates: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteFindNearDuplicates(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var similarityThreshold = GetDoubleFromArguments(arguments, "similarityThreshold", 0.8);
            var options = ParseDuplicateDetectionOptions(arguments);

            var result = await _duplicateCodeDetector.FindNearDuplicatesAsync(projectPath, similarityThreshold, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["duplicateGroups"] = result,
                    ["totalGroups"] = result.Count,
                    ["totalDuplicates"] = result.Sum(g => g.CodeBlocks.Count),
                    ["similarityThreshold"] = similarityThreshold,
                    ["averageSimilarity"] = result.Any() ? result.Average(g => g.SimilarityScore) : 0.0
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error finding near duplicates: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeDuplicationMetrics(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var options = ParseDuplicateDetectionOptions(arguments);

            var result = await _duplicateCodeDetector.AnalyzeDuplicationMetricsAsync(projectPath, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["metrics"] = result,
                    ["summary"] = new Dictionary<string, object>
                    {
                        ["totalDuplicateGroups"] = result.TotalDuplicateGroups,
                        ["exactDuplicates"] = result.ExactDuplicateGroups,
                        ["nearMissDuplicates"] = result.NearMissDuplicateGroups,
                        ["totalDuplicateLines"] = result.TotalDuplicateLines,
                        ["duplicationPercentage"] = result.DuplicationPercentage,
                        ["filesWithDuplicates"] = result.FilesWithDuplicates,
                        ["estimatedMaintenanceCost"] = result.EstimatedMaintenanceCost,
                        ["estimatedRefactoringSavings"] = result.EstimatedRefactoringSavings
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error analyzing duplication metrics: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGetRefactoringSuggestions(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var duplicatesElement = GetPropertyFromArguments(arguments, "duplicates");
            var options = ParseRefactoringOptions(arguments);

            List<MCPsharp.Models.DuplicateGroup> duplicates;

            if (duplicatesElement.HasValue && duplicatesElement.Value.ValueKind == JsonValueKind.Array)
            {
                // Parse provided duplicates
                duplicates = JsonSerializer.Deserialize<List<MCPsharp.Models.DuplicateGroup>>(duplicatesElement.Value.GetRawText()) ?? new List<MCPsharp.Models.DuplicateGroup>();
            }
            else
            {
                // Detect duplicates first
                var detectionOptions = ParseDuplicateDetectionOptions(arguments);
                var detectionResult = await _duplicateCodeDetector.DetectDuplicatesAsync(projectPath, detectionOptions, ct);
                duplicates = new List<MCPsharp.Models.DuplicateGroup>(); // Skip mapping for now since service expects different type
            }

            var result = await _duplicateCodeDetector.GetRefactoringSuggestionsAsync(projectPath, duplicates, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["suggestions"] = result,
                    ["totalSuggestions"] = result.Count,
                    ["highPrioritySuggestions"] = result.Count(s => s.Priority == RefactoringPriority.High || s.Priority == RefactoringPriority.Critical),
                    ["breakingChanges"] = result.Count(s => s.IsBreakingChange),
                    ["estimatedTotalEffort"] = result.Sum(s => s.EstimatedEffort),
                    ["estimatedTotalBenefit"] = result.Sum(s => s.EstimatedBenefit)
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error getting refactoring suggestions: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGetDuplicateHotspots(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var options = ParseDuplicateDetectionOptions(arguments);

            var result = await _duplicateCodeDetector.GetDuplicateHotspotsAsync(projectPath, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["hotspots"] = result,
                    ["summary"] = new Dictionary<string, object>
                    {
                        ["fileHotspots"] = result.FileHotspots.Count,
                        ["classHotspots"] = result.ClassHotspots.Count,
                        ["methodHotspots"] = result.MethodHotspots.Count,
                        ["directoryHotspots"] = result.DirectoryHotspots.Count,
                        ["criticalFileHotspots"] = result.FileHotspots.Count(f => f.RiskLevel == HotspotRiskLevel.Critical),
                        ["criticalMethodHotspots"] = result.MethodHotspots.Count(m => m.RiskLevel == HotspotRiskLevel.Critical)
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error getting duplicate hotspots: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteCompareCodeBlocks(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var block1Element = GetPropertyFromArguments(arguments, "codeBlock1");
            var block2Element = GetPropertyFromArguments(arguments, "codeBlock2");
            var options = ParseCodeComparisonOptions(arguments);

            if (block1Element == null || block2Element == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Both codeBlock1 and codeBlock2 are required"
                };
            }

            var block1 = ParseCodeBlockDefinition(block1Element.Value);
            var block2 = ParseCodeBlockDefinition(block2Element.Value);

            if (block1 == null || block2 == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Invalid code block definitions. Each block must include filePath, startLine, and endLine."
                };
            }

            var result = await _duplicateCodeDetector.CompareCodeBlocksAsync(block1, block2, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["similarity"] = new Dictionary<string, object>
                    {
                        ["overall"] = result.OverallSimilarity,
                        ["structural"] = result.StructuralSimilarity,
                        ["token"] = result.TokenSimilarity,
                        ["semantic"] = result.SemanticSimilarity
                    },
                    ["isDuplicate"] = result.IsDuplicate,
                    ["duplicateType"] = result.DuplicateType?.ToString() ?? "None",
                    ["differences"] = result.Differences,
                    ["commonPatterns"] = result.CommonPatterns,
                    ["summary"] = new Dictionary<string, object>
                    {
                        ["similarityPercentage"] = Math.Round(result.OverallSimilarity * 100, 2),
                        ["differenceCount"] = result.Differences.Count,
                        ["commonPatternCount"] = result.CommonPatterns.Count,
                        ["recommendation"] = result.IsDuplicate ? "Consider refactoring to eliminate duplication" : "Code blocks are sufficiently different"
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error comparing code blocks: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteValidateRefactoring(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var suggestionElement = GetPropertyFromArguments(arguments, "suggestion");

            if (suggestionElement == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Refactoring suggestion is required"
                };
            }

            var suggestion = JsonSerializer.Deserialize<RefactoringSuggestion>(suggestionElement.Value.GetRawText());
            if (suggestion == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Invalid refactoring suggestion format"
                };
            }

            var result = await _duplicateCodeDetector.ValidateRefactoringAsync(projectPath, suggestion, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["isValid"] = result.IsValid,
                    ["overallRisk"] = result.OverallRisk.ToString(),
                    ["issues"] = result.Issues,
                    ["dependencyImpacts"] = result.DependencyImpacts,
                    ["recommendations"] = result.Recommendations,
                    ["summary"] = new Dictionary<string, object>
                    {
                        ["criticalIssues"] = result.Issues.Count(i => i.Severity == MCPsharp.Models.ValidationSeverity.Critical),
                        ["errorIssues"] = result.Issues.Count(i => i.Severity == MCPsharp.Models.ValidationSeverity.Error),
                        ["warningIssues"] = result.Issues.Count(i => i.Severity == MCPsharp.Models.ValidationSeverity.Warning),
                        ["breakingChanges"] = result.DependencyImpacts.Count(d => d.IsBreakingChange),
                        ["actionRequired"] = !result.IsValid || result.Issues.Any(i => i.Severity >= MCPsharp.Models.ValidationSeverity.Error)
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error validating refactoring: {ex.Message}"
            };
        }
    }

    #region Helper Methods for Duplicate Code Detection

    private DuplicateDetectionOptions ParseDuplicateDetectionOptions(JsonDocument arguments)
    {
        var optionsElement = GetPropertyFromArguments(arguments, "options");

        var options = new DuplicateDetectionOptions();

        if (optionsElement.HasValue && optionsElement.Value.ValueKind == JsonValueKind.Object)
        {
            var optionsObj = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsElement.Value.GetRawText());

            if (optionsObj != null)
            {
                if (optionsObj.TryGetValue("minBlockSize", out var minBlockSize) && minBlockSize is int minBlock)
                    options = options with { MinBlockSize = minBlock };

                if (optionsObj.TryGetValue("maxBlockSize", out var maxBlockSize) && maxBlockSize is int maxBlock)
                    options = options with { MaxBlockSize = maxBlock };

                if (optionsObj.TryGetValue("similarityThreshold", out var similarityThreshold) && similarityThreshold is double similarity)
                    options = options with { SimilarityThreshold = similarity };

                if (optionsObj.TryGetValue("ignoreGeneratedCode", out var ignoreGenerated) && ignoreGenerated is bool ignoreGen)
                    options = options with { IgnoreGeneratedCode = ignoreGen };

                if (optionsObj.TryGetValue("ignoreTestCode", out var ignoreTest) && ignoreTest is bool ignoreTestCode)
                    options = options with { IgnoreTestCode = ignoreTestCode };

                if (optionsObj.TryGetValue("ignoreTrivialDifferences", out var ignoreTrivial) && ignoreTrivial is bool ignoreTrivialDifferences)
                    options = options with { IgnoreTrivialDifferences = ignoreTrivialDifferences };

                if (optionsObj.TryGetValue("detectionTypes", out var detectionTypes) && detectionTypes is JsonElement typesElement && typesElement.ValueKind == JsonValueKind.Array)
                {
                    var typeStrings = JsonSerializer.Deserialize<string[]>(typesElement.GetRawText());
                    if (typeStrings != null)
                    {
                        var types = DuplicateDetectionTypes.None;
                        foreach (var typeString in typeStrings)
                        {
                            if (Enum.TryParse<DuplicateDetectionTypes>(typeString, true, out var type))
                                types |= type;
                        }
                        options = options with { DetectionTypes = types };
                    }
                }
            }
        }

        return options;
    }

    private RefactoringOptions ParseRefactoringOptions(JsonDocument arguments)
    {
        var optionsElement = GetPropertyFromArguments(arguments, "options");

        var options = new RefactoringOptions();

        if (optionsElement.HasValue && optionsElement.Value.ValueKind == JsonValueKind.Object)
        {
            var optionsObj = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsElement.Value.GetRawText());

            if (optionsObj != null)
            {
                if (optionsObj.TryGetValue("maxSuggestions", out var maxSuggestions) && maxSuggestions is int maxSugg)
                    options = options with { MaxSuggestions = maxSugg };

                if (optionsObj.TryGetValue("prioritizeBreakingChanges", out var prioritizeBreaking) && prioritizeBreaking is bool prioritize)
                    options = options with { PrioritizeBreakingChanges = prioritize };

                if (optionsObj.TryGetValue("refactoringTypes", out var refactoringTypes) && refactoringTypes is JsonElement typesElement && typesElement.ValueKind == JsonValueKind.Array)
                {
                    var typeStrings = JsonSerializer.Deserialize<string[]>(typesElement.GetRawText());
                    if (typeStrings != null)
                    {
                        var types = RefactoringTypes.None;
                        foreach (var typeString in typeStrings)
                        {
                            if (Enum.TryParse<RefactoringTypes>(typeString, true, out var type))
                                types |= type;
                        }
                        options = options with { RefactoringTypes = types };
                    }
                }
            }
        }

        return options;
    }

    private CodeComparisonOptions ParseCodeComparisonOptions(JsonDocument arguments)
    {
        var optionsElement = GetPropertyFromArguments(arguments, "options");

        var options = new CodeComparisonOptions();

        if (optionsElement.HasValue && optionsElement.Value.ValueKind == JsonValueKind.Object)
        {
            var optionsObj = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsElement.Value.GetRawText());

            if (optionsObj != null)
            {
                if (optionsObj.TryGetValue("ignoreWhitespace", out var ignoreWhitespace) && ignoreWhitespace is bool ignoreWs)
                    options = options with { IgnoreWhitespace = ignoreWs };

                if (optionsObj.TryGetValue("ignoreComments", out var ignoreComments) && ignoreComments is bool ignoreComm)
                    options = options with { IgnoreComments = ignoreComm };

                if (optionsObj.TryGetValue("ignoreIdentifiers", out var ignoreIdentifiers) && ignoreIdentifiers is bool ignoreIds)
                    options = options with { IgnoreIdentifiers = ignoreIds };

                if (optionsObj.TryGetValue("ignoreStringLiterals", out var ignoreStrings) && ignoreStrings is bool ignoreStr)
                    options = options with { IgnoreStringLiterals = ignoreStr };

                if (optionsObj.TryGetValue("ignoreNumericLiterals", out var ignoreNumbers) && ignoreNumbers is bool ignoreNum)
                    options = options with { IgnoreNumericLiterals = ignoreNum };
            }
        }

        return options;
    }

    private CodeBlock? ParseCodeBlockDefinition(JsonElement blockElement)
    {
        try
        {
            var blockObj = JsonSerializer.Deserialize<Dictionary<string, object>>(blockElement.GetRawText());
            if (blockObj == null) return null;

            if (!blockObj.TryGetValue("filePath", out var filePathObj) ||
                !blockObj.TryGetValue("startLine", out var startLineObj) ||
                !blockObj.TryGetValue("endLine", out var endLineObj))
            {
                return null;
            }

            var filePath = filePathObj.ToString();
            if (!int.TryParse(startLineObj.ToString(), out var startLine) ||
                !int.TryParse(endLineObj.ToString(), out var endLine))
            {
                return null;
            }

            // Create a basic code block definition
            // In a real implementation, we would need to read the actual source code and extract details
            return new CodeBlock
            {
                FilePath = filePath,
                StartLine = startLine,
                EndLine = endLine,
                StartColumn = 1,
                EndColumn = 100, // Placeholder
                SourceCode = $"// Code from {filePath}:{startLine}-{endLine}", // Placeholder
                NormalizedCode = $"// Code from {filePath}:{startLine}-{endLine}", // Placeholder
                CodeHash = ComputeHash($"{filePath}:{startLine}:{endLine}"),
                ElementType = CodeElementType.CodeBlock,
                ElementName = $"Block_{startLine}_{endLine}",
                Accessibility = Accessibility.Private,
                IsGenerated = false,
                IsTestCode = false,
                Complexity = new ComplexityMetrics
                {
                    CyclomaticComplexity = 1,
                    CognitiveComplexity = 1,
                    LinesOfCode = endLine - startLine + 1,
                    LogicalLinesOfCode = endLine - startLine + 1,
                    ParameterCount = 0,
                    NestingDepth = 1,
                    OverallScore = 1.0
                },
                Tokens = new List<CodeToken>(),
                AstStructure = new AstStructure
                {
                    StructuralHash = ComputeHash($"structure_{filePath}_{startLine}_{endLine}"),
                    NodeTypes = new List<string> { "Block" },
                    Depth = 1,
                    NodeCount = 1,
                    StructuralComplexity = 1.0,
                    ControlFlowPatterns = new List<ControlFlowPattern>(),
                    DataFlowPatterns = new List<DataFlowPattern>()
                }
            };
        }
        catch
        {
            return null;
        }
    }

    #endregion

    private string GetProjectPathFromArguments(JsonDocument arguments)
    {
        var context = _projectContext.GetProjectContext();
        if (arguments.RootElement.TryGetProperty("projectPath", out var projectPathElement) &&
            !string.IsNullOrEmpty(projectPathElement.GetString()))
        {
            return projectPathElement.GetString()!;
        }
        return context?.RootPath ?? Environment.CurrentDirectory;
    }

    #region Additional Helper Methods for Duplicate Code Detection

    private JsonElement? GetPropertyFromArguments(JsonDocument arguments, string propertyName)
    {
        if (arguments.RootElement.TryGetProperty(propertyName, out var propertyElement))
        {
            return propertyElement;
        }
        return null;
    }

    private double GetDoubleFromArguments(JsonDocument arguments, string propertyName, double defaultValue)
    {
        if (arguments.RootElement.TryGetProperty(propertyName, out var propertyElement) &&
            propertyElement.ValueKind == JsonValueKind.Number &&
            propertyElement.TryGetDouble(out var value))
        {
            return value;
        }
        return defaultValue;
    }

    private string ComputeHash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

      #endregion

    #region Phase 3 Placeholder Tools

    private async Task<ToolCallResult> ExecutePhase3Placeholder(JsonDocument arguments, CancellationToken ct, string toolName)
    {
        await EnsureWorkspaceInitializedAsync();

        try
        {
            // Extract any relevant arguments for context
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["message"] = $"Phase 3 tool '{toolName}' is coming soon!",
                    ["status"] = "placeholder",
                    ["toolName"] = toolName,
                    ["projectPath"] = projectPath,
                    ["description"] = GetToolDescription(toolName),
                    ["capabilities"] = GetToolCapabilities(toolName)
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error in Phase 3 placeholder for '{toolName}': {ex.Message}"
            };
        }
    }

    private string GetToolDescription(string toolName)
    {
        return toolName switch
        {
            "analyze_migrations" => "Analyze Entity Framework migrations and provide insights about database schema changes.",
            "detect_breaking_changes" => "Detect potentially breaking changes in database migrations.",
            "get_migration_history" => "Get the history of applied database migrations.",
            "get_migration_dependencies" => "Analyze dependencies between migrations and detect circular dependencies.",
            "generate_migration_report" => "Generate a comprehensive report of migration status and issues.",
            "validate_migrations" => "Validate migration files for potential issues and errors.",
            "analyze_large_files" => "Identify large source files that may need refactoring for better maintainability.",
            "optimize_large_class" => "Analyze large classes and suggest refactoring strategies to improve design.",
            "optimize_large_method" => "Identify complex methods and suggest decomposition strategies.",
            "get_complexity_metrics" => "Calculate cyclomatic and cognitive complexity metrics for code.",
            "generate_optimization_plan" => "Create a prioritized optimization plan for improving code quality.",
            "detect_god_classes" => "Identify classes that have too many responsibilities and violate SRP.",
            "detect_god_methods" => "Find methods that are doing too many things and need decomposition.",
            "analyze_code_smells" => "Detect various code smells that indicate design issues.",
            "get_optimization_recommendations" => "Provide specific refactoring recommendations to improve code quality.",
            _ => $"A Phase 3 analysis tool: {toolName}"
        };
    }

    private Dictionary<string, object> GetToolCapabilities(string toolName)
    {
        return new Dictionary<string, object>
        {
            ["status"] = "coming_soon",
            ["phase"] = "phase_3",
            ["category"] = GetToolCategory(toolName),
            ["estimatedAvailability"] = "Future release",
            ["features"] = GetToolFeatures(toolName)
        };
    }

    private string GetToolCategory(string toolName)
    {
        if (toolName.StartsWith("analyze_migrations") || toolName.StartsWith("detect_") || toolName.StartsWith("get_migration") || toolName.StartsWith("generate_migration") || toolName.StartsWith("validate_migrations"))
            return "sql_migration_analyzer";

        if (toolName.StartsWith("analyze_large") || toolName.StartsWith("optimize_") || toolName.StartsWith("get_complexity") || toolName.StartsWith("generate_optimization") || toolName.StartsWith("detect_god") || toolName.StartsWith("analyze_code") || toolName.StartsWith("get_optimization"))
            return "large_file_optimizer";

        return "phase_3";
    }

    private List<string> GetToolFeatures(string toolName)
    {
        return toolName switch
        {
            "analyze_migrations" => new List<string> { "Migration parsing", "Dependency analysis", "Change detection" },
            "detect_breaking_changes" => new List<string> { "Schema analysis", "Breaking change detection", "Impact assessment" },
            "analyze_large_files" => new List<string> { "File size analysis", "Complexity scoring", "Issue detection" },
            "optimize_large_class" => new List<string> { "Class analysis", "Refactoring suggestions", "Code generation" },
            "optimize_large_method" => new List<string> { "Method decomposition", "Complexity reduction", "Code restructuring" },
            _ => new List<string> { "Advanced analysis", "Pattern detection", "Optimization suggestions" }
        };
    }

    #endregion

    #region Consolidated Service Execution Methods

    // ===== Universal File Operations Execution Methods =====

    private async Task<ToolCallResult> ExecuteGetFileInfo(JsonDocument arguments, CancellationToken ct)
    {
        if (_universalFileOps == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Universal file operations service not available"
            };
        }

        try
        {
            var path = arguments.RootElement.GetProperty("path").GetString();
            if (string.IsNullOrEmpty(path))
            {
                return new ToolCallResult { Success = false, Error = "Path is required" };
            }

            var includeMetadata = true;
            if (arguments.RootElement.TryGetProperty("includeMetadata", out var metadataElement))
            {
                includeMetadata = metadataElement.GetBoolean();
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new FileInfoRequest
            {
                Path = path,
                Options = new ToolOptions
                {
                    Include = includeMetadata ? IncludeFlags.Metadata : IncludeFlags.Default
                },
                RequestId = requestId
            };

            var result = await _universalFileOps.GetFileInfoAsync(request, ct);
            return new ToolCallResult
            {
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to get file info: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGetFileContent(JsonDocument arguments, CancellationToken ct)
    {
        if (_universalFileOps == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Universal file operations service not available"
            };
        }

        try
        {
            var path = arguments.RootElement.GetProperty("path").GetString();
            if (string.IsNullOrEmpty(path))
            {
                return new ToolCallResult { Success = false, Error = "Path is required" };
            }

            var encoding = "utf-8";
            if (arguments.RootElement.TryGetProperty("encoding", out var encodingElement))
            {
                encoding = encodingElement.GetString() ?? "utf-8";
            }

            JsonElement? lineRange = null;
            if (arguments.RootElement.TryGetProperty("lineRange", out var lineRangeElement))
            {
                lineRange = lineRangeElement;
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new FileContentRequest
            {
                Path = path,
                Options = lineRange?.ValueKind == JsonValueKind.Object ? new FileContentOptions
                {
                    StartLine = lineRange.Value.TryGetProperty("start", out var startElement) && startElement.ValueKind == JsonValueKind.Number ? startElement.GetInt32() : null,
                    EndLine = lineRange.Value.TryGetProperty("end", out var endElement) && endElement.ValueKind == JsonValueKind.Number ? endElement.GetInt32() : null
                } : null,
                RequestId = requestId
            };

            var result = await _universalFileOps.GetFileContentAsync(request, ct);
            return new ToolCallResult
            {
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to get file content: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteFileOperation(JsonDocument arguments, CancellationToken ct)
    {
        if (_universalFileOps == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Universal file operations service not available"
            };
        }

        try
        {
            var operationString = arguments.RootElement.GetProperty("operation").GetString();
            if (string.IsNullOrEmpty(operationString))
            {
                return new ToolCallResult { Success = false, Error = "Operation is required" };
            }

            if (!Enum.TryParse<FileOperationType>(operationString, true, out var operation))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid operation: {operationString}" };
            }

            var sourcePath = arguments.RootElement.GetProperty("sourcePath").GetString();
            var targetPath = arguments.RootElement.GetProperty("targetPath").GetString();
            var content = arguments.RootElement.GetProperty("content").GetString();

            var overwrite = false;
            if (arguments.RootElement.TryGetProperty("overwrite", out var overwriteElement))
            {
                overwrite = overwriteElement.GetBoolean();
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new FileOperationRequest
            {
                Path = sourcePath ?? string.Empty,
                Operation = operation,
                DestinationPath = targetPath,
                Options = new FileOperationOptions
                {
                    Overwrite = overwrite
                },
                RequestId = requestId
            };

            var result = await _universalFileOps.ExecuteFileOperationAsync(request, ct);
            return new ToolCallResult
            {
                Success = result.IsValid,
                Result = result.IsValid ? result : null,
                Error = result.IsValid ? null : result.Error
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to execute file operation: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteBatch(JsonDocument arguments, CancellationToken ct)
    {
        if (_universalFileOps == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Universal file operations service not available"
            };
        }

        try
        {
            var operationsArray = arguments.RootElement.GetProperty("operations");
            if (operationsArray.ValueKind != JsonValueKind.Array)
            {
                return new ToolCallResult { Success = false, Error = "Operations must be an array" };
            }

            var operations = new List<FileOperationDefinition>();
            foreach (var op in operationsArray.EnumerateArray())
            {
                var operationDef = JsonSerializer.Deserialize<FileOperationDefinition>(op.GetRawText());
                if (operationDef != null)
                {
                    operations.Add(operationDef);
                }
            }

            var continueOnError = false;
            if (arguments.RootElement.TryGetProperty("continueOnError", out var continueElement))
            {
                continueOnError = continueElement.GetBoolean();
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new FileBatchRequest
            {
                Operations = operations,
                ContinueOnError = continueOnError,
                RequestId = requestId
            };

            var result = await _universalFileOps.ExecuteBatchAsync(request, ct);
            return new ToolCallResult
            {
                Success = result.Success,
                Result = result.Success ? result : null,
                Error = result.Success ? null : result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to execute batch operation: {ex.Message}"
            };
        }
    }

    // ===== Unified Analysis Service Execution Methods =====

    private async Task<ToolCallResult> ExecuteAnalyzeSymbol(JsonDocument arguments, CancellationToken ct)
    {
        if (_unifiedAnalysis == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Unified analysis service not available"
            };
        }

        try
        {
            var symbolName = arguments.RootElement.GetProperty("symbolName").GetString();
            if (string.IsNullOrEmpty(symbolName))
            {
                return new ToolCallResult { Success = false, Error = "Symbol name is required" };
            }

            var filePath = arguments.RootElement.GetProperty("filePath").GetString();
            int? line = null;
            int? column = null;

            if (arguments.RootElement.TryGetProperty("line", out var lineElement))
            {
                line = lineElement.GetInt32();
            }

            if (arguments.RootElement.TryGetProperty("column", out var columnElement))
            {
                column = columnElement.GetInt32();
            }

            var includeReferences = true;
            if (arguments.RootElement.TryGetProperty("includeReferences", out var refsElement))
            {
                includeReferences = refsElement.GetBoolean();
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new SymbolAnalysisRequest
            {
                SymbolName = symbolName,
                Context = filePath != null ? $"{filePath}:{line}:{column}" : null,
                Options = new ToolOptions
                {
                    Include = includeReferences ? IncludeFlags.Dependencies : IncludeFlags.Default
                },
                RequestId = requestId
            };

            var result = await _unifiedAnalysis.AnalyzeSymbolAsync(request, ct);
            return new ToolCallResult
            {
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to analyze symbol: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeType(JsonDocument arguments, CancellationToken ct)
    {
        if (_unifiedAnalysis == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Unified analysis service not available"
            };
        }

        try
        {
            var typeName = arguments.RootElement.GetProperty("typeName").GetString();
            if (string.IsNullOrEmpty(typeName))
            {
                return new ToolCallResult { Success = false, Error = "Type name is required" };
            }

            var includeHierarchy = true;
            if (arguments.RootElement.TryGetProperty("includeHierarchy", out var hierarchyElement))
            {
                includeHierarchy = hierarchyElement.GetBoolean();
            }

            var includeMembers = true;
            if (arguments.RootElement.TryGetProperty("includeMembers", out var membersElement))
            {
                includeMembers = membersElement.GetBoolean();
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new TypeAnalysisRequest
            {
                TypeName = typeName,
                Options = new ToolOptions
                {
                    Include = (includeHierarchy ? IncludeFlags.History : 0) | (includeMembers ? IncludeFlags.Dependencies : 0)
                },
                RequestId = requestId
            };

            var result = await _unifiedAnalysis.AnalyzeTypeAsync(request, ct);
            return new ToolCallResult
            {
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to analyze type: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeFile(JsonDocument arguments, CancellationToken ct)
    {
        if (_unifiedAnalysis == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Unified analysis service not available"
            };
        }

        try
        {
            var filePath = arguments.RootElement.GetProperty("filePath").GetString();
            if (string.IsNullOrEmpty(filePath))
            {
                return new ToolCallResult { Success = false, Error = "File path is required" };
            }

            var includeStructure = true;
            if (arguments.RootElement.TryGetProperty("includeStructure", out var structureElement))
            {
                includeStructure = structureElement.GetBoolean();
            }

            var includeSymbols = true;
            if (arguments.RootElement.TryGetProperty("includeSymbols", out var symbolsElement))
            {
                includeSymbols = symbolsElement.GetBoolean();
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new FileAnalysisRequest
            {
                FilePath = filePath,
                IncludeStructure = includeStructure,
                IncludeSymbols = includeSymbols,
                RequestId = requestId
            };

            var result = await _unifiedAnalysis.AnalyzeFileAsync(request, ct);
            return new ToolCallResult
            {
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to analyze file: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeProject(JsonDocument arguments, CancellationToken ct)
    {
        if (_unifiedAnalysis == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Unified analysis service not available"
            };
        }

        try
        {
            var projectPath = arguments.RootElement.GetProperty("projectPath").GetString();

            var includeDependencies = true;
            if (arguments.RootElement.TryGetProperty("includeDependencies", out var depsElement))
            {
                includeDependencies = depsElement.GetBoolean();
            }

            var includeMetrics = true;
            if (arguments.RootElement.TryGetProperty("includeMetrics", out var metricsElement))
            {
                includeMetrics = metricsElement.GetBoolean();
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new ProjectAnalysisRequest
            {
                ProjectPath = projectPath,
                IncludeDependencies = includeDependencies,
                IncludeMetrics = includeMetrics,
                RequestId = requestId
            };

            var result = await _unifiedAnalysis.AnalyzeProjectAsync(request, ct);
            return new ToolCallResult
            {
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to analyze project: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeArchitecture(JsonDocument arguments, CancellationToken ct)
    {
        if (_unifiedAnalysis == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Unified analysis service not available"
            };
        }

        try
        {
            var projectPath = arguments.RootElement.GetProperty("projectPath").GetString();

            var includeViolations = true;
            if (arguments.RootElement.TryGetProperty("includeViolations", out var violationsElement))
            {
                includeViolations = violationsElement.GetBoolean();
            }

            var includeRecommendations = true;
            if (arguments.RootElement.TryGetProperty("includeRecommendations", out var recsElement))
            {
                includeRecommendations = recsElement.GetBoolean();
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new ArchitectureAnalysisRequest
            {
                ProjectPath = projectPath,
                IncludeViolations = includeViolations,
                IncludeRecommendations = includeRecommendations,
                Scope = AnalysisScope.All,
                RequestId = requestId
            };

            var result = await _unifiedAnalysis.AnalyzeArchitectureAsync(request, ct);
            return new ToolCallResult
            {
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to analyze architecture: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeDependenciesUnified(JsonDocument arguments, CancellationToken ct)
    {
        if (_unifiedAnalysis == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Unified analysis service not available"
            };
        }

        try
        {
            var typeName = arguments.RootElement.GetProperty("typeName").GetString();
            var projectPath = arguments.RootElement.GetProperty("projectPath").GetString();

            var includeCircular = true;
            if (arguments.RootElement.TryGetProperty("includeCircular", out var circularElement))
            {
                includeCircular = circularElement.GetBoolean();
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new DependencyAnalysisRequest
            {
                TypeName = typeName,
                ProjectPath = projectPath,
                IncludeCircular = includeCircular,
                Scope = AnalysisScope.Dependencies,
                RequestId = requestId
            };

            var result = await _unifiedAnalysis.AnalyzeDependenciesAsync(request, ct);
            return new ToolCallResult
            {
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to analyze dependencies: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeQuality(JsonDocument arguments, CancellationToken ct)
    {
        if (_unifiedAnalysis == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Unified analysis service not available"
            };
        }

        try
        {
            var filePath = arguments.RootElement.GetProperty("filePath").GetString();
            var projectPath = arguments.RootElement.GetProperty("projectPath").GetString();

            var includeMetrics = true;
            if (arguments.RootElement.TryGetProperty("includeMetrics", out var metricsElement))
            {
                includeMetrics = metricsElement.GetBoolean();
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new QualityAnalysisRequest
            {
                FilePath = filePath,
                ProjectPath = projectPath,
                IncludeMetrics = includeMetrics,
                Scope = AnalysisScope.All,
                RequestId = requestId
            };

            var result = await _unifiedAnalysis.AnalyzeQualityAsync(request, ct);
            return new ToolCallResult
            {
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to analyze quality: {ex.Message}"
            };
        }
    }

    // ===== Bulk Operations Hub Execution Methods =====

    private async Task<ToolCallResult> ExecuteBulkOperation(JsonDocument arguments, CancellationToken ct)
    {
        if (_bulkOperationsHub == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Bulk operations hub not available"
            };
        }

        try
        {
            var operationTypeString = arguments.RootElement.GetProperty("operationType").GetString();
            if (string.IsNullOrEmpty(operationTypeString))
            {
                return new ToolCallResult { Success = false, Error = "Operation type is required" };
            }

            if (!Enum.TryParse<BulkOperationType>(operationTypeString, true, out var operationType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid operation type: {operationTypeString}" };
            }

            var filesArray = arguments.RootElement.GetProperty("files");
            if (filesArray.ValueKind != JsonValueKind.Array)
            {
                return new ToolCallResult { Success = false, Error = "Files must be an array" };
            }

            var files = new List<string>();
            foreach (var file in filesArray.EnumerateArray())
            {
                files.Add(file.GetString() ?? "");
            }

            var operationElement = arguments.RootElement.GetProperty("operation");
            var optionsElement = arguments.RootElement.GetProperty("options");
            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            // Parse options from JsonElement
            BulkOperationOptions? options = null;
            if (optionsElement.ValueKind == JsonValueKind.Object)
            {
                options = new BulkOperationOptions();
                if (optionsElement.TryGetProperty("createBackup", out var createBackupElement))
                    options.CreateBackup = createBackupElement.GetBoolean();
                if (optionsElement.TryGetProperty("maxParallelism", out var maxParallelismElement))
                    options.MaxParallelism = maxParallelismElement.GetInt32();
                if (optionsElement.TryGetProperty("dryRun", out var dryRunElement))
                    options.DryRun = dryRunElement.GetBoolean();
                if (optionsElement.TryGetProperty("failFast", out var failFastElement))
                    options.FailFast = failFastElement.GetBoolean();
            }

            var request = new BulkOperationRequest
            {
                OperationType = operationType,
                Files = files,
                Operation = operationElement.GetString(),
                Options = options,
                RequestId = requestId
            };

            var result = await _bulkOperationsHub.ExecuteBulkOperationAsync(request, ct);
            return new ToolCallResult
            {
                Success = result.Success,
                Result = result.Success ? result : null,
                Error = result.Success ? null : result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to execute bulk operation: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecutePreviewBulkOperation(JsonDocument arguments, CancellationToken ct)
    {
        if (_bulkOperationsHub == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Bulk operations hub not available"
            };
        }

        try
        {
            var operationTypeString = arguments.RootElement.GetProperty("operationType").GetString();
            if (string.IsNullOrEmpty(operationTypeString))
            {
                return new ToolCallResult { Success = false, Error = "Operation type is required" };
            }

            if (!Enum.TryParse<BulkOperationType>(operationTypeString, true, out var operationType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid operation type: {operationTypeString}" };
            }

            var filesArray = arguments.RootElement.GetProperty("files");
            if (filesArray.ValueKind != JsonValueKind.Array)
            {
                return new ToolCallResult { Success = false, Error = "Files must be an array" };
            }

            var files = new List<string>();
            foreach (var file in filesArray.EnumerateArray())
            {
                files.Add(file.GetString() ?? "");
            }

            var operationElement = arguments.RootElement.GetProperty("operation");

            var maxPreviewFiles = 10;
            if (arguments.RootElement.TryGetProperty("maxPreviewFiles", out var maxElement))
            {
                maxPreviewFiles = maxElement.GetInt32();
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new BulkPreviewRequest
            {
                OperationType = operationType,
                Files = files,
                Operation = operationElement.GetString(),
                MaxPreviewFiles = maxPreviewFiles,
                RequestId = requestId
            };

            var result = await _bulkOperationsHub.PreviewBulkOperationAsync(request, ct);
            return new ToolCallResult
            {
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to preview bulk operation: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGetBulkProgress(JsonDocument arguments, CancellationToken ct)
    {
        if (_bulkOperationsHub == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Bulk operations hub not available"
            };
        }

        try
        {
            var operationId = arguments.RootElement.GetProperty("operationId").GetString();
            if (string.IsNullOrEmpty(operationId))
            {
                return new ToolCallResult { Success = false, Error = "Operation ID is required" };
            }

            var result = await _bulkOperationsHub.GetBulkProgressAsync(operationId, ct);
            return new ToolCallResult
            {
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to get bulk progress: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteManageBulkOperation(JsonDocument arguments, CancellationToken ct)
    {
        if (_bulkOperationsHub == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Bulk operations hub not available"
            };
        }

        try
        {
            var operationId = arguments.RootElement.GetProperty("operationId").GetString();
            if (string.IsNullOrEmpty(operationId))
            {
                return new ToolCallResult { Success = false, Error = "Operation ID is required" };
            }

            var actionString = arguments.RootElement.GetProperty("action").GetString();
            if (string.IsNullOrEmpty(actionString))
            {
                return new ToolCallResult { Success = false, Error = "Action is required" };
            }

            if (!Enum.TryParse<BulkManagementAction>(actionString, true, out var action))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid action: {actionString}" };
            }

            var request = new BulkManagementRequest
            {
                OperationId = operationId,
                Action = action
            };

            var result = await _bulkOperationsHub.ManageBulkOperationAsync(request, ct);
            return new ToolCallResult
            {
                Success = result.Success,
                Result = result.Success ? result : null,
                Error = result.Success ? null : result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to manage bulk operation: {ex.Message}"
            };
        }
    }

    // ===== Stream Processing Controller Execution Methods =====

    private async Task<ToolCallResult> ExecuteProcessStream(JsonDocument arguments, CancellationToken ct)
    {
        if (_streamController == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Stream processing controller not available"
            };
        }

        try
        {
            var filePath = arguments.RootElement.GetProperty("filePath").GetString();
            if (string.IsNullOrEmpty(filePath))
            {
                return new ToolCallResult { Success = false, Error = "File path is required" };
            }

            var processorType = arguments.RootElement.GetProperty("processorType").GetString();
            if (string.IsNullOrEmpty(processorType))
            {
                return new ToolCallResult { Success = false, Error = "Processor type is required" };
            }

            var outputPath = arguments.RootElement.GetProperty("outputPath").GetString();
            if (string.IsNullOrEmpty(outputPath))
            {
                return new ToolCallResult { Success = false, Error = "Output path is required" };
            }

            ProcessorOptions? processorOptions = null;
            if (arguments.RootElement.TryGetProperty("processorOptions", out var processorOptionsElement) &&
                processorOptionsElement.ValueKind == JsonValueKind.Object)
            {
                processorOptions = new ProcessorOptions();
                if (processorOptionsElement.TryGetProperty("chunkSize", out var psChunkElement))
                    processorOptions.ChunkSize = psChunkElement.GetInt32();
                if (processorOptionsElement.TryGetProperty("enableCompression", out var compressElement))
                    processorOptions.EnableCompression = compressElement.GetBoolean();
                if (processorOptionsElement.TryGetProperty("enableCheckpoints", out var checkpointsElement))
                    processorOptions.EnableCheckpoints = checkpointsElement.GetBoolean();
                if (processorOptionsElement.TryGetProperty("maxParallelChunks", out var parallelElement))
                    processorOptions.MaxParallelChunks = parallelElement.GetInt32();
            }

            var chunkSize = 65536;
            if (arguments.RootElement.TryGetProperty("chunkSize", out var chunkElement))
            {
                chunkSize = chunkElement.GetInt32();
            }

            var requestId = arguments.RootElement.GetProperty("requestId").GetString();

            var request = new MCPsharp.Models.Consolidated.StreamProcessRequest
            {
                InputType = MCPsharp.Models.Consolidated.StreamInputType.File,
                FilePath = filePath,
                ProcessorType = processorType,
                OutputPath = outputPath,
                ProcessorOptions = processorOptions,
                ChunkSize = chunkSize,
                RequestId = requestId
            };

            var result = await _streamController.ProcessStreamAsync(request, ct);
            return new ToolCallResult
            {
                Success = result.Success,
                Result = result.Success ? result : null,
                Error = result.Success ? null : result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to process stream: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteMonitorStream(JsonDocument arguments, CancellationToken ct)
    {
        if (_streamController == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Stream processing controller not available"
            };
        }

        try
        {
            var operationId = arguments.RootElement.GetProperty("operationId").GetString();
            if (string.IsNullOrEmpty(operationId))
            {
                return new ToolCallResult { Success = false, Error = "Operation ID is required" };
            }

            var includeDetails = true;
            if (arguments.RootElement.TryGetProperty("includeDetails", out var detailsElement))
            {
                includeDetails = detailsElement.GetBoolean();
            }

            var request = new StreamMonitorRequest
            {
                IncludeDetails = includeDetails
            };

            var result = await _streamController.MonitorStreamAsync(operationId, request, ct);
            return new ToolCallResult
            {
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to monitor stream: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteManageStream(JsonDocument arguments, CancellationToken ct)
    {
        if (_streamController == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Stream processing controller not available"
            };
        }

        try
        {
            var operationId = arguments.RootElement.GetProperty("operationId").GetString();
            var actionString = arguments.RootElement.GetProperty("action").GetString();

            if (!Enum.TryParse<StreamManagementAction>(actionString, true, out var action))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid action: {actionString}" };
            }

            var cleanupOlderThanHours = 24;
            if (arguments.RootElement.TryGetProperty("cleanupOlderThanHours", out var cleanupElement))
            {
                cleanupOlderThanHours = cleanupElement.GetInt32();
            }

            var request = new StreamManagementRequest
            {
                OperationId = operationId,
                Action = action,
                CleanupOlderThanHours = cleanupOlderThanHours
            };

            var result = await _streamController.ManageStreamAsync(request, ct);
            return new ToolCallResult
            {
                Success = result.Success,
                Result = result.Success ? result : null,
                Error = result.Success ? null : result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to manage stream: {ex.Message}"
            };
        }
    }

    #endregion
}

/// <summary>
/// Extension methods for JsonElement
/// </summary>
internal static class JsonElementExtensions
{
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value : null;
    }
}
