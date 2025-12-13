using System.Text;
using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Tests for incremental compilation integration between FileOperationsService and RoslynWorkspace
/// </summary>
public class IncrementalCompilationTests
{
    private string _testRoot = null!;
    private RoslynWorkspace _workspace = null!;
    private FileOperationsService _fileService = null!;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mcpsharp-incremental-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);

        _workspace = new RoslynWorkspace();
        _fileService = new FileOperationsService(_testRoot, _workspace);
    }

    [TearDown]
    public void TearDown()
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

    [Test]
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
        Assert.That(originalDocument, Is.Not.Null);
        var originalSemanticModel = await _workspace.GetSemanticModelAsync(originalDocument!);
        Assert.That(originalSemanticModel, Is.Not.Null);
        var originalText = await originalDocument!.GetTextAsync();

        Assert.That(originalText.ToString(), Does.Contain("OriginalMethod"));

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
        Assert.That(result.Success, Is.True, $"Edit failed with error: {result.Error}");
        Assert.That(result.NewContent?.Trim(), Is.EqualTo(updatedContent.Trim()));

        // Verify workspace was updated with new content
        var updatedDocument = _workspace.GetDocument(Path.Combine(_testRoot, testFile));
        Assert.That(updatedDocument, Is.Not.Null);
        var updatedSemanticModel = await _workspace.GetSemanticModelAsync(updatedDocument!);
        Assert.That(updatedSemanticModel, Is.Not.Null);
        var updatedText = await updatedDocument!.GetTextAsync();
        Assert.That(updatedText.ToString(), Does.Contain("UpdatedMethod"));
        Assert.That(updatedText.ToString(), Does.Contain("Updated"));

        // Verify semantic model reflects the changes
        var root = await updatedDocument!.GetSyntaxRootAsync();
        Assert.That(root, Is.Not.Null);
        var methodDeclaration = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .FirstOrDefault();
        Assert.That(methodDeclaration, Is.Not.Null);
        Assert.That(methodDeclaration.Identifier.Text, Is.EqualTo("UpdatedMethod"));
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Created, Is.True);

        // Verify workspace was updated with new file
        var document = _workspace.GetDocument(Path.Combine(_testRoot, testFile));
        Assert.That(document, Is.Not.Null);
        var semanticModel = await _workspace.GetSemanticModelAsync(document!);
        Assert.That(semanticModel, Is.Not.Null);
        var text = await document!.GetTextAsync();
        Assert.That(text.ToString(), Does.Contain("NewClass"));
        Assert.That(text.ToString(), Does.Contain("NewMethod"));

        // Verify semantic model has correct structure
        var root = await document!.GetSyntaxRootAsync();
        Assert.That(root, Is.Not.Null);
        var classDeclaration = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .FirstOrDefault();
        Assert.That(classDeclaration, Is.Not.Null);
        Assert.That(classDeclaration.Identifier.Text, Is.EqualTo("NewClass"));
    }

    [Test]
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
        Assert.That(result.Success, Is.True);

        // Verify workspace doesn't contain the .txt file
        var document = _workspace.GetDocument(Path.Combine(_testRoot, testFile));
        Assert.That(document, Is.Null);
    }
}
