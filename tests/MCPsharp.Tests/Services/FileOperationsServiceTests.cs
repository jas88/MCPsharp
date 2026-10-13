using MCPsharp.Services;
using NUnit.Framework;
using FileInfo = MCPsharp.Models.FileInfo;

namespace MCPsharp.Tests.Services;

[TestFixture]
public partial class FileOperationsServiceTests
{
    private string _testRoot = null!;
    private FileOperationsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        // Create a temporary test directory
        _testRoot = Path.Combine(Path.GetTempPath(), $"mcpsharp-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);
        _service = new FileOperationsService(_testRoot);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test directory
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public void Constructor_ShouldThrowException_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => new FileOperationsService(nonExistentPath));
    }

    [Test]
    public void RootPath_ShouldReturnNormalizedAbsolutePath()
    {
        // Arrange
        var subDir = Path.Combine(_testRoot, "sub");
        Directory.CreateDirectory(subDir);
        var nonNormalizedPath = Path.Combine(subDir, "..", "sub");

        // Act
        var service = new FileOperationsService(nonNormalizedPath);

        // Assert
        Assert.That(_service.RootPath, Is.EqualTo(Path.GetFullPath(_testRoot)));
        Assert.That(service.RootPath, Is.EqualTo(Path.GetFullPath(subDir)));
        Assert.That(Path.IsPathRooted(service.RootPath), Is.True);
    }

    [Test]
    public void ListFiles_ShouldReturnAllFiles_WhenNoPatternSpecified()
    {
        // Arrange
        CreateTestFile("file1.cs", "// test");
        CreateTestFile("file2.txt", "test");
        CreateTestFile("subdir/file3.cs", "// test");

        // Act
        var result = _service.ListFiles();

        // Assert
        Assert.That(result.TotalFiles, Is.EqualTo(3));
        Assert.That(result.Files, Has.Count.EqualTo(3));
        Assert.That(result.Pattern, Is.Null);
    }

    [Test]
    public void ListFiles_ShouldFilterByCsFiles_WhenPatternSpecified()
    {
        // Arrange
        CreateTestFile("file1.cs", "// test");
        CreateTestFile("file2.txt", "test");
        CreateTestFile("subdir/file3.cs", "// test");

        // Act
        var result = _service.ListFiles("**/*.cs");

        // Assert
        Assert.That(result.TotalFiles, Is.EqualTo(2));
        Assert.That(result.Files, Has.Count.EqualTo(2));
        Assert.That(result.Files, Has.All.Matches<FileInfo>(f => f is not null && f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)));
        Assert.That(result.Pattern, Is.EqualTo("**/*.cs"));
    }

    [Test]
    public void ListFiles_ShouldExcludeHiddenFiles_ByDefault()
    {
        // Arrange
        CreateTestFile("visible.cs", "// test");
        var hiddenFile = CreateTestFile(".hidden.cs", "// test");
        File.SetAttributes(hiddenFile, FileAttributes.Hidden);

        // Act
        var result = _service.ListFiles();

        // Assert
        Assert.That(result.Files, Has.Count.EqualTo(1));
        Assert.That(result.Files, Has.All.Matches<FileInfo>(f => f is not null && !f.IsHidden));
    }

    [Test]
    public void ListFiles_ShouldIncludeHiddenFiles_WhenRequested()
    {
        // Arrange
        CreateTestFile("visible.cs", "// test");
        CreateTestFile(".hidden.cs", "// test");

        // Act
        var result = _service.ListFiles(includeHidden: true);

        // Assert
        Assert.That(result.Files, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ReadFileAsync_ShouldReturnContent_WhenFileExists()
    {
        // Arrange
        var content = "Hello, World!\nLine 2\nLine 3";
        CreateTestFile("test.txt", content);

        // Act
        var result = await _service.ReadFileAsync("test.txt");

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Content, Is.EqualTo(content));
        Assert.That(result.LineCount, Is.EqualTo(3));
        Assert.That(result.Encoding, Is.EqualTo("utf-8"));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ReadFileAsync_ShouldFail_WhenFileDoesNotExist()
    {
        // Act
        var result = await _service.ReadFileAsync("nonexistent.txt");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not found"));
        Assert.That(result.Content, Is.Null);
    }

    [Test]
    public async Task ReadFileAsync_ShouldFail_WhenPathOutsideRoot()
    {
        // Act
        var result = await _service.ReadFileAsync("../outside.txt");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("outside project root"));
    }

    [Test]
    public async Task WriteFileAsync_ShouldCreateNewFile()
    {
        // Arrange
        var content = "New file content";

        // Act
        var result = await _service.WriteFileAsync("newfile.txt", content);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Created, Is.True);
        Assert.That(result.BytesWritten, Is.GreaterThan(0));

        var fileContent = File.ReadAllText(Path.Combine(_testRoot, "newfile.txt"));
        Assert.That(fileContent, Is.EqualTo(content));
    }

    [Test]
    public async Task WriteFileAsync_ShouldOverwriteExistingFile()
    {
        // Arrange
        CreateTestFile("existing.txt", "Old content");
        var newContent = "New content";

        // Act
        var result = await _service.WriteFileAsync("existing.txt", newContent);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Created, Is.False);

        var fileContent = File.ReadAllText(Path.Combine(_testRoot, "existing.txt"));
        Assert.That(fileContent, Is.EqualTo(newContent));
    }

    [Test]
    public async Task WriteFileAsync_ShouldCreateDirectories_WhenRequested()
    {
        // Arrange
        var content = "Test content";

        // Act
        var result = await _service.WriteFileAsync("subdir1/subdir2/file.txt", content, createDirectories: true);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Created, Is.True);

        var filePath = Path.Combine(_testRoot, "subdir1/subdir2/file.txt");
        Assert.That(File.Exists(filePath), Is.True);
    }

    [Test]
    public async Task WriteFileAsync_ShouldFail_WhenPathOutsideRoot()
    {
        // Act
        var result = await _service.WriteFileAsync("../outside.txt", "content");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("outside project root"));
    }

    private string CreateTestFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_testRoot, relativePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        return fullPath;
    }
}
