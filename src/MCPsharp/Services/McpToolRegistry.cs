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

    // Hybrid search service with graceful degradation
    private HybridSearchService? _hybridSearch;

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
            var context = _projectContext.GetProjectContext();
            if (context == null)
                throw new InvalidOperationException("Project context is not available");
            if (string.IsNullOrEmpty(context.RootPath))
                throw new InvalidOperationException("Project root path is not available");

            if (!_workspace.IsInitialized)
            {
                await _workspace.InitializeAsync(context.RootPath);

                // Initialize services
                _symbolQuery = new SymbolQueryService(_workspace);
                _classStructure = new ClassStructureService(_workspace);
                _semanticEdit = new SemanticEditService(_workspace, _classStructure);
                _referenceFinder = new ReferenceFinderService(_workspace);
                _projectParser = new ProjectParserService();

                // Initialize hybrid search service with graceful degradation
                var logger = _loggerFactory?.CreateLogger<HybridSearchService>()
                    ?? LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HybridSearchService>();
                _hybridSearch = new HybridSearchService(_workspace, _symbolQuery, logger);

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

                // Initialize hybrid search service with graceful degradation
                var logger = _loggerFactory?.CreateLogger<HybridSearchService>()
                    ?? LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HybridSearchService>();
                _hybridSearch = new HybridSearchService(_workspace, _symbolQuery, logger);

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

        var name = arguments.RootElement.GetProperty("name").GetString();
        if (string.IsNullOrEmpty(name))
        {
            return new ToolCallResult { Success = false, Error = "Name is required" };
        }

        // Use hybrid search with graceful degradation
        if (_hybridSearch != null && _workspace != null)
        {
            var searchResult = await _hybridSearch.FindSymbolAsync(name, SearchStrategy.Auto);

            if (!searchResult.Success)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "All search methods failed. " + string.Join("; ", searchResult.Warnings)
                };
            }

            // Get workspace health for capability report
            var health = _workspace.GetHealth();

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    Symbols = searchResult.Symbols,
                    TotalResults = searchResult.Symbols.Count,

                    // Metadata about search method used
                    metadata = new
                    {
                        method = searchResult.Method,
                        confidence = searchResult.Confidence,
                        warnings = searchResult.Warnings
                    },

                    // Workspace capability status
                    capabilities = health.GetCapabilitySummary()
                }
            };
        }

        // Fallback to legacy behavior if hybrid search not available
        if (_symbolQuery == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
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
}
