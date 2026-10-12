using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.AI;

/// <summary>
/// Factory for creating AI provider instances based on configuration.
/// Supports auto-detection and manual configuration.
/// </summary>
public partial class AIProviderFactory
{
    private readonly IConfiguration? _configuration;
    private readonly ILogger<AIProviderFactory> _logger;

    public AIProviderFactory(IConfiguration? configuration, ILogger<AIProviderFactory> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Create an AI provider instance based on configuration.
    /// Returns null if AI features are disabled or no provider is available.
    /// </summary>
    public async Task<IAIProvider?> CreateAsync()
    {
        var providerType = _configuration?["AIProvider:Type"] ?? "auto";

        if (providerType.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            LogAiDisabled(_logger);
            return null;
        }

        if (providerType.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return await AutoDetectProviderAsync();
        }

        return providerType.ToLowerInvariant() switch
        {
            "ollama" => await CreateOllamaProviderAsync(),
            "openrouter" => (IAIProvider)CreateOpenRouterProvider(),
            _ => throw new InvalidOperationException($"Unknown AI provider type: {providerType}")
        };
    }

    /// <summary>
    /// Auto-detect available AI providers in order of preference:
    /// 1. Ollama (local, free, fast if running)
    /// 2. OpenRouter (requires API key)
    /// 3. None (AI features disabled)
    /// </summary>
    private async Task<IAIProvider?> AutoDetectProviderAsync()
    {
        LogAutoDetecting(_logger);

        // Try Ollama first
        var ollamaProvider = await CreateOllamaProviderAsync();
        if (ollamaProvider != null && await ollamaProvider.IsAvailableAsync())
        {
            var detectedUrl = Environment.GetEnvironmentVariable("OLLAMA_URL") ?? OllamaProvider.DefaultBaseUrl;
            LogUsingOllama(_logger, detectedUrl);
            return ollamaProvider;
        }

        // Try OpenRouter
        var openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            ?? _configuration?["AIProvider:OpenRouter:ApiKey"];

        if (!string.IsNullOrWhiteSpace(openRouterKey))
        {
            LogUsingOpenRouter(_logger);
            return CreateOpenRouterProvider();
        }

        // No provider available
        LogNoProvider(_logger);
        return null;
    }

    private async Task<OllamaProvider?> CreateOllamaProviderAsync()
    {
        OllamaProvider? provider = null;
        try
        {
            var baseUrl = _configuration?["AIProvider:Ollama:BaseUrl"]
                ?? Environment.GetEnvironmentVariable("OLLAMA_URL");

            var model = _configuration?["AIProvider:Ollama:Model"]
                ?? Environment.GetEnvironmentVariable("OLLAMA_MODEL");

            provider = new OllamaProvider(baseUrl, model);

            // Verify it's actually available
            if (!await provider.IsAvailableAsync())
            {
                LogOllamaUnavailable(_logger, baseUrl ?? OllamaProvider.DefaultBaseUrl);
                provider.Dispose();
                return null;
            }

            return provider;
        }
        catch (Exception ex)
        {
            LogOllamaCreateFailed(_logger, ex);
            provider?.Dispose();
            return null;
        }
    }

    private OpenRouterProvider CreateOpenRouterProvider()
    {
        var apiKey = _configuration?["AIProvider:OpenRouter:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            ?? throw new InvalidOperationException("OpenRouter API key not configured");

        var model = _configuration?["AIProvider:OpenRouter:Model"]
            ?? "anthropic/claude-3.5-sonnet";

        return new OpenRouterProvider(apiKey, model);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AI-powered tools are disabled (AIProvider:Type = none)")]
    private static partial void LogAiDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-detecting AI provider...")]
    private static partial void LogAutoDetecting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Using Ollama for AI-powered tools (detected at {BaseUrl})")]
    private static partial void LogUsingOllama(ILogger logger, string baseUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "Using OpenRouter for AI-powered tools")]
    private static partial void LogUsingOpenRouter(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No AI provider available. AI-powered tools will be disabled. Install Ollama (brew install ollama) or configure OpenRouter API key.")]
    private static partial void LogNoProvider(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ollama not available at {BaseUrl}")]
    private static partial void LogOllamaUnavailable(ILogger logger, string baseUrl);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to create Ollama provider")]
    private static partial void LogOllamaCreateFailed(ILogger logger, Exception exception);
}
