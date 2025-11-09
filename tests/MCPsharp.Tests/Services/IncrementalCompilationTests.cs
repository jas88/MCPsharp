using System.Text;
using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;
using Xunit;
using Xunit.Abstractions;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Tests for incremental compilation integration between FileOperationsService and RoslynWorkspace
/// </summary>
public class IncrementalCompilationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly RoslynWorkspace _workspace;
    private readonly FileOperationsService _fileService;
    private readonly ITestOutputHelper _output;

    public IncrementalCompilationTests(ITestOutputHelper output)
    {
        _output = output;
        _testRoot = Path.Combine(Path.GetTempPath(), $"mcpsharp-incremental-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);

        _workspace = new RoslynWorkspace();
        _fileService = new FileOperationsService(_testRoot, _workspace);
    }

    public void Dispose()
    {
        _workspace?.Dispose();
        if (Directory.Exists(_testRoot))
        {
            try
            {
                Directory.Delete(_testRoot, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task EditFileAsync_ShouldUpdateRoslynWorkspace_WhenEditingCSharpFile()
    {
        // Arrange
        var testFile = "TestClass.cs";
        var originalContent = """
        public class TestClass
        {
            public void OriginalMethod()
            {
                Console.WriteLine("Original");
            }
        }
        """;

        var updatedContent = """
        public class TestClass
        {
            public void UpdatedMethod()
            {
                Console.WriteLine("Updated");
            }
        }
        """;

        // Create and initialize workspace with the test file
        await File.WriteAllTextAsync(Path.Combine(_testRoot, testFile), originalContent);
        await _workspace.InitializeAsync(_testRoot);

        // Verify original content is in workspace
        var originalDocument = _workspace.GetDocument(Path.Combine(_testRoot, testFile));
        Assert.NotNull(originalDocument);
        var originalSemanticModel = await _workspace.GetSemanticModelAsync(originalDocument!);
        Assert.NotNull(originalSemanticModel);
        var originalText = await originalDocument!.GetTextAsync();

        Assert.Contains("OriginalMethod", originalText.ToString());

        // Act - Edit the file
        var edits = new List<TextEdit>
        {
            new ReplaceEdit
            {
                StartLine = 2, StartColumn = 16,  // "OriginalMethod" starts at position 16
                EndLine = 2, EndColumn = 30,   // "OriginalMethod" ends at position 30 (exclusive)
                NewText = "UpdatedMethod"
            },
            new ReplaceEdit
            {
                StartLine = 4, StartColumn = 27,  // "Original" starts at position 27
                EndLine = 4, EndColumn = 35,   // "Original" ends at position 35 (exclusive)
                NewText = "Updated"
            }
        };

        var result = await _fileService.EditFileAsync(testFile, edits);

        // Assert
        Assert.True(result.Success, $"Edit failed with error: {result.Error}");
        Assert.Equal(updatedContent.Trim(), result.NewContent?.Trim());

        // Verify workspace was updated with new content
        var updatedDocument = _workspace.GetDocument(Path.Combine(_testRoot, testFile));
        Assert.NotNull(updatedDocument);
        var updatedSemanticModel = await _workspace.GetSemanticModelAsync(updatedDocument!);
        Assert.NotNull(updatedSemanticModel);
        var updatedText = await updatedDocument!.GetTextAsync();
        Assert.Contains("UpdatedMethod", updatedText.ToString());
        Assert.Contains("Updated", updatedText.ToString());

        // Verify semantic model reflects the changes
        var root = await updatedDocument!.GetSyntaxRootAsync();
        var methodDeclaration = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .FirstOrDefault();
        Assert.NotNull(methodDeclaration);
        Assert.Equal("UpdatedMethod", methodDeclaration!.Identifier.Text);
    }

    [Fact]
    public async Task WriteFileAsync_ShouldUpdateRoslynWorkspace_WhenWritingCSharpFile()
    {
        // Arrange
        var testFile = "NewClass.cs";
        var content = """
        public class NewClass
        {
            public int Value { get; set; }

            public void NewMethod()
            {
                Value = 42;
            }
        }
        """;

        // Initialize workspace first
        await _workspace.InitializeAsync(_testRoot);

        // Act - Write new file
        var result = await _fileService.WriteFileAsync(testFile, content);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Created);

        // Verify workspace was updated with new file
        var document = _workspace.GetDocument(Path.Combine(_testRoot, testFile));
        Assert.NotNull(document);
        var semanticModel = await _workspace.GetSemanticModelAsync(document!);
        Assert.NotNull(semanticModel);
        var text = await document!.GetTextAsync();
        Assert.Contains("NewClass", text.ToString());
        Assert.Contains("NewMethod", text.ToString());

        // Verify semantic model has correct structure
        var root = await document!.GetSyntaxRootAsync();
        var classDeclaration = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .FirstOrDefault();
        Assert.NotNull(classDeclaration);
        Assert.Equal("NewClass", classDeclaration!.Identifier.Text);
    }

    [Fact]
    public async Task EditFileAsync_ShouldNotUpdateWorkspace_WhenEditingNonCSharpFile()
    {
        // Arrange
        var testFile = "test.txt";
        var content = "Original content";
        var updatedContent = "Updated content";

        await File.WriteAllTextAsync(Path.Combine(_testRoot, testFile), content);
        await _workspace.InitializeAsync(_testRoot);

        // Act
        var result = await _fileService.WriteFileAsync(testFile, updatedContent);

        // Assert
        Assert.True(result.Success);

        // Verify workspace doesn't contain the .txt file
        var document = _workspace.GetDocument(Path.Combine(_testRoot, testFile));
        Assert.Null(document);
    }
}