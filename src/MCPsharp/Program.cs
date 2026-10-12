using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.AI;
using MCPsharp.Services.Consolidated;
using MCPsharp.Services.Database;
using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging;

namespace MCPsharp;

sealed partial class Program
{
    private Program()
    {
    }

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
            LogMCPsharpStartingWorkspace(logger, workspaceRoot);

            // Database initialization
            await using var projectDatabase = new ProjectDatabase(loggerFactory.CreateLogger<ProjectDatabase>());

            // Resource and prompt registries
            var resourceRegistry = new McpResourceRegistry(loggerFactory.CreateLogger<McpResourceRegistry>());
            var promptRegistry = new McpPromptRegistry(loggerFactory.CreateLogger<McpPromptRegistry>());

            // Initialize all services
            var projectManager = new ProjectContextManager();
            var roslynWorkspace = new RoslynWorkspace();

            // Auto-load project if current directory is a C# project
            await AutoLoadProjectAsync(workspaceRoot, projectManager, roslynWorkspace, projectDatabase, logger);

            // Initialize core file operations service first (needed by consolidated services)
            var fileOperations = new FileOperationsService(workspaceRoot);

            // Initialize Roslyn Analyzer infrastructure
            var analyzerLoader = new MCPsharp.Services.Analyzers.RoslynAnalyzerLoader(
                loggerFactory.CreateLogger<MCPsharp.Services.Analyzers.RoslynAnalyzerLoader>(),
                loggerFactory);

            var analyzerRegistry = new MCPsharp.Services.Analyzers.AnalyzerRegistry(
                loggerFactory.CreateLogger<MCPsharp.Services.Analyzers.AnalyzerRegistry>());

            // Initialize auto-load service with default configuration
            var autoLoadService = new MCPsharp.Services.Analyzers.AnalyzerAutoLoadService(
                loggerFactory.CreateLogger<MCPsharp.Services.Analyzers.AnalyzerAutoLoadService>(),
                analyzerLoader,
                analyzerRegistry);

            // Initialize built-in code fix registry (auto-registers built-in providers)
            var codeFixRegistry = new MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry.BuiltInCodeFixRegistry(
                loggerFactory.CreateLogger<MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry.BuiltInCodeFixRegistry>(),
                loggerFactory);

            // Initialize analyzer host dependencies
            var securityManager = new MCPsharp.Services.Analyzers.SecurityManager(
                loggerFactory.CreateLogger<MCPsharp.Services.Analyzers.SecurityManager>(),
                workspaceRoot);

            var fixEngine = new MCPsharp.Services.Analyzers.Fixes.FixEngine(
                loggerFactory.CreateLogger<MCPsharp.Services.Analyzers.Fixes.FixEngine>(),
                fileOperations);

            var sandboxFactory = new MCPsharp.Services.Analyzers.DefaultAnalyzerSandboxFactory(
                loggerFactory,
                securityManager);

            // Initialize analyzer host
            var analyzerHost = new MCPsharp.Services.Analyzers.AnalyzerHost(
                loggerFactory.CreateLogger<MCPsharp.Services.Analyzers.AnalyzerHost>(),
                loggerFactory,
                analyzerRegistry,
                securityManager,
                fixEngine,
                sandboxFactory);

            // Initialize RoslynAnalyzerService with auto-load support
            var roslynAnalyzerService = new MCPsharp.Services.Analyzers.RoslynAnalyzerService(
                analyzerLoader,
                analyzerHost,
                loggerFactory.CreateLogger<MCPsharp.Services.Analyzers.RoslynAnalyzerService>(),
                autoLoadService);

            // Phase 2 services that can be instantiated immediately
            var configAnalyzer = new MCPsharp.Services.ConfigAnalyzerService(
              loggerFactory.CreateLogger<MCPsharp.Services.ConfigAnalyzerService>());
            var workflowAnalyzer = new MCPsharp.Services.Phase2.WorkflowAnalyzerService();

            // Supporting services
            using var bulkEditService = new BulkEditService(loggerFactory.CreateLogger<BulkEditService>());
            var progressTracker = new ProgressTrackerService(loggerFactory.CreateLogger<ProgressTrackerService>());
            var tempFileManager = new TempFileManagerService();
            var streamingProcessor = new StreamingFileProcessor(
                loggerFactory.CreateLogger<StreamingFileProcessor>(),
                progressTracker,
                tempFileManager
            );

            // Initialize consolidated services
            var universalFileOps = new UniversalFileOperations(
                workspaceRoot,
                fileOperations,
                bulkEditService,
                loggerFactory.CreateLogger<UniversalFileOperations>()
            );

            var unifiedAnalysis = new UnifiedAnalysisService(
                workspace: roslynWorkspace,
                logger: loggerFactory.CreateLogger<UnifiedAnalysisService>()
            );

            var bulkOperationsHub = new BulkOperationsHub(
                bulkEditService: bulkEditService,
                fileOps: universalFileOps,
                progressTracker: progressTracker,
                tempFileManager: tempFileManager,
                logger: loggerFactory.CreateLogger<BulkOperationsHub>()
            );

            var streamController = new StreamProcessingController(
                streamingProcessor: streamingProcessor,
                progressTracker: progressTracker,
                tempFileManager: tempFileManager,
                logger: loggerFactory.CreateLogger<StreamProcessingController>()
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

                LogAIPoweredToolsEnabledWith(logger, aiProvider.ProviderName, aiProvider.ModelName);
                LogAICodeTransformationToolsEnabled(logger);
            }
            else
            {
                LogAIPoweredToolsDisabledNo(logger);
            }

            // Setup resource content generators
            var resourceGenerators = new ResourceContentGenerators(projectManager);

            // Register MCP resources
            resourceRegistry.RegisterResource(
                new McpResource { Uri = ProjectResourceUris.Overview, Name = "Project Overview", Description = "Overview of the current project", MimeType = "text/markdown" },
                () => resourceGenerators.GenerateOverview());

            resourceRegistry.RegisterResource(
                new McpResource { Uri = ProjectResourceUris.Structure, Name = "Project Structure", Description = "File and folder structure", MimeType = "text/markdown" },
                () => resourceGenerators.GenerateStructure());

            resourceRegistry.RegisterResource(
                new McpResource { Uri = ProjectResourceUris.Dependencies, Name = "Dependencies", Description = "Project dependencies", MimeType = "text/markdown" },
                () => resourceGenerators.GenerateDependencies());

            resourceRegistry.RegisterResource(
                new McpResource { Uri = ProjectResourceUris.Symbols, Name = "Symbols", Description = "Symbol summary", MimeType = "text/markdown" },
                () => resourceGenerators.GenerateSymbolsSummary());

            resourceRegistry.RegisterResource(
                new McpResource { Uri = ProjectResourceUris.Guidance, Name = "Usage Guide", Description = "Best practices for using MCPsharp", MimeType = "text/markdown" },
                () => ResourceContentGenerators.GenerateGuidance());

            LogRegisteredMCPResources(logger, 5);

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
                sqlMigrationAnalyzer: new MCPsharp.Services.Phase3.SqlMigrationAnalyzerService(
                    loggerFactory.CreateLogger<MCPsharp.Services.Phase3.SqlMigrationAnalyzerService>(), loggerFactory),
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

            if (logger.IsEnabled(LogLevel.Information))
            {
                var toolCount = toolRegistry.GetTools().Count;
                LogMCPsharpInitializedWithTools(logger, toolCount);
            }

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
                LogMCPsharpShuttingDown(logger);
            };

            // Run MCP server loop
            await handler.RunAsync(cts.Token);

            // projectDatabase will be disposed automatically via await using

            LogMCPsharpStopped(logger);
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Fatal error: {ex}");
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
                if (slnFiles.Length > 1 && logger.IsEnabled(LogLevel.Information))
                {
                    var fileName = Path.GetFileName(projectPath);
                    LogFoundSolutionFilesAutoLoading(logger, slnFiles.Length, fileName);
                }
            }
            else if (csprojFiles.Length > 0)
            {
                projectPath = csprojFiles[0];
                if (csprojFiles.Length > 1 && logger.IsEnabled(LogLevel.Information))
                {
                    var fileName = Path.GetFileName(projectPath);
                    LogFoundProjectFilesAutoLoading(logger, csprojFiles.Length, fileName);
                }
            }

            if (projectPath != null)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    var fileName = Path.GetFileName(projectPath);
                    LogAutoLoadingCSharpProject(logger, fileName);
                }

                // Load directory into project manager (expects directory, not file)
                projectManager.OpenProject(workspaceRoot);

                // Initialize Roslyn workspace with project/solution file
                await roslynWorkspace.InitializeAsync(projectPath);

                // Initialize database for the project
                try
                {
                    await projectDatabase.OpenOrCreateAsync(workspaceRoot);
                    LogOpenedProjectDatabase(logger);
                }
                catch (Exception dbEx)
                {
                    LogFailedToOpenProjectDatabase(logger, dbEx);
                }

                LogProjectAutoLoadedSuccessfully(logger);
            }
            else
            {
                LogNoCProjectFilesFound(logger);
            }
        }
        catch (Exception ex)
        {
            // Don't fail startup if auto-load fails - just log it
            LogFailedToAutoLoadProject(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MCPsharp starting, workspace: {Workspace}")]
    private static partial void LogMCPsharpStartingWorkspace(ILogger logger, string workspace);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI-powered tools enabled with {Provider}/{Model}")]
    private static partial void LogAIPoweredToolsEnabledWith(ILogger logger, string provider, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI code transformation tools enabled (Roslyn AST-based)")]
    private static partial void LogAICodeTransformationToolsEnabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI-powered tools disabled (no provider available)")]
    private static partial void LogAIPoweredToolsDisabledNo(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered {Count} MCP resources")]
    private static partial void LogRegisteredMCPResources(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCPsharp initialized with {ToolCount} tools")]
    private static partial void LogMCPsharpInitializedWithTools(ILogger logger, int toolCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCPsharp shutting down...")]
    private static partial void LogMCPsharpShuttingDown(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCPsharp stopped")]
    private static partial void LogMCPsharpStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found {Count} solution files, auto-loading: {Path}")]
    private static partial void LogFoundSolutionFilesAutoLoading(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found {Count} project files, auto-loading: {Path}")]
    private static partial void LogFoundProjectFilesAutoLoading(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-loading C# project: {Project}")]
    private static partial void LogAutoLoadingCSharpProject(ILogger logger, string project);

    [LoggerMessage(Level = LogLevel.Information, Message = "Opened project database")]
    private static partial void LogOpenedProjectDatabase(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open project database, continuing without cache")]
    private static partial void LogFailedToOpenProjectDatabase(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Project auto-loaded successfully")]
    private static partial void LogProjectAutoLoadedSuccessfully(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "No C# project files found in workspace - project tools available on demand")]
    private static partial void LogNoCProjectFilesFound(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to auto-load project, continuing without pre-loaded project")]
    private static partial void LogFailedToAutoLoadProject(ILogger logger, Exception exception);
}
