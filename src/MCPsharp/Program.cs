using MCPsharp.Services;
using MCPsharp.Services.Phase2;
using MCPsharp.Services.Phase3;
using MCPsharp.Services.Consolidated;
using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging;

namespace MCPsharp;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            // Parse command line arguments
            var workspaceRoot = args.Length > 0 ? args[0] : Environment.CurrentDirectory;

            // Validate workspace exists
            if (!Directory.Exists(workspaceRoot))
            {
                await Console.Error.WriteLineAsync($"Error: Directory not found: {workspaceRoot}");
                return 1;
            }

            // Setup logging (to stderr, not stdout - stdout is for MCP protocol)
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole(options =>
                {
                    options.LogToStandardErrorThreshold = LogLevel.Trace; // Log to stderr
                });
                builder.SetMinimumLevel(LogLevel.Warning);
            });

            var logger = loggerFactory.CreateLogger<Program>();
            logger.LogInformation("MCPsharp starting, workspace: {Workspace}", workspaceRoot);

            // Initialize all services
            var projectManager = new ProjectContextManager();
            var roslynWorkspace = new RoslynWorkspace();

            // Initialize core file operations service first (needed by consolidated services)
            var fileOperations = new FileOperationsService(workspaceRoot);

            // Initialize Roslyn Analyzer infrastructure
            var analyzerLoader = new MCPsharp.Services.Analyzers.RoslynAnalyzerLoader(
                loggerFactory?.CreateLogger<MCPsharp.Services.Analyzers.RoslynAnalyzerLoader>() ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MCPsharp.Services.Analyzers.RoslynAnalyzerLoader>.Instance,
                loggerFactory);

            var analyzerRegistry = new MCPsharp.Services.Analyzers.AnalyzerRegistry(
                loggerFactory?.CreateLogger<MCPsharp.Services.Analyzers.AnalyzerRegistry>() ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MCPsharp.Services.Analyzers.AnalyzerRegistry>.Instance);

            // Initialize auto-load service with default configuration
            var autoLoadService = new MCPsharp.Services.Analyzers.AnalyzerAutoLoadService(
                loggerFactory?.CreateLogger<MCPsharp.Services.Analyzers.AnalyzerAutoLoadService>() ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MCPsharp.Services.Analyzers.AnalyzerAutoLoadService>.Instance,
                analyzerLoader,
                analyzerRegistry);

            // Initialize built-in code fix registry (auto-registers built-in providers)
            var codeFixRegistry = new MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry.BuiltInCodeFixRegistry(
                loggerFactory?.CreateLogger<MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry.BuiltInCodeFixRegistry>() ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry.BuiltInCodeFixRegistry>.Instance,
                loggerFactory);

            // Initialize analyzer host dependencies
            var securityManager = new MCPsharp.Services.Analyzers.SecurityManager(
                loggerFactory?.CreateLogger<MCPsharp.Services.Analyzers.SecurityManager>() ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MCPsharp.Services.Analyzers.SecurityManager>.Instance,
                workspaceRoot);

            var fixEngine = new MCPsharp.Services.Analyzers.Fixes.FixEngine(
                loggerFactory?.CreateLogger<MCPsharp.Services.Analyzers.Fixes.FixEngine>() ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MCPsharp.Services.Analyzers.Fixes.FixEngine>.Instance,
                fileOperations);

            var sandboxFactory = new MCPsharp.Services.Analyzers.DefaultAnalyzerSandboxFactory(
                loggerFactory,
                securityManager);

            // Initialize analyzer host
            var analyzerHost = new MCPsharp.Services.Analyzers.AnalyzerHost(
                loggerFactory?.CreateLogger<MCPsharp.Services.Analyzers.AnalyzerHost>() ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MCPsharp.Services.Analyzers.AnalyzerHost>.Instance,
                loggerFactory,
                analyzerRegistry,
                securityManager,
                fixEngine,
                sandboxFactory);

            // Initialize RoslynAnalyzerService with auto-load support
            var roslynAnalyzerService = new MCPsharp.Services.Analyzers.RoslynAnalyzerService(
                analyzerLoader,
                analyzerHost,
                loggerFactory?.CreateLogger<MCPsharp.Services.Analyzers.RoslynAnalyzerService>() ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MCPsharp.Services.Analyzers.RoslynAnalyzerService>.Instance,
                autoLoadService);

            // Phase 2 services that can be instantiated immediately
            var configAnalyzer = new MCPsharp.Services.ConfigAnalyzerService(
              loggerFactory?.CreateLogger<MCPsharp.Services.ConfigAnalyzerService>() ??
              Microsoft.Extensions.Logging.Abstractions.NullLogger<MCPsharp.Services.ConfigAnalyzerService>.Instance);
            var workflowAnalyzer = new MCPsharp.Services.Phase2.WorkflowAnalyzerService();

            // Supporting services
            var bulkEditService = new BulkEditService(loggerFactory?.CreateLogger<BulkEditService>());
            var progressTracker = new ProgressTrackerService(loggerFactory?.CreateLogger<ProgressTrackerService>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProgressTrackerService>.Instance);
            var tempFileManager = new TempFileManagerService();
            var streamingProcessor = new StreamingFileProcessor(
                loggerFactory?.CreateLogger<StreamingFileProcessor>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<StreamingFileProcessor>.Instance,
                progressTracker,
                tempFileManager
            );

            // Initialize consolidated services
            var universalFileOps = new UniversalFileOperations(
                workspaceRoot,
                fileOperations,
                bulkEditService,
                loggerFactory?.CreateLogger<UniversalFileOperations>()
            );

            var unifiedAnalysis = new UnifiedAnalysisService(
                workspace: roslynWorkspace,
                logger: loggerFactory?.CreateLogger<UnifiedAnalysisService>()
            );

            var bulkOperationsHub = new BulkOperationsHub(
                bulkEditService: bulkEditService,
                fileOps: universalFileOps,
                progressTracker: progressTracker,
                tempFileManager: tempFileManager,
                logger: loggerFactory?.CreateLogger<BulkOperationsHub>()
            );

            var streamController = new StreamProcessingController(
                streamingProcessor: streamingProcessor,
                progressTracker: progressTracker,
                tempFileManager: tempFileManager,
                logger: loggerFactory?.CreateLogger<StreamProcessingController>()
            );

            // Create tool registry with all Phase 2 services and consolidated services
            // Note: ImpactAnalyzerService and FeatureTracerService require runtime dependencies
            // and will be instantiated lazily by the McpToolRegistry
            var toolRegistry = new McpToolRegistry(
                projectManager,
                roslynWorkspace,
                workflowAnalyzer: workflowAnalyzer,
                configAnalyzer: configAnalyzer,
                impactAnalyzer: null, // Will be created lazily due to ReferenceFinderService dependency
                featureTracer: null, // Will be created lazily due to FileOperationsService dependency
                bulkEditService: bulkEditService,
                streamingProcessor: streamingProcessor,
                progressTracker: progressTracker,
                tempFileManager: tempFileManager,
                fileOperationsService: fileOperations,
                universalFileOps: universalFileOps,
                unifiedAnalysis: unifiedAnalysis,
                bulkOperationsHub: bulkOperationsHub,
                streamController: streamController,
                roslynAnalyzerService: roslynAnalyzerService,
                codeFixRegistry: codeFixRegistry,
                loggerFactory: loggerFactory
            );

            logger.LogInformation("MCPsharp initialized with {ToolCount} tools", toolRegistry.GetTools().Count);

            // Create and run JSON-RPC handler
            var handler = new JsonRpcHandler(Console.In, Console.Out, toolRegistry, logger);

            // Setup cancellation on Ctrl+C
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                logger.LogInformation("MCPsharp shutting down...");
            };

            // Run MCP server loop
            await handler.RunAsync(cts.Token);

            logger.LogInformation("MCPsharp stopped");
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Fatal error: {ex.Message}");
            await Console.Error.WriteLineAsync(ex.StackTrace);
            return 1;
        }
    }
}
