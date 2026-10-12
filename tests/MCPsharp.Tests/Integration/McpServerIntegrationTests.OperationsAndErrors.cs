using MCPsharp.Services;
using MCPsharp.Models;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

public partial class McpServerIntegrationTests
{
    [Test]
    public async Task Test06_MultipleOperations_SequentialReads()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Read multiple files
        var programResult = await _fileService.ReadFileAsync("src/Program.cs");
        var helperResult = await _fileService.ReadFileAsync("src/Helper.cs");
        var readmeResult = await _fileService.ReadFileAsync("README.md");

        // Assert - All reads succeeded
        Assert.That(programResult.Success, Is.True);
        Assert.That(programResult.Content, Does.Contain("Main"));

        Assert.That(helperResult.Success, Is.True);
        Assert.That(helperResult.Content, Does.Contain("Helper"));

        Assert.That(readmeResult.Success, Is.True);
        Assert.That(readmeResult.Content, Does.Contain("Test Project"));
    }

    [Test]
    public async Task Test07_ErrorHandling_ReadNonExistentFile()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act
        var result = await _fileService.ReadFileAsync("src/DoesNotExist.cs");

        // Assert - Proper error response
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Error, Does.Contain("not found"));
        Assert.That(result.Content, Is.Null);
    }

    [Test]
    public async Task Test08_ErrorHandling_ReadFileOutsideProject()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Try to read outside project root
        var result = await _fileService.ReadFileAsync("../outside.cs");

        // Assert - Security error
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("outside project root"));
        Assert.That(result.Content, Is.Null);
    }

    [Test]
    public void Test09_ErrorHandling_OpenNonExistentProject()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        var ex = Assert.Throws<DirectoryNotFoundException>(() => new FileOperationsService(nonExistentPath));
        Assert.That(ex.Message, Is.EqualTo($"Root path does not exist: {nonExistentPath}"));
    }

    [Test]
    public async Task Test10_ErrorHandling_WriteFileOutsideProject()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Try to write outside project root
        var result = await _fileService.WriteFileAsync("../outside.cs", "content");

        // Assert - Security error
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("outside project root"));
    }

    [Test]
    public async Task Test11_CompleteWorkflow_CreateEditRead()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var fileName = "src/Workflow.cs";
        var initialContent = "namespace TestProject\n{\n    public class Workflow { }\n}";

        // Act 1 - Create file
        var createResult = await _fileService.WriteFileAsync(fileName, initialContent);
        Assert.That(createResult.Success, Is.True);
        Assert.That(createResult.Created, Is.True);

        // Act 2 - Edit file (add method before closing brace)
        var edits = new List<TextEdit>
        {
            new InsertEdit
            {
                StartLine = 2,
                StartColumn = 28, // Position right before closing brace (length of "    public class Workflow { }")
                EndLine = 2,
                EndColumn = 28,
                NewText = "\n        public void Execute() { }\n    "
            }
        };
        var editResult = await _fileService.EditFileAsync(fileName, edits);

        // Debug: print error if failed
        if (!editResult.Success)
        {
            throw new InvalidOperationException($"Edit failed: {editResult.Error}");
        }
        Assert.That(editResult.Success, Is.True);

        // Act 3 - Read back final content
        var readResult = await _fileService.ReadFileAsync(fileName);

        // Assert - Verify complete workflow
        Assert.That(readResult.Success, Is.True);
        Assert.That(readResult.Content, Does.Contain("public class Workflow"));
        Assert.That(readResult.Content, Does.Contain("public void Execute()"));
    }
}
