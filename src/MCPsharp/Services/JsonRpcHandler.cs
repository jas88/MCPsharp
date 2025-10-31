using System.Text.Json;
using MCPsharp.Models;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

/// <summary>
/// Handles JSON-RPC 2.0 protocol communication over stdin/stdout
/// </summary>
public class JsonRpcHandler
{
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly McpToolRegistry _toolRegistry;
    private readonly Microsoft.Extensions.Logging.ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonRpcHandler(TextReader input, TextWriter output, McpToolRegistry toolRegistry, Microsoft.Extensions.Logging.ILogger logger)
    {
        _input = input;
        _output = output;
        _toolRegistry = toolRegistry;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Main loop that reads JSON-RPC requests from input and writes responses to output
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await _input.ReadLineAsync(ct);
            if (line == null)
            {
                // End of input stream
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonRpcResponse response;
            object? requestId = null;

            try
            {
                var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, _jsonOptions);
                if (request == null)
                {
                    response = CreateErrorResponse(null, JsonRpcErrorCodes.InvalidRequest, "Request is null");
                }
                else
                {
                    requestId = request.Id;

                    // Handle notifications (no ID, no response)
                    if (request.Id == null)
                    {
                        await HandleNotification(request);
                        continue; // Don't send response for notifications
                    }

                    response = await HandleRequest(request);
                }
            }
            catch (JsonException ex)
            {
                response = CreateErrorResponse(requestId, JsonRpcErrorCodes.ParseError, $"Parse error: {ex.Message}");
            }
            catch (Exception ex)
            {
                response = CreateErrorResponse(requestId, JsonRpcErrorCodes.InternalError, $"Internal error: {ex.Message}");
            }

            var responseLine = JsonSerializer.Serialize(response, _jsonOptions);
            await _output.WriteLineAsync(responseLine);
            await _output.FlushAsync(ct);
        }
    }

    /// <summary>
    /// Routes requests to appropriate handlers based on method name
    /// </summary>
    public async Task<JsonRpcResponse> HandleRequest(JsonRpcRequest request)
    {
        // Validate JSON-RPC version
        if (request.Jsonrpc != "2.0")
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidRequest, "Invalid JSON-RPC version");
        }

        try
        {
            object? result = request.Method switch
            {
                "initialize" => await HandleInitialize(request.Params),
                "tools/list" => await HandleToolsList(request.Params),
                "tools/call" => await HandleToolsCall(request.Params),
                _ => throw new MethodNotFoundException($"Method not found: {request.Method}")
            };

            return new JsonRpcResponse
            {
                Jsonrpc = "2.0",
                Id = request.Id ?? string.Empty,
                Result = result
            };
        }
        catch (MethodNotFoundException ex)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.MethodNotFound, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, ex.Message);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InternalError, ex.Message);
        }
    }

    private Task HandleNotification(JsonRpcRequest request)
    {
        // Handle MCP notifications (no response required)
        _logger.LogDebug("Received notification: {Method}", request.Method);

        switch (request.Method)
        {
            case "notifications/initialized":
                // Client has finished initialization
                _logger.LogInformation("Client initialized");
                break;

            case "notifications/cancelled":
                // Client cancelled a request
                _logger.LogInformation("Request cancelled");
                break;

            default:
                _logger.LogWarning("Unknown notification: {Method}", request.Method);
                break;
        }

        return Task.CompletedTask;
    }

    private Task<object> HandleInitialize(object? parameters)
    {
        // MCP initialize handshake
        var response = new
        {
            protocolVersion = "2024-11-05",
            serverInfo = new
            {
                name = "MCPsharp",
                version = "0.1.0"
            },
            capabilities = new
            {
                tools = new { }
            }
        };

        return Task.FromResult<object>(response);
    }

    private Task<object> HandleToolsList(object? parameters)
    {
        // Return list of available tools from registry
        var tools = _toolRegistry.GetTools();
        var response = new
        {
            tools = tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                inputSchema = t.InputSchema
            })
        };

        return Task.FromResult<object>(response);
    }

    private async Task<object> HandleToolsCall(object? parameters)
    {
        // Execute a tool via the registry
        if (parameters == null)
        {
            throw new ArgumentException("Tool call requires parameters");
        }

        var paramsJson = JsonSerializer.SerializeToDocument(parameters, _jsonOptions);
        var name = paramsJson.RootElement.GetProperty("name").GetString();
        var arguments = paramsJson.RootElement.GetProperty("arguments");

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Tool name is required");
        }

        var request = new ToolCallRequest
        {
            Name = name,
            Arguments = JsonDocument.Parse(arguments.GetRawText())
        };

        var result = await _toolRegistry.ExecuteTool(request);

        var response = new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = result.Success
                        ? JsonSerializer.Serialize(result.Result, _jsonOptions)
                        : $"Error: {result.Error}"
                }
            },
            isError = !result.Success
        };

        return response;
    }

    private static JsonRpcResponse CreateErrorResponse(object? id, int code, string message)
    {
        return new JsonRpcResponse
        {
            Jsonrpc = "2.0",
            Id = id ?? "null",
            Error = new JsonRpcError
            {
                Code = code,
                Message = message
            }
        };
    }

    private class MethodNotFoundException : Exception
    {
        public MethodNotFoundException(string message) : base(message) { }
    }
}
