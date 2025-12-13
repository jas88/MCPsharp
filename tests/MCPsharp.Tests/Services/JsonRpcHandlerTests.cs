using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public class JsonRpcHandlerTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonRpcHandlerTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Test]
    public async Task HandleRequest_ShouldParseValidRequest()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Jsonrpc = "2.0",
            Id = "test-1",
            Method = "initialize"
        };

        var handler = CreateHandler("", out _);

        // Act
        var response = await handler.HandleRequest(request);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Jsonrpc, Is.EqualTo("2.0"));
        Assert.That(response.Id, Is.EqualTo("test-1"));
        Assert.That(response.Error, Is.Null);
        Assert.That(response.Result, Is.Not.Null);
    }

    [Test]
    public async Task RunAsync_ShouldHandleInvalidJson()
    {
        // Arrange
        var input = "{ invalid json }\n";
        var handler = CreateHandler(input, out var output);

        // Act
        await handler.RunAsync();

        // Assert
        var responseLine = output.ToString().Trim();
        var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseLine, _jsonOptions);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Error, Is.Not.Null);
        Assert.That(response.Error!.Code, Is.EqualTo(JsonRpcErrorCodes.ParseError));
        Assert.That(response.Error.Message, Does.Contain("Parse error"));
    }

    [Test]
    public async Task HandleRequest_ShouldReturnMethodNotFoundError()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Jsonrpc = "2.0",
            Id = "test-2",
            Method = "nonexistent/method"
        };

        var handler = CreateHandler("", out _);

        // Act
        var response = await handler.HandleRequest(request);

        // Assert
        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error!.Code, Is.EqualTo(JsonRpcErrorCodes.MethodNotFound));
        Assert.That(response.Error.Message, Does.Contain("Method not found"));
        Assert.That(response.Result, Is.Null);
    }

    [Test]
    public async Task HandleRequest_ShouldHandleInitializeMethod()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Jsonrpc = "2.0",
            Id = 42,
            Method = "initialize"
        };

        var handler = CreateHandler("", out _);

        // Act
        var response = await handler.HandleRequest(request);

        // Assert
        Assert.That(response.Error, Is.Null);
        Assert.That(response.Result, Is.Not.Null);

        var resultJson = JsonSerializer.Serialize(response.Result, _jsonOptions);
        Assert.That(resultJson, Does.Contain("protocolVersion"));
        Assert.That(resultJson, Does.Contain("serverInfo"));
        Assert.That(resultJson, Does.Contain("MCPsharp"));
    }

    [Test]
    public async Task RunAsync_ShouldMatchRequestIdInResponse()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Jsonrpc = "2.0",
            Id = "unique-id-123",
            Method = "initialize"
        };

        var inputLine = JsonSerializer.Serialize(request, _jsonOptions) + "\n";
        var handler = CreateHandler(inputLine, out var output);

        // Act
        await handler.RunAsync();

        // Assert
        var responseLine = output.ToString().Trim();
        var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseLine, _jsonOptions);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Id.ToString(), Is.EqualTo("unique-id-123"));
    }

    [Test]
    public async Task RunAsync_ShouldHandleMultipleRequests()
    {
        // Arrange
        var request1 = new JsonRpcRequest { Jsonrpc = "2.0", Id = "req-1", Method = "initialize" };
        var request2 = new JsonRpcRequest { Jsonrpc = "2.0", Id = "req-2", Method = "tools/list" };
        var request3 = new JsonRpcRequest { Jsonrpc = "2.0", Id = "req-3", Method = "initialize" };

        var input = string.Join("\n",
            JsonSerializer.Serialize(request1, _jsonOptions),
            JsonSerializer.Serialize(request2, _jsonOptions),
            JsonSerializer.Serialize(request3, _jsonOptions)
        ) + "\n";

        var handler = CreateHandler(input, out var output);

        // Act
        await handler.RunAsync();

        // Assert
        var responseLines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(responseLines, Has.Length.EqualTo(3));

        var responses = responseLines
            .Select(line => JsonSerializer.Deserialize<JsonRpcResponse>(line, _jsonOptions))
            .ToList();

        Assert.That(responses[0]!.Id.ToString(), Is.EqualTo("req-1"));
        Assert.That(responses[1]!.Id.ToString(), Is.EqualTo("req-2"));
        Assert.That(responses[2]!.Id.ToString(), Is.EqualTo("req-3"));
    }

    [Test]
    public async Task HandleRequest_ShouldReturnProperErrorStructure()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Jsonrpc = "2.0",
            Id = "error-test",
            Method = "unknown/method"
        };

        var handler = CreateHandler("", out _);

        // Act
        var response = await handler.HandleRequest(request);

        // Assert
        Assert.That(response.Jsonrpc, Is.EqualTo("2.0"));
        Assert.That(response.Id, Is.EqualTo("error-test"));
        Assert.That(response.Result, Is.Null);
        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error!.Code, Is.EqualTo(JsonRpcErrorCodes.MethodNotFound));
        Assert.That(response.Error.Message, Is.Not.Empty);
    }

    [Test]
    public async Task HandleRequest_ShouldRejectInvalidJsonRpcVersion()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Jsonrpc = "1.0",
            Id = "version-test",
            Method = "initialize"
        };

        var handler = CreateHandler("", out _);

        // Act
        var response = await handler.HandleRequest(request);

        // Assert
        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error!.Code, Is.EqualTo(JsonRpcErrorCodes.InvalidRequest));
        Assert.That(response.Error.Message, Does.Contain("version"));
    }

    [Test]
    public async Task HandleRequest_ShouldHandleToolsListMethod()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Jsonrpc = "2.0",
            Id = "tools-list",
            Method = "tools/list"
        };

        var handler = CreateHandler("", out _);

        // Act
        var response = await handler.HandleRequest(request);

        // Assert
        Assert.That(response.Error, Is.Null);
        Assert.That(response.Result, Is.Not.Null);

        var resultJson = JsonSerializer.Serialize(response.Result, _jsonOptions);
        Assert.That(resultJson, Does.Contain("tools"));
    }

    [Test]
    public async Task RunAsync_ShouldSkipEmptyLines()
    {
        // Arrange
        var request = new JsonRpcRequest { Jsonrpc = "2.0", Id = "skip-test", Method = "initialize" };
        var input = "\n\n" + JsonSerializer.Serialize(request, _jsonOptions) + "\n\n";

        var handler = CreateHandler(input, out var output);

        // Act
        await handler.RunAsync();

        // Assert
        var responseLines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(responseLines, Has.Length.EqualTo(1));

        var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseLines[0], _jsonOptions);
        Assert.That(response!.Id.ToString(), Is.EqualTo("skip-test"));
    }

    [Test]
    public async Task HandleRequest_ShouldValidateJsonRpcField()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Jsonrpc = "2.0",
            Id = "validate-test",
            Method = "initialize"
        };

        var handler = CreateHandler("", out _);

        // Act
        var response = await handler.HandleRequest(request);

        // Assert
        Assert.That(response.Jsonrpc, Is.EqualTo("2.0"));
    }

    private static JsonRpcHandler CreateHandler(string input, out StringWriter output)
    {
        var inputReader = new StringReader(input);
        var outputWriter = new StringWriter();
        output = outputWriter;
        var projectManager = new ProjectContextManager();
        var workspace = new RoslynWorkspace();
        var registry = new McpToolRegistry(projectManager, workspace);
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonRpcHandler>();
        return new JsonRpcHandler(inputReader, outputWriter, registry, logger);
    }
}
