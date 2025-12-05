using MCPsharp.Services;
using MCPsharp.Services.Phase2;
using MCPsharp.Services.Phase3;
using MCPsharp.Services.Consolidated;
using MCPsharp.Services.Roslyn;
using MCPsharp.Services.AI;
using MCPsharp.Services.Database;
using MCPsharp.Models;
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

            // Database initialization
            var projectDatabase = new ProjectDatabase(loggerFactory?.CreateLogger<ProjectDatabase>());

            // Resource and prompt registries
            var resourceRegistry = new McpResourceRegistry(loggerFactory?.CreateLogger<McpResourceRegistry>());
            var promptRegistry = new McpPromptRegistry(loggerFactory?.CreateLogger<McpPromptRegistry>());

            // Initialize all services
            var projectManager = new ProjectContextManager();
            var roslynWorkspace = new RoslynWorkspace();

            // Auto-load project if current directory is a C# project
            await AutoLoadProjectAsync(workspaceRoot, projectManager, roslynWorkspace, projectDatabase, logger);

            // Initialize core file operations service first (needed by consolidated services)
            var fileOperations = new FileOperationsService(workspaceRoot);

            // Initialize Roslyn Analyzer infrastructure
            var analyzerLoader = new MCPsharp.Services.Analyzers.RoslynAnalyzerLoader(
                loggerFactory?.CreateLogger<MCPsharp.Services.Analyzers.RoslynAnalyzerLoader>() ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MCPsharp.Services.Analyzers.RoslynAnalyzerLoader>.Instance,
                loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

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
                loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

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
                loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
                securityManager);

            // Initialize analyzer host
            var analyzerHost = new MCPsharp.Services.Analyzers.AnalyzerHost(
                loggerFactory?.CreateLogger<MCPsharp.Services.Analyzers.AnalyzerHost>() ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MCPsharp.Services.Analyzers.AnalyzerHost>.Instance,
                loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
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

            // Initialize AI services (optional - gracefully degrades if unavailable)
            var aiProviderFactory = new AIProviderFactory(null, loggerFactory.CreateLogger<AIProviderFactory>());
            var aiProvider = await aiProviderFactory.CreateAsync();

            CodebaseQueryService? codebaseQuery = null;
            AICodeTransformationService? aiCodeTransformation = null;

            if (aiProvider != null)
            {
                codebaseQuery = new CodebaseQueryService(
                    aiProvider,
                    projectManager,
                    loggerFactory.CreateLogger<CodebaseQueryService>()
                );

                aiCodeTransformation = new AICodeTransformationService(
                    aiProvider,
                    roslynWorkspace,
                    loggerFactory.CreateLogger<AICodeTransformationService>()
                );

                logger.LogInformation("AI-powered tools enabled with {Provider}/{Model}",
                    aiProvider.ProviderName, aiProvider.ModelName);
                logger.LogInformation("AI code transformation tools enabled (Roslyn AST-based)");
            }
            else
            {
                logger.LogInformation("AI-powered tools disabled (no provider available)");
            }

            // Setup resource content generators
            var resourceGenerators = new ResourceContentGenerators(projectManager, () => projectDatabase);

            // Register MCP resources
            resourceRegistry.RegisterResource(
                new McpResource { Uri = "project://overview", Name = "Project Overview", Description = "Overview of the current project", MimeType = "text/markdown" },
                () => resourceGenerators.GenerateOverview());

            resourceRegistry.RegisterResource(
                new McpResource { Uri = "project://structure", Name = "Project Structure", Description = "File and folder structure", MimeType = "text/markdown" },
                () => resourceGenerators.GenerateStructure());

            resourceRegistry.RegisterResource(
                new McpResource { Uri = "project://dependencies", Name = "Dependencies", Description = "Project dependencies", MimeType = "text/markdown" },
                () => resourceGenerators.GenerateDependencies());

            resourceRegistry.RegisterResource(
                new McpResource { Uri = "project://symbols", Name = "Symbols", Description = "Symbol summary", MimeType = "text/markdown" },
                () => resourceGenerators.GenerateSymbolsSummary());

            resourceRegistry.RegisterResource(
                new McpResource { Uri = "project://guidance", Name = "Usage Guide", Description = "Best practices for using MCPsharp", MimeType = "text/markdown" },
                () => resourceGenerators.GenerateGuidance());

            logger.LogInformation("Registered {Count} MCP resources", 5);

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
                loggerFactory: loggerFactory,
                codebaseQuery: codebaseQuery,
                aiCodeTransformation: aiCodeTransformation
            );

            logger.LogInformation("MCPsharp initialized with {ToolCount} tools", toolRegistry.GetTools().Count);

            // Create and run JSON-RPC handler
            var handler = new JsonRpcHandler(
                Console.In,
                Console.Out,
                toolRegistry,
                logger,
                resourceRegistry,
                promptRegistry);

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

            // Cleanup
            await projectDatabase.DisposeAsync();

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

    /// <summary>
    /// Auto-load project if the workspace directory contains C# project files.
    /// </summary>
    private static async Task AutoLoadProjectAsync(
        string workspaceRoot,
        ProjectContextManager projectManager,
        RoslynWorkspace roslynWorkspace,
        ProjectDatabase projectDatabase,
        ILogger logger)
    {
        try
        {
            // Look for .csproj or .sln files in the workspace
            var csprojFiles = Directory.GetFiles(workspaceRoot, "*.csproj", SearchOption.TopDirectoryOnly);
            var slnFiles = Directory.GetFiles(workspaceRoot, "*.sln", SearchOption.TopDirectoryOnly);

            string? projectPath = null;

            // Prefer .sln files if present
            if (slnFiles.Length > 0)
            {
                projectPath = slnFiles[0];
                if (slnFiles.Length > 1)
                {
                    logger.LogInformation("Found {Count} solution files, auto-loading: {Path}",
                        slnFiles.Length, Path.GetFileName(projectPath));
                }
            }
            else if (csprojFiles.Length > 0)
            {
                projectPath = csprojFiles[0];
                if (csprojFiles.Length > 1)
                {
                    logger.LogInformation("Found {Count} project files, auto-loading: {Path}",
                        csprojFiles.Length, Path.GetFileName(projectPath));
                }
            }

            if (projectPath != null)
            {
                logger.LogInformation("Auto-loading C# project: {Project}", Path.GetFileName(projectPath));

                // Load directory into project manager (expects directory, not file)
                projectManager.OpenProject(workspaceRoot);

                // Initialize Roslyn workspace with project/solution file
                await roslynWorkspace.InitializeAsync(projectPath);

                // Initialize database for the project
                try
                {
                    await projectDatabase.OpenOrCreateAsync(workspaceRoot);
                    logger.LogInformation("Opened project database");
                }
                catch (Exception dbEx)
                {
                    logger.LogWarning(dbEx, "Failed to open project database, continuing without cache");
                }

                logger.LogInformation("Project auto-loaded successfully");
            }
            else
            {
                logger.LogInformation("No C# project files found in workspace - project tools available on demand");
            }
        }
        catch (Exception ex)
        {
            // Don't fail startup if auto-load fails - just log it
            logger.LogWarning(ex, "Failed to auto-load project, continuing without pre-loaded project");
        }
    }
}
