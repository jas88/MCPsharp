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
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry;
using MCPsharp.Models.LargeFileOptimization;
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
    private RenameSymbolService? _renameSymbol;

    // Phase 3 services
    private IArchitectureValidatorService? _architectureValidator;
    private IDuplicateCodeDetectorService? _duplicateCodeDetector;
    private ISqlMigrationAnalyzerService? _sqlMigrationAnalyzer;
    private ILargeFileOptimizerService? _largeFileOptimizer;

    // Search service
    private readonly ISearchService? _searchService;

    // Roslyn Analyzer service
    private readonly IRoslynAnalyzerService? _roslynAnalyzerService;

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
    private readonly UniversalFileOperations? _universalFileOps;
    private readonly UnifiedAnalysisService? _unifiedAnalysis;
    private readonly BulkOperationsHub? _bulkOperationsHub;
    private readonly StreamProcessingController? _streamController;

    // Response processing for token limiting
    private readonly ResponseProcessor _responseProcessor;
    private readonly ILoggerFactory? _loggerFactory;

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
        IRoslynAnalyzerService? roslynAnalyzerService = null,
        BuiltInCodeFixRegistry? codeFixRegistry = null,
        ISearchService? searchService = null,
        ILoggerFactory? loggerFactory = null)
    {
        _projectContext = projectContext;
        _workspace = workspace;
        _codeFixRegistry = codeFixRegistry;
        _workflowAnalyzer = workflowAnalyzer;
        _configAnalyzer = configAnalyzer;
        _impactAnalyzer = impactAnalyzer;
        _featureTracer = featureTracer;
        _bulkEditService = bulkEditService;
        _streamingProcessor = streamingProcessor;
        _progressTracker = progressTracker;
        _tempFileManager = tempFileManager;

        // Phase 3 services
        _sqlMigrationAnalyzer = sqlMigrationAnalyzer;
        _largeFileOptimizer = largeFileOptimizer;

        // Consolidated services
        _universalFileOps = universalFileOps;
        _unifiedAnalysis = unifiedAnalysis;
        _bulkOperationsHub = bulkOperationsHub;
        _streamController = streamController;
        _roslynAnalyzerService = roslynAnalyzerService;
        _searchService = searchService;
        _loggerFactory = loggerFactory;

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
                "search_text" => await ExecuteSearchText(request.Arguments, ct),
                "find_symbol" => await ExecuteFindSymbol(request.Arguments),
                "get_symbol_info" => await ExecuteGetSymbolInfo(request.Arguments),
                "get_class_structure" => await ExecuteGetClassStructure(request.Arguments),
                "add_class_property" => await ExecuteAddClassProperty(request.Arguments),
                "add_class_method" => await ExecuteAddClassMethod(request.Arguments),
                "find_references" => await ExecuteFindReferences(request.Arguments),
                "find_implementations" => await ExecuteFindImplementations(request.Arguments),
                "parse_project" => await ExecuteParseProject(request.Arguments),
                "rename_symbol" => await ExecuteRenameSymbol(request.Arguments, ct),
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
                // Roslyn Analyzer Tools
                // TODO: Implement these methods or route to MCPSharpTools
                // "load_roslyn_analyzers" => await ExecuteLoadRoslynAnalyzers(request.Arguments, ct),
                // "run_roslyn_analyzers" => await ExecuteRunRoslynAnalyzers(request.Arguments, ct),
                // "list_roslyn_analyzers" => await ExecuteListRoslynAnalyzers(request.Arguments, ct),
                // Automated Code Fix Tools
                "code_quality_analyze" => await ExecuteCodeQualityAnalyze(request.Arguments, ct),
                "code_quality_fix" => await ExecuteCodeQualityFix(request.Arguments, ct),
                "code_quality_profiles" => await ExecuteCodeQualityProfiles(request.Arguments, ct),
                // Roslyn Code Fix and Configuration Tools
                "apply_roslyn_fixes" => await ExecuteApplyRoslynFixes(request.Arguments, ct),
                "configure_analyzer" => await ExecuteConfigureAnalyzer(request.Arguments, ct),
                "get_analyzer_config" => await ExecuteGetAnalyzerConfig(request.Arguments, ct),
                "reset_analyzer_config" => await ExecuteResetAnalyzerConfig(request.Arguments, ct),
                "generate_analysis_report" => await ExecuteGenerateAnalysisReport(request.Arguments, ct),
                // SQL Migration Analyzer Tools (Phase 3)
                "analyze_migrations" => await ExecuteAnalyzeMigrations(request.Arguments, ct),
                "detect_breaking_changes" => await ExecuteDetectBreakingChanges(request.Arguments, ct),
                "get_migration_history" => await ExecuteGetMigrationHistory(request.Arguments, ct),
                "get_migration_dependencies" => await ExecuteGetMigrationDependencies(request.Arguments, ct),
                "generate_migration_report" => await ExecuteGenerateMigrationReport(request.Arguments, ct),
                "validate_migrations" => await ExecuteValidateMigrations(request.Arguments, ct),
                // Large File Optimization Tools (Phase 3)
                "analyze_large_files" => await ExecuteAnalyzeLargeFiles(request.Arguments, ct),
                "optimize_large_class" => await ExecuteOptimizeLargeClass(request.Arguments, ct),
                "optimize_large_method" => await ExecuteOptimizeLargeMethod(request.Arguments, ct),
                "get_complexity_metrics" => await ExecuteGetComplexityMetrics(request.Arguments, ct),
                "generate_optimization_plan" => await ExecuteGenerateOptimizationPlan(request.Arguments, ct),
                "detect_god_classes" => await ExecuteDetectGodClasses(request.Arguments, ct),
                "detect_god_methods" => await ExecuteDetectGodMethods(request.Arguments, ct),
                "analyze_code_smells" => await ExecuteAnalyzeCodeSmells(request.Arguments, ct),
                // Refactoring tools
                // TODO: Implement method extraction
                // "extract_method" => await ExecuteExtractMethod(request.Arguments, ct),
                "get_optimization_recommendations" => await ExecuteGetOptimizationRecommendations(request.Arguments, ct),

            // Consolidated Service Tool Routes
            // TODO: Integrate these with consolidated services or implement methods
            // Universal File Operations Tools
            // "get_file_info" => await ExecuteGetFileInfo(request.Arguments, ct),
            // "get_file_content" => await ExecuteGetFileContent(request.Arguments, ct),
            // "execute_file_operation" => await ExecuteFileOperation(request.Arguments, ct),
            // "execute_batch" => await ExecuteBatch(request.Arguments, ct),

            // // Unified Analysis Service Tools
            // "analyze_symbol" => await ExecuteAnalyzeSymbol(request.Arguments, ct),
            // "analyze_type" => await ExecuteAnalyzeType(request.Arguments, ct),
            // "analyze_file" => await ExecuteAnalyzeFile(request.Arguments, ct),
            // "analyze_project" => await ExecuteAnalyzeProject(request.Arguments, ct),
            // "analyze_architecture" => await ExecuteAnalyzeArchitecture(request.Arguments, ct),
            // "analyze_dependencies" => await ExecuteAnalyzeDependenciesUnified(request.Arguments, ct),
            // "analyze_quality" => await ExecuteAnalyzeQuality(request.Arguments, ct),

            // // Bulk Operations Hub Tools
            // "execute_bulk_operation" => await ExecuteBulkOperation(request.Arguments, ct),
            // "preview_bulk_operation" => await ExecutePreviewBulkOperation(request.Arguments, ct),
            // "get_bulk_progress" => await ExecuteGetBulkProgress(request.Arguments, ct),
            // "manage_bulk_operation" => await ExecuteManageBulkOperation(request.Arguments, ct),

            // // Stream Processing Controller Tools
            // "process_stream" => await ExecuteProcessStream(request.Arguments, ct),
            // "monitor_stream" => await ExecuteMonitorStream(request.Arguments, ct),
            // "manage_stream" => await ExecuteManageStream(request.Arguments, ct),

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
                Name = "search_text",
                Description = "Search for text or regex patterns across project files",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "pattern",
                        Type = "string",
                        Description = "Text or regex pattern to search for",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "target_path",
                        Type = "string",
                        Description = "File or directory to search (default: current project)",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "regex",
                        Type = "boolean",
                        Description = "Treat pattern as regex (default: false)",
                        Required = false,
                        Default = false
                    },
                    new PropertyDefinition
                    {
                        Name = "case_sensitive",
                        Type = "boolean",
                        Description = "Case-sensitive search (default: true)",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "include_pattern",
                        Type = "string",
                        Description = "Glob pattern for files to include (e.g., \"*.cs\")",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "exclude_pattern",
                        Type = "string",
                        Description = "Glob pattern for files to exclude",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "context_lines",
                        Type = "integer",
                        Description = "Lines of context before/after match (default: 2)",
                        Required = false,
                        Default = 2
                    },
                    new PropertyDefinition
                    {
                        Name = "max_results",
                        Type = "integer",
                        Description = "Maximum results to return (default: 100)",
                        Required = false,
                        Default = 100
                    },
                    new PropertyDefinition
                    {
                        Name = "offset",
                        Type = "integer",
                        Description = "Pagination offset (default: 0)",
                        Required = false,
                        Default = 0
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
            },
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
            // Reverse search tools
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
            // Automated Code Fix Tools
            new McpTool
            {
                Name = "code_quality_analyze",
                Description = "Analyze code for quality issues using built-in diagnostic analyzers",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "target_path",
                        Type = "string",
                        Description = "File or directory to analyze",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "diagnostic_ids",
                        Type = "array",
                        Description = "Specific diagnostic IDs to check (e.g., [\"MCP001\", \"MCP002\"]). Optional - if not specified, all diagnostics for the profile are checked.",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "profile",
                        Type = "string",
                        Description = "Fix profile to use: Conservative, Balanced, or Aggressive (default: Balanced)",
                        Required = false,
                        Default = "Balanced"
                    }
                )
            },
            new McpTool
            {
                Name = "code_quality_fix",
                Description = "Apply automated code quality fixes to improve code",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "target_path",
                        Type = "string",
                        Description = "File or directory to fix",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "diagnostic_ids",
                        Type = "array",
                        Description = "Specific diagnostic IDs to fix. Optional - if not specified, all safe fixes for the profile are applied.",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "profile",
                        Type = "string",
                        Description = "Fix profile to use: Conservative, Balanced, or Aggressive (default: Balanced)",
                        Required = false,
                        Default = "Balanced"
                    },
                    new PropertyDefinition
                    {
                        Name = "preview",
                        Type = "boolean",
                        Description = "Show preview without applying changes (default: true for safety)",
                        Required = false,
                        Default = true
                    },
                    new PropertyDefinition
                    {
                        Name = "create_backup",
                        Type = "boolean",
                        Description = "Create backup before fixing (default: true)",
                        Required = false,
                        Default = true
                    }
                )
            },
            new McpTool
            {
                Name = "code_quality_profiles",
                Description = "List available fix profiles and their capabilities",
                InputSchema = JsonSchemaHelper.CreateSchema()
            },
            // Refactoring Tools
            new McpTool
            {
                Name = "extract_method",
                Description = "Extract selected code into a new method with automatic parameter inference",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    new PropertyDefinition
                    {
                        Name = "file_path",
                        Type = "string",
                        Description = "Path to the file containing code to extract",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "start_line",
                        Type = "integer",
                        Description = "Starting line number (1-based)",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "end_line",
                        Type = "integer",
                        Description = "Ending line number (1-based)",
                        Required = true
                    },
                    new PropertyDefinition
                    {
                        Name = "method_name",
                        Type = "string",
                        Description = "Name for the extracted method (optional, auto-generated if not provided)",
                        Required = false
                    },
                    new PropertyDefinition
                    {
                        Name = "accessibility",
                        Type = "string",
                        Description = "Method accessibility: private, public, protected, internal (default: private)",
                        Required = false,
                        Default = "private"
                    },
                    new PropertyDefinition
                    {
                        Name = "preview",
                        Type = "boolean",
                        Description = "Show preview without applying changes (default: true)",
                        Required = false,
                        Default = true
                    }
                )
            }
        };
    }

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

                // Initialize rename symbol service
                _renameSymbol = new RenameSymbolService(_workspace, _advancedReferenceFinder, _symbolQuery,
                    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<RenameSymbolService>());

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

            // Initialize workspace if available
            if (_workspace != null)
            {
                await _workspace.InitializeAsync(path);
                _fileOperations = new FileOperationsService(path, _workspace);
            }
            else
            {
                _fileOperations = new FileOperationsService(path);
            }

            // Initialize Roslyn services if workspace is available
            if (_workspace != null)
            {
                _symbolQuery = new SymbolQueryService(_workspace);
                _classStructure = new ClassStructureService(_workspace);
                _semanticEdit = new SemanticEditService(_workspace, _classStructure);
                _referenceFinder = new ReferenceFinderService(_workspace);
                _projectParser = new ProjectParserService();

                // Initialize advanced reverse search services for project opening
                _callerAnalysis = new CallerAnalysisService(_workspace, _symbolQuery);
                _callChain = new CallChainService(_workspace, _symbolQuery, _callerAnalysis);
                _typeUsage = new TypeUsageService(_workspace, _symbolQuery);
                _advancedReferenceFinder = new AdvancedReferenceFinderService(_workspace, _symbolQuery, _callerAnalysis, _callChain, _typeUsage);

                // Initialize rename symbol service
                _renameSymbol = new RenameSymbolService(_workspace, _advancedReferenceFinder, _symbolQuery,
                    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<RenameSymbolService>());

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

    private async Task<ToolCallResult> ExecuteSearchText(JsonDocument arguments, CancellationToken ct)
    {
        if (_searchService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Search service not available"
            };
        }

        var pattern = arguments.RootElement.GetProperty("pattern").GetString();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Pattern is required"
            };
        }

        var request = new Models.Search.SearchRequest
        {
            Pattern = pattern,
            TargetPath = arguments.RootElement.TryGetProperty("target_path", out var targetPathProp) && targetPathProp.ValueKind != JsonValueKind.Null
                ? targetPathProp.GetString()
                : null,
            Regex = arguments.RootElement.TryGetProperty("regex", out var regexProp) && regexProp.ValueKind == JsonValueKind.True,
            CaseSensitive = !arguments.RootElement.TryGetProperty("case_sensitive", out var caseProp) || caseProp.ValueKind != JsonValueKind.False,
            IncludePattern = arguments.RootElement.TryGetProperty("include_pattern", out var incProp) && incProp.ValueKind != JsonValueKind.Null
                ? incProp.GetString()
                : null,
            ExcludePattern = arguments.RootElement.TryGetProperty("exclude_pattern", out var excProp) && excProp.ValueKind != JsonValueKind.Null
                ? excProp.GetString()
                : null,
            ContextLines = arguments.RootElement.TryGetProperty("context_lines", out var contextProp) && contextProp.ValueKind == JsonValueKind.Number
                ? contextProp.GetInt32()
                : 2,
            MaxResults = arguments.RootElement.TryGetProperty("max_results", out var maxProp) && maxProp.ValueKind == JsonValueKind.Number
                ? maxProp.GetInt32()
                : 100,
            Offset = arguments.RootElement.TryGetProperty("offset", out var offsetProp) && offsetProp.ValueKind == JsonValueKind.Number
                ? offsetProp.GetInt32()
                : 0
        };

        var result = await _searchService.SearchTextAsync(request, ct);

        return new ToolCallResult
        {
            Success = result.Success,
            Result = result.Success ? new
            {
                result.Success,
                result.TotalMatches,
                result.Returned,
                result.Offset,
                result.HasMore,
                Matches = result.Matches.Select(m => new
                {
                    m.FilePath,
                    m.LineNumber,
                    m.ColumnNumber,
                    m.MatchText,
                    m.ContextBefore,
                    m.ContextAfter,
                    m.LineContent
                }),
                result.FilesSearched,
                result.SearchDurationMs
            } : null,
            Error = result.ErrorMessage
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
                Error = result.Errors.Any()
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

        if (!arguments.RootElement.TryGetProperty("methodName", out var methodNameElement) ||
            string.IsNullOrEmpty(methodNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "MethodName is required" };
        }

        var methodName = methodNameElement.GetString()!;

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

        if (!arguments.RootElement.TryGetProperty("methodName", out var methodNameElement) ||
            string.IsNullOrEmpty(methodNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "MethodName is required" };
        }

        var methodName = methodNameElement.GetString()!;

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

        if (!arguments.RootElement.TryGetProperty("typeName", out var typeNameElement) ||
            string.IsNullOrEmpty(typeNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "TypeName is required" };
        }

        var typeName = typeNameElement.GetString()!;

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

        if (!arguments.RootElement.TryGetProperty("methodName", out var methodNameElement) ||
            string.IsNullOrEmpty(methodNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "MethodName is required" };
        }

        var methodName = methodNameElement.GetString()!;

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

        if (!arguments.RootElement.TryGetProperty("typeName", out var typeNameElement) ||
            string.IsNullOrEmpty(typeNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "TypeName is required" };
        }

        var typeName = typeNameElement.GetString()!;

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

        if (!arguments.RootElement.TryGetProperty("methodName", out var methodNameElement) ||
            string.IsNullOrEmpty(methodNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "MethodName is required" };
        }

        var methodName = methodNameElement.GetString()!;

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

        if (!arguments.RootElement.TryGetProperty("typeName", out var typeNameElement) ||
            string.IsNullOrEmpty(typeNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "TypeName is required" };
        }

        var typeName = typeNameElement.GetString()!;

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

            var suggestion = JsonSerializer.Deserialize<MCPsharp.Models.RefactoringSuggestion>(suggestionElement.Value.GetRawText());
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
                Complexity = new MCPsharp.Models.ComplexityMetrics
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

    #region Phase 3 SQL Migration Analyzer Execution Methods

    private async Task<ToolCallResult> ExecuteAnalyzeMigrations(JsonDocument arguments, CancellationToken ct)
    {
        if (_sqlMigrationAnalyzer == null)
        {
            return new ToolCallResult { Success = false, Error = "SQL migration analyzer service not available" };
        }

        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();

        try
        {
            var result = await _sqlMigrationAnalyzer.AnalyzeMigrationsAsync(projectPath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalMigrations"] = result.Summary.TotalMigrations,
                    ["appliedMigrations"] = result.Summary.AppliedMigrations,
                    ["pendingMigrations"] = result.Summary.PendingMigrations,
                    ["breakingChanges"] = result.Summary.BreakingChanges,
                    ["highRiskOperations"] = result.Summary.HighRiskOperations,
                    ["provider"] = result.Summary.Provider.ToString(),
                    ["analysisTime"] = result.Summary.TotalAnalysisTime.TotalSeconds,
                    ["migrations"] = result.Migrations.Take(10).Select(m => new
                    {
                        name = m.Name,
                        filePath = m.FilePath,
                        createdAt = m.CreatedAt,
                        isApplied = m.IsApplied,
                        operationCount = m.Operations.Count,
                        hasBreakingChanges = m.Operations.Any(o => o.IsBreakingChange)
                    }).ToList(),
                    ["topBreakingChanges"] = result.BreakingChanges.Take(5).Select(bc => new
                    {
                        type = bc.Type,
                        severity = bc.Severity.ToString(),
                        description = bc.Description,
                        tableName = bc.TableName,
                        recommendation = bc.Recommendation
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error analyzing migrations: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteDetectBreakingChanges(JsonDocument arguments, CancellationToken ct)
    {
        if (_sqlMigrationAnalyzer == null)
        {
            return new ToolCallResult { Success = false, Error = "SQL migration analyzer service not available" };
        }

        var fromMigration = GetStringArgument(arguments, "fromMigration");
        var toMigration = GetStringArgument(arguments, "toMigration");
        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();

        if (string.IsNullOrEmpty(fromMigration) || string.IsNullOrEmpty(toMigration))
        {
            return new ToolCallResult { Success = false, Error = "fromMigration and toMigration parameters are required" };
        }

        try
        {
            var breakingChanges = await _sqlMigrationAnalyzer.DetectBreakingChangesAsync(fromMigration, toMigration, projectPath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalBreakingChanges"] = breakingChanges.Count,
                    ["criticalChanges"] = breakingChanges.Count(bc => bc.Severity == MCPsharp.Models.SqlMigration.Severity.Critical),
                    ["highSeverityChanges"] = breakingChanges.Count(bc => bc.Severity == MCPsharp.Models.SqlMigration.Severity.High),
                    ["breakingChanges"] = breakingChanges.Select(bc => new
                    {
                        type = bc.Type,
                        severity = bc.Severity.ToString(),
                        description = bc.Description,
                        tableName = bc.TableName,
                        columnName = bc.ColumnName,
                        fromMigration = bc.FromMigration,
                        toMigration = bc.ToMigration,
                        recommendation = bc.Recommendation
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error detecting breaking changes: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteGetMigrationHistory(JsonDocument arguments, CancellationToken ct)
    {
        if (_sqlMigrationAnalyzer == null)
        {
            return new ToolCallResult { Success = false, Error = "SQL migration analyzer service not available" };
        }

        var dbContextPath = GetStringArgument(arguments, "dbContextPath");
        if (string.IsNullOrEmpty(dbContextPath))
        {
            return new ToolCallResult { Success = false, Error = "dbContextPath parameter is required" };
        }

        try
        {
            var history = await _sqlMigrationAnalyzer.GetMigrationHistoryAsync(dbContextPath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalEntries"] = history.Count,
                    ["history"] = history.Select(h => new
                    {
                        migrationName = h.MigrationName,
                        appliedAt = h.AppliedAt,
                        checksum = h.Checksum,
                        executionTime = h.ExecutionTime,
                        isSuccessful = h.IsSuccessful
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error getting migration history: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteGetMigrationDependencies(JsonDocument arguments, CancellationToken ct)
    {
        if (_sqlMigrationAnalyzer == null)
        {
            return new ToolCallResult { Success = false, Error = "SQL migration analyzer service not available" };
        }

        var migrationName = GetStringArgument(arguments, "migrationName");
        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();

        if (string.IsNullOrEmpty(migrationName))
        {
            return new ToolCallResult { Success = false, Error = "migrationName parameter is required" };
        }

        try
        {
            var dependency = await _sqlMigrationAnalyzer.GetMigrationDependenciesAsync(migrationName, projectPath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["migrationName"] = dependency.MigrationName,
                    ["dependencyType"] = dependency.Type.ToString(),
                    ["dependsOn"] = dependency.DependsOn,
                    ["requiredBy"] = dependency.RequiredBy,
                    ["totalDependencies"] = dependency.DependsOn.Count,
                    ["totalDependents"] = dependency.RequiredBy.Count
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error getting migration dependencies: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteGenerateMigrationReport(JsonDocument arguments, CancellationToken ct)
    {
        if (_sqlMigrationAnalyzer == null)
        {
            return new ToolCallResult { Success = false, Error = "SQL migration analyzer service not available" };
        }

        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
        var includeHistory = GetBoolArgument(arguments, "includeHistory") ?? true;

        try
        {
            var report = await _sqlMigrationAnalyzer.GenerateMigrationReportAsync(projectPath, includeHistory, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["metadata"] = new
                    {
                        projectPath = report.Metadata.ProjectPath,
                        generatedAt = report.Metadata.GeneratedAt,
                        analyzerVersion = report.Metadata.AnalyzerVersion,
                        provider = report.Metadata.Provider.ToString(),
                        totalMigrations = report.Metadata.TotalMigrations,
                        analysisTime = report.Metadata.AnalysisTime.TotalSeconds
                    },
                    ["totalMigrations"] = report.Migrations.Count,
                    ["totalBreakingChanges"] = report.BreakingChanges.Count,
                    ["overallRisk"] = report.RiskAssessment.OverallRisk.ToString(),
                    ["highRiskMigrations"] = report.RiskAssessment.MigrationRisks.Count(mr => (int)mr.RiskLevel >= (int)MCPsharp.Models.SqlMigration.RiskLevel.High),
                    ["topRecommendations"] = report.Recommendations.BestPractices.Take(5).ToList(),
                    ["warnings"] = report.Recommendations.Warnings.Take(5).ToList(),
                    ["statistics"] = report.Statistics
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error generating migration report: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteValidateMigrations(JsonDocument arguments, CancellationToken ct)
    {
        if (_sqlMigrationAnalyzer == null)
        {
            return new ToolCallResult { Success = false, Error = "SQL migration analyzer service not available" };
        }

        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();

        try
        {
            var issues = await _sqlMigrationAnalyzer.ValidateMigrationsAsync(projectPath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalIssues"] = issues.Count,
                    ["criticalIssues"] = issues.Count(i => i.Severity == MCPsharp.Models.SqlMigration.Severity.Critical),
                    ["highSeverityIssues"] = issues.Count(i => i.Severity == MCPsharp.Models.SqlMigration.Severity.High),
                    ["mediumSeverityIssues"] = issues.Count(i => i.Severity == MCPsharp.Models.SqlMigration.Severity.Medium),
                    ["lowSeverityIssues"] = issues.Count(i => i.Severity == MCPsharp.Models.SqlMigration.Severity.Low),
                    ["issues"] = issues.Take(20).Select(i => new
                    {
                        type = i.Type,
                        severity = i.Severity.ToString(),
                        message = i.Message,
                        migrationFile = i.MigrationFile,
                        recommendation = i.Recommendation
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error validating migrations: {ex.Message}" };
        }
    }

    #endregion

    #region Phase 3 Large File Optimizer Execution Methods

    private async Task<ToolCallResult> ExecuteAnalyzeLargeFiles(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
        var maxLines = GetIntArgument(arguments, "maxLines");

        try
        {
            var result = await _largeFileOptimizer.AnalyzeLargeFilesAsync(projectPath, maxLines, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalFilesAnalyzed"] = result.TotalFilesAnalyzed,
                    ["filesAboveThreshold"] = result.FilesAboveThreshold,
                    ["averageFileSize"] = result.AverageFileSize,
                    ["largestFileSize"] = result.LargestFileSize,
                    ["analysisTime"] = result.AnalysisDuration.TotalSeconds,
                    ["largeFiles"] = result.LargeFiles.Take(10).Select(f => new
                    {
                        filePath = f.FilePath,
                        lineCount = f.LineCount,
                        sizeCategory = f.SizeCategory.ToString(),
                        largeClassCount = f.LargeClasses.Count,
                        largeMethodCount = f.LargeMethods.Count,
                        codeSmellCount = f.CodeSmells.Count,
                        optimizationPriority = f.OptimizationPriority,
                        immediateActions = f.ImmediateActions.Take(3).ToList()
                    }).ToList(),
                    ["fileSizeDistribution"] = result.FileSizeDistribution,
                    ["statistics"] = new
                    {
                        filesNeedingOptimization = result.Statistics.FilesNeedingOptimization,
                        classesNeedingSplitting = result.Statistics.ClassesNeedingSplitting,
                        methodsNeedingRefactoring = result.Statistics.MethodsNeedingRefactoring,
                        totalCodeSmells = result.Statistics.TotalCodeSmells
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error analyzing large files: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteOptimizeLargeClass(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var filePath = GetStringArgument(arguments, "filePath");
        if (string.IsNullOrEmpty(filePath))
        {
            return new ToolCallResult { Success = false, Error = "filePath parameter is required" };
        }

        try
        {
            var result = await _largeFileOptimizer.OptimizeLargeClassAsync(filePath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["className"] = result.ClassName,
                    ["filePath"] = result.FilePath,
                    ["priority"] = result.Priority.ToString(),
                    ["estimatedEffortHours"] = result.EstimatedEffortHours,
                    ["expectedBenefit"] = result.ExpectedBenefit,
                    ["currentMetrics"] = new
                    {
                        lineCount = result.CurrentMetrics.LineCount,
                        methodCount = result.CurrentMetrics.MethodCount,
                        propertyCount = result.CurrentMetrics.PropertyCount,
                        fieldCount = result.CurrentMetrics.FieldCount,
                        responsibilities = result.CurrentMetrics.Responsibilities.Count,
                        dependencies = result.CurrentMetrics.Dependencies.Count,
                        godClassScore = result.CurrentMetrics.GodClassScore.GodClassScore,
                        isTooLarge = result.CurrentMetrics.IsTooLarge
                    },
                    ["splittingStrategies"] = result.SplittingStrategies.Take(5).Select(s => new
                    {
                        strategyName = s.StrategyName,
                        description = s.Description,
                        newClassName = s.NewClassName,
                        splitType = s.SplitType.ToString(),
                        confidence = s.Confidence,
                        estimatedEffort = s.EstimatedEffortHours,
                        pros = s.Pros.Take(3).ToList(),
                        cons = s.Cons.Take(3).ToList()
                    }).ToList(),
                    ["recommendedActions"] = result.RecommendedActions.Take(5).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error optimizing large class: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteOptimizeLargeMethod(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var filePath = GetStringArgument(arguments, "filePath");
        var methodName = GetStringArgument(arguments, "methodName");

        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(methodName))
        {
            return new ToolCallResult { Success = false, Error = "filePath and methodName parameters are required" };
        }

        try
        {
            var result = await _largeFileOptimizer.OptimizeLargeMethodAsync(filePath, methodName, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["methodName"] = result.MethodName,
                    ["className"] = result.ClassName,
                    ["filePath"] = result.FilePath,
                    ["priority"] = result.Priority.ToString(),
                    ["estimatedEffortHours"] = result.EstimatedEffortHours,
                    ["expectedBenefit"] = result.ExpectedBenefit,
                    ["currentMetrics"] = new
                    {
                        lineCount = result.CurrentMetrics.LineCount,
                        parameterCount = result.CurrentMetrics.ParameterCount,
                        localVariableCount = result.CurrentMetrics.LocalVariableCount,
                        loopCount = result.CurrentMetrics.LoopCount,
                        conditionalCount = result.CurrentMetrics.ConditionalCount,
                        cyclomaticComplexity = result.CurrentMetrics.Complexity.CyclomaticComplexity,
                        cognitiveComplexity = result.CurrentMetrics.Complexity.CognitiveComplexity,
                        isTooLarge = result.CurrentMetrics.IsTooLarge,
                        isTooComplex = result.CurrentMetrics.IsTooComplex
                    },
                    ["refactoringStrategies"] = result.RefactoringStrategies.Take(5).Select(s => new
                    {
                        strategyName = s.StrategyName,
                        description = s.Description,
                        refactoringType = s.RefactoringType.ToString(),
                        confidence = s.Confidence,
                        estimatedEffort = s.EstimatedEffortHours,
                        pros = s.Pros.Take(3).ToList(),
                        cons = s.Cons.Take(3).ToList()
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error optimizing large method: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteGetComplexityMetrics(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var filePath = GetStringArgument(arguments, "filePath");
        var methodName = GetStringArgument(arguments, "methodName");

        if (string.IsNullOrEmpty(filePath))
        {
            return new ToolCallResult { Success = false, Error = "filePath parameter is required" };
        }

        try
        {
            var metrics = await _largeFileOptimizer.GetComplexityMetricsAsync(filePath, methodName, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["cyclomaticComplexity"] = metrics.CyclomaticComplexity,
                    ["cognitiveComplexity"] = metrics.CognitiveComplexity,
                    ["halsteadVolume"] = metrics.HalsteadVolume,
                    ["halsteadDifficulty"] = metrics.HalsteadDifficulty,
                    ["maintainabilityIndex"] = metrics.MaintainabilityIndex,
                    ["maximumNestingDepth"] = metrics.MaximumNestingDepth,
                    ["numberOfDecisionPoints"] = metrics.NumberOfDecisionPoints,
                    ["complexityLevel"] = metrics.ComplexityLevel.ToString(),
                    ["hotspots"] = metrics.Hotspots.Take(5).Select(h => new
                    {
                        startLine = h.StartLine,
                        endLine = h.EndLine,
                        hotspotType = h.HotspotType,
                        localComplexity = h.LocalComplexity,
                        description = h.Description,
                        suggestion = h.Suggestion
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error getting complexity metrics: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteGenerateOptimizationPlan(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var filePath = GetStringArgument(arguments, "filePath");
        if (string.IsNullOrEmpty(filePath))
        {
            return new ToolCallResult { Success = false, Error = "filePath parameter is required" };
        }

        try
        {
            var plan = await _largeFileOptimizer.GenerateOptimizationPlanAsync(filePath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["filePath"] = plan.FilePath,
                    ["overallPriority"] = plan.OverallPriority.ToString(),
                    ["totalEstimatedEffortHours"] = plan.TotalEstimatedEffortHours,
                    ["totalExpectedBenefit"] = plan.TotalExpectedBenefit,
                    ["totalActions"] = plan.Actions.Count,
                    ["actions"] = plan.Actions.Take(10).Select(a => new
                    {
                        title = a.Title,
                        description = a.Description,
                        actionType = a.ActionType.ToString(),
                        priority = a.Priority,
                        estimatedEffort = a.EstimatedEffortHours,
                        expectedBenefit = a.ExpectedBenefit,
                        isRecommended = a.IsRecommended
                    }).ToList(),
                    ["prerequisites"] = plan.Prerequisites.Take(5).ToList(),
                    ["risks"] = plan.Risks.Take(5).ToList(),
                    ["recommendations"] = plan.Recommendations.Take(5).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error generating optimization plan: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteDetectGodClasses(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
        var filePath = GetStringArgument(arguments, "filePath");

        try
        {
            var godClasses = await _largeFileOptimizer.DetectGodClassesAsync(projectPath, filePath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalGodClasses"] = godClasses.Count,
                    ["criticalGodClasses"] = godClasses.Count(gc => gc.Severity == GodClassSeverity.Critical),
                    ["highSeverityGodClasses"] = godClasses.Count(gc => gc.Severity == GodClassSeverity.High),
                    ["godClasses"] = godClasses.Take(10).Select(gc => new
                    {
                        className = gc.ClassName,
                        filePath = gc.FilePath,
                        godClassScore = gc.GodClassScore,
                        severity = gc.Severity.ToString(),
                        responsibilityCount = gc.Responsibilities.Count,
                        violations = gc.Violations.Take(3).ToList(),
                        recommendedSplits = gc.RecommendedSplits.Take(3).Select(s => new
                        {
                            strategyName = s.StrategyName,
                            newClassName = s.NewClassName,
                            confidence = s.Confidence
                        }).ToList()
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error detecting god classes: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteDetectGodMethods(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
        var filePath = GetStringArgument(arguments, "filePath");

        try
        {
            var godMethods = await _largeFileOptimizer.DetectGodMethodsAsync(projectPath, filePath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalGodMethods"] = godMethods.Count,
                    ["criticalGodMethods"] = godMethods.Count(gm => gm.Severity == GodMethodSeverity.Critical),
                    ["highSeverityGodMethods"] = godMethods.Count(gm => gm.Severity == GodMethodSeverity.High),
                    ["godMethods"] = godMethods.Take(10).Select(gm => new
                    {
                        methodName = gm.MethodName,
                        className = gm.ClassName,
                        filePath = gm.FilePath,
                        godMethodScore = gm.GodMethodScore,
                        severity = gm.Severity.ToString(),
                        violations = gm.Violations.Take(3).ToList(),
                        recommendedRefactorings = gm.RecommendedRefactorings.Take(3).Select(r => new
                        {
                            strategyName = r.StrategyName,
                            refactoringType = r.RefactoringType.ToString(),
                            confidence = r.Confidence
                        }).ToList()
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error detecting god methods: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeCodeSmells(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var filePath = GetStringArgument(arguments, "filePath");
        if (string.IsNullOrEmpty(filePath))
        {
            return new ToolCallResult { Success = false, Error = "filePath parameter is required" };
        }

        try
        {
            var codeSmells = await _largeFileOptimizer.AnalyzeCodeSmellsAsync(filePath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalCodeSmells"] = codeSmells.Count,
                    ["blockerSmells"] = codeSmells.Count(cs => cs.Severity == CodeSmellSeverity.Blocker),
                    ["criticalSmells"] = codeSmells.Count(cs => cs.Severity == CodeSmellSeverity.Critical),
                    ["majorSmells"] = codeSmells.Count(cs => cs.Severity == CodeSmellSeverity.Major),
                    ["codeSmells"] = codeSmells.Take(20).Select(cs => new
                    {
                        smellType = cs.SmellType,
                        description = cs.Description,
                        severity = cs.Severity.ToString(),
                        startLine = cs.StartLine,
                        endLine = cs.EndLine,
                        impactScore = cs.ImpactScore,
                        suggestion = cs.Suggestion,
                        refactoringPatterns = cs.RefactoringPatterns.Take(2).Select(rp => new
                        {
                            patternName = rp.PatternName,
                            description = rp.Description,
                            applicability = rp.Applicability
                        }).ToList()
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error analyzing code smells: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteGetOptimizationRecommendations(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var filePath = GetStringArgument(arguments, "filePath");
        if (string.IsNullOrEmpty(filePath))
        {
            return new ToolCallResult { Success = false, Error = "filePath parameter is required" };
        }

        try
        {
            // First analyze code smells
            var codeSmells = await _largeFileOptimizer.AnalyzeCodeSmellsAsync(filePath, ct);

            // Then suggest refactoring patterns
            var suggestions = await _largeFileOptimizer.SuggestRefactoringPatternsAsync(filePath, codeSmells, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalRecommendations"] = suggestions.Count,
                    ["highConfidenceRecommendations"] = suggestions.Count(s => s.Confidence > 0.8),
                    ["lowEffortRecommendations"] = suggestions.Count(s => s.EstimatedEffortHours <= 2),
                    ["recommendations"] = suggestions.Take(10).Select(s => new
                    {
                        title = s.Title,
                        description = s.Description,
                        refactoringType = s.Type.ToString(),
                        filePath = s.FilePath,
                        startLine = s.StartLine,
                        endLine = s.EndLine,
                        confidence = s.Confidence,
                        estimatedEffort = s.EstimatedEffortHours,
                        benefits = s.Benefits.Take(3).ToList(),
                        risks = s.Risks.Take(3).ToList()
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error getting optimization recommendations: {ex.Message}" };
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

    private string? GetStringArgument(JsonDocument arguments, string parameterName)
    {
        if (arguments.RootElement.TryGetProperty(parameterName, out var propertyElement))
        {
            return propertyElement.ValueKind == JsonValueKind.String ? propertyElement.GetString() : propertyElement.ToString();
        }
        return null;
    }

    private bool? GetBoolArgument(JsonDocument arguments, string parameterName)
    {
        if (arguments.RootElement.TryGetProperty(parameterName, out var propertyElement))
        {
            if (propertyElement.ValueKind == JsonValueKind.True || propertyElement.ValueKind == JsonValueKind.False)
            {
                return propertyElement.GetBoolean();
            }
            if (propertyElement.ValueKind == JsonValueKind.String &&
                bool.TryParse(propertyElement.GetString(), out var boolValue))
            {
                return boolValue;
            }
        }
        return null;
    }

    private int? GetIntArgument(JsonDocument arguments, string parameterName)
    {
        if (arguments.RootElement.TryGetProperty(parameterName, out var propertyElement))
        {
            if (propertyElement.ValueKind == JsonValueKind.Number && propertyElement.TryGetInt32(out var intValue))
            {
                return intValue;
            }
            if (propertyElement.ValueKind == JsonValueKind.String &&
                int.TryParse(propertyElement.GetString(), out var parsedValue))
            {
                return parsedValue;
            }
        }
        return null;
    }

    private double? GetDoubleArgument(JsonDocument arguments, string parameterName)
    {
        if (arguments.RootElement.TryGetProperty(parameterName, out var propertyElement))
        {
            if (propertyElement.ValueKind == JsonValueKind.Number && propertyElement.TryGetDouble(out var doubleValue))
            {
                return doubleValue;
            }
            if (propertyElement.ValueKind == JsonValueKind.String &&
                double.TryParse(propertyElement.GetString(), out var parsedValue))
            {
                return parsedValue;
            }
        }
        return null;
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
        if (element.TryGetProperty(propertyName, out var propertyElement))
        {
            return propertyElement;
        }
        return null;
    }
}
