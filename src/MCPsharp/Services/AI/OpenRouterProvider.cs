using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MCPsharp.Services.AI;

/// <summary>
/// AI provider that uses OpenRouter for cloud-based inference.
/// Requires an OpenRouter API key.
/// </summary>
public class OpenRouterProvider : IAIProvider, IDisposable
{
    private readonly HttpClient _client;
    private readonly string _model;
    private readonly string _apiKey;

    public string ProviderName => "OpenRouter";
    public string ModelName => _model;

    public OpenRouterProvider(string apiKey, string? model = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model ?? "anthropic/claude-3.5-sonnet";

        _client = new HttpClient
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/"),
            Timeout = TimeSpan.FromMinutes(2)
        };

        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        _client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/mcpsharp/mcpsharp");
        _client.DefaultRequestHeaders.Add("X-Title", "MCPsharp");
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            // Simple check - if we can reach the API, consider it available
            var response = await _client.GetAsync("models");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> ProcessQueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var request = new OpenRouterRequest
        {
            Model = _model,
            Messages = new[]
            {
                new OpenRouterMessage
                {
                    Role = "user",
                    Content = prompt
                }
            },
            Temperature = 0.1, // Low temperature for factual responses
            MaxTokens = 4096
        };

        var response = await _client.PostAsJsonAsync("chat/completions", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenRouterResponse>(cancellationToken);

        if (result?.Choices == null || result.Choices.Length == 0)
        {
            throw new InvalidOperationException("OpenRouter returned no choices");
        }

        var content = result.Choices[0].Message.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("OpenRouter returned empty content");
        }

        return content.Trim();
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    // OpenRouter API models
    private class OpenRouterRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("messages")]
        public OpenRouterMessage[] Messages { get; set; } = Array.Empty<OpenRouterMessage>();

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }
    }

    private class OpenRouterMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private class OpenRouterResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("choices")]
        public OpenRouterChoice[] Choices { get; set; } = Array.Empty<OpenRouterChoice>();

        [JsonPropertyName("usage")]
        public OpenRouterUsage? Usage { get; set; }
    }

    private class OpenRouterChoice
    {
        [JsonPropertyName("message")]
        public OpenRouterMessage Message { get; set; } = new();

        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; } = "";
    }

    private class OpenRouterUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
