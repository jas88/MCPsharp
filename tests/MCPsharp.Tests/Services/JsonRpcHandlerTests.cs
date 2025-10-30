using System.Text.Json;
using FluentAssertions;
using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using Xunit;

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

    [Fact]
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
        response.Should().NotBeNull();
        response.Jsonrpc.Should().Be("2.0");
        response.Id.Should().Be("test-1");
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();
    }

    [Fact]
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

        response.Should().NotBeNull();
        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(JsonRpcErrorCodes.ParseError);
        response.Error.Message.Should().Contain("Parse error");
    }

    [Fact]
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
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(JsonRpcErrorCodes.MethodNotFound);
        response.Error.Message.Should().Contain("Method not found");
        response.Result.Should().BeNull();
    }

    [Fact]
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
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();

        var resultJson = JsonSerializer.Serialize(response.Result, _jsonOptions);
        resultJson.Should().Contain("protocolVersion");
        resultJson.Should().Contain("serverInfo");
        resultJson.Should().Contain("MCPsharp");
    }

    [Fact]
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

        response.Should().NotBeNull();
        response!.Id.ToString().Should().Be("unique-id-123");
    }

    [Fact]
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
        responseLines.Should().HaveCount(3);

        var responses = responseLines
            .Select(line => JsonSerializer.Deserialize<JsonRpcResponse>(line, _jsonOptions))
            .ToList();

        responses[0]!.Id.ToString().Should().Be("req-1");
        responses[1]!.Id.ToString().Should().Be("req-2");
        responses[2]!.Id.ToString().Should().Be("req-3");
    }

    [Fact]
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
        response.Jsonrpc.Should().Be("2.0");
        response.Id.Should().Be("error-test");
        response.Result.Should().BeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(JsonRpcErrorCodes.MethodNotFound);
        response.Error.Message.Should().NotBeEmpty();
    }

    [Fact]
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
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(JsonRpcErrorCodes.InvalidRequest);
        response.Error.Message.Should().Contain("version");
    }

    [Fact]
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
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();

        var resultJson = JsonSerializer.Serialize(response.Result, _jsonOptions);
        resultJson.Should().Contain("tools");
    }

    [Fact]
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
        responseLines.Should().HaveCount(1);

        var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseLines[0], _jsonOptions);
        response!.Id.ToString().Should().Be("skip-test");
    }

    [Fact]
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
        response.Jsonrpc.Should().Be("2.0");
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
