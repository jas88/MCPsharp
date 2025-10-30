using System.Text.Json.Serialization;

namespace MCPsharp.Models;

/// <summary>
/// JSON-RPC 2.0 Request
/// </summary>
public class JsonRpcRequest
{
    /// <summary>
    /// JSON-RPC version (always "2.0")
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public required string Jsonrpc { get; init; }

    /// <summary>
    /// Request ID (string or number) - not present for notifications
    /// </summary>
    [JsonPropertyName("id")]
    public object? Id { get; init; }

    /// <summary>
    /// Method name to invoke
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>
    /// Method parameters (optional)
    /// </summary>
    [JsonPropertyName("params")]
    public object? Params { get; init; }
}

/// <summary>
/// JSON-RPC 2.0 Response
/// </summary>
public class JsonRpcResponse
{
    /// <summary>
    /// JSON-RPC version (always "2.0")
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public required string Jsonrpc { get; init; }

    /// <summary>
    /// Response ID (matches request ID)
    /// </summary>
    [JsonPropertyName("id")]
    public required object Id { get; init; }

    /// <summary>
    /// Result of successful method execution
    /// </summary>
    [JsonPropertyName("result")]
    public object? Result { get; init; }

    /// <summary>
    /// Error details if method execution failed
    /// </summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; init; }
}

/// <summary>
/// JSON-RPC 2.0 Error
/// </summary>
public class JsonRpcError
{
    /// <summary>
    /// Error code
    /// </summary>
    [JsonPropertyName("code")]
    public required int Code { get; init; }

    /// <summary>
    /// Error message
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Additional error data (optional)
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; init; }
}

/// <summary>
/// Standard JSON-RPC 2.0 error codes
/// </summary>
public static class JsonRpcErrorCodes
{
    /// <summary>
    /// Invalid JSON was received by the server
    /// </summary>
    public const int ParseError = -32700;

    /// <summary>
    /// The JSON sent is not a valid Request object
    /// </summary>
    public const int InvalidRequest = -32600;

    /// <summary>
    /// The method does not exist / is not available
    /// </summary>
    public const int MethodNotFound = -32601;

    /// <summary>
    /// Invalid method parameter(s)
    /// </summary>
    public const int InvalidParams = -32602;

    /// <summary>
    /// Internal JSON-RPC error
    /// </summary>
    public const int InternalError = -32603;
}
