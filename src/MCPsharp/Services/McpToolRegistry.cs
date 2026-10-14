using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services.AI;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry;
using MCPsharp.Services.Consolidated;
using MCPsharp.Services.Phase3;
using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services;

/// <summary>
/// Registry and executor for MCP tools
/// </summary>
public partial class McpToolRegistry
{
    private readonly ProjectContextManager _projectContext;
    private readonly List<McpTool> _tools;
    private readonly Dictionary<string, Func<JsonDocument, CancellationToken, Task<ToolCallResult>>> _toolHandlers;
    private FileOperationsService? _fileOperations;
    private RoslynWorkspace? _workspace;
    private SymbolQueryService? _symbolQuery;
    private ClassStructureService? _classStructure;
    private SemanticEditService? _semanticEdit;
    private ReferenceFinderService? _referenceFinder;

    // Advanced reverse search services
    private ICallerAnalysisService? _callerAnalysis;
    private ICallChainService? _callChain;
    private ITypeUsageService? _typeUsage;
    private AdvancedReferenceFinderService? _advancedReferenceFinder;
    private RenameSymbolService? _renameSymbol;

    // Phase 3 services
    private ArchitectureValidatorService? _architectureValidator;
    private DuplicateCodeDetectorService? _duplicateCodeDetector;
    private ISqlMigrationAnalyzerService? _sqlMigrationAnalyzer;
    private ILargeFileOptimizerService? _largeFileOptimizer;
    private readonly bool _largeFileOptimizerSupplied;

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

    // Consolidated services. File, analysis and bulk services are rebound to the project
    // root (and project-scoped Roslyn services) whenever a project is opened.
    private UniversalFileOperations? _universalFileOps;
    private UnifiedAnalysisService? _unifiedAnalysis;
    private BulkOperationsHub? _bulkOperationsHub;
    private readonly StreamProcessingController? _streamController;

    // Static JSON schema fragments
    private static readonly string[] MethodParameterRequiredProperties = { "name", "type" };
    private static readonly Dictionary<string, object> MethodParameterItems = new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            ["name"] = new Dictionary<string, object> { ["type"] = "string" },
            ["type"] = new Dictionary<string, object> { ["type"] = "string" }
        },
        ["required"] = MethodParameterRequiredProperties
    };
    private static readonly string[] EditOperationTypes = { "replace", "insert", "delete" };
    private static readonly string[] EditRequiredProperties = { "type" };

    // Response processing for token limiting
    private readonly ResponseProcessor _responseProcessor;
    private readonly ILoggerFactory? _loggerFactory;

    // AI-powered tools
    private readonly CodebaseQueryService? _codebaseQuery;
    private readonly AICodeTransformationService? _aiCodeTransformation;
    private readonly ILogger _logger;

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
        UniversalFileOperations? universalFileOps = null,
        UnifiedAnalysisService? unifiedAnalysis = null,
        BulkOperationsHub? bulkOperationsHub = null,
        StreamProcessingController? streamController = null,
        IRoslynAnalyzerService? roslynAnalyzerService = null,
        BuiltInCodeFixRegistry? codeFixRegistry = null,
        ISearchService? searchService = null,
        ILoggerFactory? loggerFactory = null,
        CodebaseQueryService? codebaseQuery = null,
        AICodeTransformationService? aiCodeTransformation = null)
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
        _largeFileOptimizerSupplied = largeFileOptimizer != null;

        // Consolidated services
        _universalFileOps = universalFileOps;
        _unifiedAnalysis = unifiedAnalysis;
        _bulkOperationsHub = bulkOperationsHub;
        _streamController = streamController;
        _roslynAnalyzerService = roslynAnalyzerService;
        _searchService = searchService;
        _loggerFactory = loggerFactory;
        _codebaseQuery = codebaseQuery;
        _aiCodeTransformation = aiCodeTransformation;
        _logger = (ILogger?)loggerFactory?.CreateLogger<McpToolRegistry>() ?? NullLogger.Instance;

        // Initialize response processor with configuration
        var responseConfig = ResponseConfiguration.LoadFromEnvironment();
        var responseLogger = loggerFactory?.CreateLogger<ResponseProcessor>();
        _responseProcessor = new ResponseProcessor(responseConfig, responseLogger);

        _toolHandlers = CreateToolHandlers();
        _tools = RegisterTools();

        // Register consolidated and Roslyn analyzer tools for the services that were supplied
        RegisterConsolidatedTools();
        RegisterRoslynAnalyzerTools();
        RegisterBulkStreamAndCodeFixTools();
        RegisterPhase3Tools();

        // Register AI-powered tools if available
        RegisterAITools();

        // A project opened before the registry was created (Program auto-loads the startup directory)
        // gets its project-scoped services now rather than only after an explicit project_open.
        var openRoot = projectContext.GetProjectContext()?.RootPath;
        if (openRoot != null)
        {
            InitializeProjectServices(openRoot);
        }
    }

    /// <summary>
    /// Names of every tool <see cref="ExecuteTool"/> can dispatch, whether or not it is currently listed.
    /// </summary>
    internal IReadOnlyCollection<string> HandledToolNames => _toolHandlers.Keys;

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
            var result = _toolHandlers.TryGetValue(request.Name, out var handler)
                ? await handler(request.Arguments, ct)
                : new ToolCallResult
                {
                    Success = false,
                    Error = $"Unknown tool: {request.Name}"
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
            LogToolExecutionFailed(_logger, ex, request.Name);
            return new ToolCallResult
            {
                Success = false,
                Error = $"Tool execution failed: {ex.Message}"
            };
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool operation {Operation} failed")]
    private static partial void LogToolOperationFailed(ILogger logger, Exception exception, string operation);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool {ToolName} execution failed")]
    private static partial void LogToolExecutionFailed(ILogger logger, Exception exception, string toolName);
}
