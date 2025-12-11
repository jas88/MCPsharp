using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MCPsharp.Services.AI;

/// <summary>
/// AI provider that uses Ollama for local inference.
/// Ollama must be running locally (typically at http://localhost:11434).
/// </summary>
public class OllamaProvider : IAIProvider, IDisposable
{
    private readonly HttpClient _client;
    private readonly string _model;
    private readonly string _baseUrl;

    public string ProviderName => "Ollama";
    public string ModelName => _model;

    public OllamaProvider(string? baseUrl = null, string? model = null)
    {
        _baseUrl = baseUrl
            ?? Environment.GetEnvironmentVariable("OLLAMA_URL")
            ?? "http://localhost:11434";

        _model = model
            ?? Environment.GetEnvironmentVariable("OLLAMA_MODEL")
            ?? "qwen2.5-coder:3b";

        _client = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromMinutes(5) // Local inference can be slow
        };
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _client.GetAsync("/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> ProcessQueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var request = new OllamaGenerateRequest
        {
            Model = _model,
            Prompt = prompt,
            Stream = false,
            Options = new OllamaOptions
            {
                Temperature = 0.1, // Low temperature for consistent, factual responses
                NumCtx = 8192      // Context window
            }
        };

        var response = await _client.PostAsJsonAsync("/api/generate", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken);

        if (result == null || string.IsNullOrWhiteSpace(result.Response))
        {
            throw new InvalidOperationException("Ollama returned empty response");
        }

        return result.Response.Trim();
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    // Ollama API models
    private class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "";

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("options")]
        public OllamaOptions? Options { get; set; }
    }

    private class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("num_ctx")]
        public int NumCtx { get; set; }
    }

    private class OllamaGenerateResponse
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("response")]
        public string Response { get; set; } = "";

        [JsonPropertyName("done")]
        public bool Done { get; set; }

        [JsonPropertyName("context")]
        public int[]? Context { get; set; }

        [JsonPropertyName("total_duration")]
        public long? TotalDuration { get; set; }
    }
}
