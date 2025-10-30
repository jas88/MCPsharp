using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.BuiltIn;
using MCPsharp.Services.Analyzers.Fixes;

namespace MCPsharp.Services;

/// <summary>
/// Service collection extensions for analyzer services
/// </summary>
public static class AnalyzerServiceExtensions
{
    /// <summary>
    /// Add analyzer services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddAnalyzerServices(this IServiceCollection services, string? basePath = null)
    {
        basePath ??= Directory.GetCurrentDirectory();

        // Core analyzer services
        services.AddSingleton<ISecurityManager>(provider =>
            new SecurityManager(
                provider.GetRequiredService<ILogger<SecurityManager>>(),
                basePath));

        services.AddSingleton<IAnalyzerRegistry, AnalyzerRegistry>();
        services.AddSingleton<IFixEngine, FixEngine>();

        services.AddSingleton<IAnalyzerHost>(provider =>
            new AnalyzerHost(
                provider.GetRequiredService<ILogger<AnalyzerHost>>(),
                provider.GetRequiredService<IAnalyzerRegistry>(),
                provider.GetRequiredService<ISecurityManager>(),
                provider.GetRequiredService<IFixEngine>()));

        services.AddSingleton<AnalyzerConfigurationManager>(provider =>
            new AnalyzerConfigurationManager(
                provider.GetRequiredService<ILogger<AnalyzerConfigurationManager>>(),
                basePath));

        // Built-in analyzers
        services.AddSingleton<NUnitUpgraderAnalyzer>();

        // Register built-in analyzers on startup
        services.AddSingleton<IHostedService>(provider =>
            new AnalyzerInitializationService(provider));

        return services;
    }

    /// <summary>
    /// Add MCP tool registry with analyzer tools
    /// </summary>
    public static IServiceCollection AddAnalyzerMcpTools(this IServiceCollection services)
    {
        services.AddSingleton<IMcpToolRegistry>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<McpToolRegistry>>();
            var projectContext = provider.GetRequiredService<ProjectContextManager>();
            var analyzerHost = provider.GetRequiredService<IAnalyzerHost>();
            var fixEngine = provider.GetRequiredService<IFixEngine>();

            return new AnalyzerToolRegistry(logger, projectContext, analyzerHost, fixEngine);
        });

        return services;
    }
}

/// <summary>
/// Background service to initialize built-in analyzers
/// </summary>
internal class AnalyzerInitializationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AnalyzerInitializationService> _logger;

    public AnalyzerInitializationService(IServiceProvider serviceProvider, ILogger<AnalyzerInitializationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initializing built-in analyzers");

            var analyzerHost = _serviceProvider.GetRequiredService<IAnalyzerHost>();
            var registry = _serviceProvider.GetRequiredService<IAnalyzerRegistry>();

            // Register built-in analyzers
            var nunitAnalyzer = _serviceProvider.GetRequiredService<NUnitUpgraderAnalyzer>();
            await registry.RegisterAnalyzerAsync(nunitAnalyzer, cancellationToken);

            _logger.LogInformation("Built-in analyzers initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing built-in analyzers");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Extended MCP tool registry that includes analyzer tools
/// </summary>
internal class AnalyzerToolRegistry : McpToolRegistry
{
    private readonly IAnalyzerHost _analyzerHost;
    private readonly IFixEngine _fixEngine;

    public AnalyzerToolRegistry(
        ILogger<McpToolRegistry> logger,
        ProjectContextManager projectContext,
        IAnalyzerHost analyzerHost,
        IFixEngine fixEngine)
        : base(logger, projectContext)
    {
        _analyzerHost = analyzerHost;
        _fixEngine = fixEngine;
    }

    public override List<McpTool> GetTools()
    {
        var baseTools = base.GetTools();
        var analyzerTools = AnalyzerTools.GetAnalyzerTools();

        return baseTools.Concat(analyzerTools).ToList();
    }

    public override async Task<ToolCallResult> ExecuteTool(ToolCallRequest request, CancellationToken ct = default)
    {
        // Check if this is an analyzer tool
        var analyzerToolNames = AnalyzerTools.GetAnalyzerTools().Select(t => t.Name).ToHashSet();

        if (analyzerToolNames.Contains(request.Name))
        {
            return await AnalyzerTools.ExecuteAnalyzerTool(request.Name, request.Arguments, _analyzerHost, _fixEngine, ct);
        }

        // Fall back to base implementation for other tools
        return await base.ExecuteTool(request, ct);
    }
}