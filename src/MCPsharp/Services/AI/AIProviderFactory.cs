using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.AI;

/// <summary>
/// Factory for creating AI provider instances based on configuration.
/// Supports auto-detection and manual configuration.
/// </summary>
public class AIProviderFactory
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
            _logger.LogInformation("AI-powered tools are disabled (AIProvider:Type = none)");
            return null;
        }

        if (providerType.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return await AutoDetectProviderAsync();
        }

        return providerType.ToLowerInvariant() switch
        {
            "ollama" => await CreateOllamaProviderAsync(),
            "openrouter" => CreateOpenRouterProvider(),
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
        _logger.LogInformation("Auto-detecting AI provider...");

        // Try Ollama first
        var ollamaProvider = await CreateOllamaProviderAsync();
        if (ollamaProvider != null && await ollamaProvider.IsAvailableAsync())
        {
            _logger.LogInformation("Using Ollama for AI-powered tools (detected at {BaseUrl})",
                Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://localhost:11434");
            return ollamaProvider;
        }

        // Try OpenRouter
        var openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            ?? _configuration?["AIProvider:OpenRouter:ApiKey"];

        if (!string.IsNullOrWhiteSpace(openRouterKey))
        {
            _logger.LogInformation("Using OpenRouter for AI-powered tools");
            return CreateOpenRouterProvider();
        }

        // No provider available
        _logger.LogWarning("No AI provider available. AI-powered tools will be disabled. " +
                          "Install Ollama (brew install ollama) or configure OpenRouter API key.");
        return null;
    }

    private async Task<OllamaProvider?> CreateOllamaProviderAsync()
    {
        try
        {
            var baseUrl = _configuration?["AIProvider:Ollama:BaseUrl"]
                ?? Environment.GetEnvironmentVariable("OLLAMA_URL");

            var model = _configuration?["AIProvider:Ollama:Model"]
                ?? Environment.GetEnvironmentVariable("OLLAMA_MODEL");

            var provider = new OllamaProvider(baseUrl, model);

            // Verify it's actually available
            if (!await provider.IsAvailableAsync())
            {
                _logger.LogDebug("Ollama not available at {BaseUrl}", baseUrl ?? "http://localhost:11434");
                provider.Dispose();
                return null;
            }

            return provider;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to create Ollama provider");
            return null;
        }
    }

    private IAIProvider CreateOpenRouterProvider()
    {
        var apiKey = _configuration?["AIProvider:OpenRouter:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            ?? throw new InvalidOperationException("OpenRouter API key not configured");

        var model = _configuration?["AIProvider:OpenRouter:Model"]
            ?? "anthropic/claude-3.5-sonnet";

        return new OpenRouterProvider(apiKey, model);
    }
}
