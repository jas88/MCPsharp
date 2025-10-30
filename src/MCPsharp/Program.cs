using MCPsharp.Services;
using MCPsharp.Services.Phase2;
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

            // Create tool registry with all Phase 2 services
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
                loggerFactory: loggerFactory
            );

            logger.LogInformation("MCPsharp initialized with {ToolCount} tools", toolRegistry.GetTools().Count);

            // Create and run JSON-RPC handler
            var handler = new JsonRpcHandler(Console.In, Console.Out, toolRegistry, logger);

            // Setup cancellation on Ctrl+C
            var cts = new CancellationTokenSource();
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
