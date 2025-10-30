using MCPsharp.Services;
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

            // Create tool registry with core services
            var toolRegistry = new McpToolRegistry(
                projectManager,
                roslynWorkspace
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
