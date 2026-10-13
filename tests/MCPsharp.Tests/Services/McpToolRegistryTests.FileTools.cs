using System.Text.Json;
using MCPsharp.Models;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class McpToolRegistryTests
{
    [Test]
    public async Task ExecuteTool_FileList_ReturnsFiles()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        // Create some test files
        var testFile1 = Path.Combine(_testProjectPath, "test1.txt");
        var testFile2 = Path.Combine(_testProjectPath, "test2.cs");
        await File.WriteAllTextAsync(testFile1, "Test content 1");
        await File.WriteAllTextAsync(testFile2, "Test content 2");

        var request = new ToolCallRequest
        {
            Name = "file_list",
            Arguments = JsonDocument.Parse("{}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteTool_FileList_WithPattern_FiltersFiles()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        // Create test files
        await File.WriteAllTextAsync(Path.Combine(_testProjectPath, "test.cs"), "C# file");
        await File.WriteAllTextAsync(Path.Combine(_testProjectPath, "test.txt"), "Text file");

        var request = new ToolCallRequest
        {
            Name = "file_list",
            Arguments = JsonDocument.Parse("{\"pattern\": \"**/*.cs\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteTool_FileRead_ReturnsContent()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        var testFile = Path.Combine(_testProjectPath, "test.txt");
        var expectedContent = "Hello, MCP!";
        await File.WriteAllTextAsync(testFile, expectedContent);

        var request = new ToolCallRequest
        {
            Name = "file_read",
            Arguments = JsonDocument.Parse("{\"path\": \"test.txt\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteTool_FileRead_NonExistentFile_Fails()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        var request = new ToolCallRequest
        {
            Name = "file_read",
            Arguments = JsonDocument.Parse("{\"path\": \"nonexistent.txt\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteTool_FileWrite_CreatesFile()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        var content = "New file content";
        var request = new ToolCallRequest
        {
            Name = "file_write",
            Arguments = JsonDocument.Parse($"{{\"path\": \"newfile.txt\", \"content\": \"{content}\"}}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);

        var filePath = Path.Combine(_testProjectPath, "newfile.txt");
        Assert.That(File.Exists(filePath), Is.True);
        var actualContent = await File.ReadAllTextAsync(filePath);
        Assert.That(actualContent, Is.EqualTo(content));
    }

    [Test]
    public async Task ExecuteTool_FileWrite_OverwritesExistingFile()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        var filePath = Path.Combine(_testProjectPath, "existing.txt");
        await File.WriteAllTextAsync(filePath, "Original content");

        var newContent = "Updated content";
        var request = new ToolCallRequest
        {
            Name = "file_write",
            Arguments = JsonDocument.Parse($"{{\"path\": \"existing.txt\", \"content\": \"{newContent}\"}}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        var actualContent = await File.ReadAllTextAsync(filePath);
        Assert.That(actualContent, Is.EqualTo(newContent));
    }

    [Test]
    public async Task ExecuteTool_FileEdit_AppliesEdits()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        var filePath = Path.Combine(_testProjectPath, "edit.txt");
        await File.WriteAllTextAsync(filePath, "Line 1\nLine 2\nLine 3");

        var editsJson = @"{
            ""path"": ""edit.txt"",
            ""edits"": [
                {
                    ""type"": ""replace"",
                    ""start_line"": 1,
                    ""start_column"": 0,
                    ""end_line"": 1,
                    ""end_column"": 6,
                    ""new_text"": ""Modified""
                }
            ]
        }";

        var request = new ToolCallRequest
        {
            Name = "file_edit",
            Arguments = JsonDocument.Parse(editsJson)
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteTool_UnknownTool_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "unknown_tool",
            Arguments = JsonDocument.Parse("{}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Unknown tool"));
    }

    [Test]
    public async Task ExecuteTool_InvalidArguments_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "file_read",
            Arguments = JsonDocument.Parse("{\"invalid_arg\": \"value\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
    }
}
