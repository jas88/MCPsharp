namespace MCPsharp.Services.AI;

/// <summary>
/// Abstraction for AI inference providers (Ollama, OpenRouter, local models, etc.)
/// Used to power intelligent MCP tools that process verbose data and return concise answers.
/// </summary>
public interface IAIProvider
{
    /// <summary>
    /// Process a query using the AI model.
    /// </summary>
    /// <param name="prompt">The complete prompt including context and question</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The AI's response text</returns>
    Task<string> ProcessQueryAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the provider is available and configured.
    /// </summary>
    /// <returns>True if the provider can handle requests</returns>
    Task<bool> IsAvailableAsync();

    /// <summary>
    /// Get the provider's name for logging/diagnostics.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Get the model being used.
    /// </summary>
    string ModelName { get; }
}
