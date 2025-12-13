using MCPsharp.Services;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public class ProjectContextManagerTests
{
    private string _testRoot = null!;
    private ProjectContextManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        // Create a temporary test directory
        _testRoot = Path.Combine(Path.GetTempPath(), $"mcpsharp-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);
        _manager = new ProjectContextManager();
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
    public void OpenProject_ShouldSetContext_WhenValidDirectoryProvided()
    {
        // Arrange
        CreateTestFile("file1.txt", "test");
        CreateTestFile("file2.txt", "test");
        CreateTestFile("subdir/file3.txt", "test");

        // Act
        _manager.OpenProject(_testRoot);
        var context = _manager.GetProjectContext();

        // Assert
        Assert.That(context, Is.Not.Null);
        Assert.That(context!.RootPath, Is.EqualTo(Path.GetFullPath(_testRoot)));
        Assert.That(context.OpenedAt, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(5)));
        Assert.That(context.FileCount, Is.EqualTo(3));
        Assert.That(context.KnownFiles, Has.Count.EqualTo(3));
    }

    [Test]
    public void OpenProject_ShouldThrowDirectoryNotFoundException_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        var ex = Assert.Throws<DirectoryNotFoundException>(() => _manager.OpenProject(nonExistentPath));
        Assert.That(ex!.Message, Is.EqualTo($"Directory does not exist: {Path.GetFullPath(nonExistentPath)}"));
    }

    [Test]
    public void OpenProject_ShouldThrowArgumentException_WhenPathIsFile()
    {
        // Arrange
        var filePath = CreateTestFile("testfile.txt", "test");

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => _manager.OpenProject(filePath));
        Assert.That(ex!.Message, Is.EqualTo($"Path is a file, not a directory: {Path.GetFullPath(filePath)}"));
    }

    [Test]
    public void OpenProject_ShouldNormalizePath_WithGetFullPath()
    {
        // Arrange
        var relativePath = Path.Combine(_testRoot, "subdir");
        Directory.CreateDirectory(relativePath);

        // Act
        _manager.OpenProject(relativePath);
        var context = _manager.GetProjectContext();

        // Assert
        Assert.That(context!.RootPath, Is.EqualTo(Path.GetFullPath(relativePath)));
    }

    [Test]
    public void CloseProject_ShouldClearContext()
    {
        // Arrange
        _manager.OpenProject(_testRoot);
        Assert.That(_manager.GetProjectContext(), Is.Not.Null);

        // Act
        _manager.CloseProject();
        var context = _manager.GetProjectContext();

        // Assert
        Assert.That(context, Is.Null);
    }

    [Test]
    public void GetProjectInfo_ShouldReturnNull_WhenNoProjectOpen()
    {
        // Act
        var info = _manager.GetProjectInfo();

        // Assert
        Assert.That(info, Is.Null);
    }

    [Test]
    public void GetProjectInfo_ShouldReturnProjectMetadata_WhenProjectOpen()
    {
        // Arrange
        CreateTestFile("file1.txt", "test");
        CreateTestFile("file2.txt", "test");
        _manager.OpenProject(_testRoot);

        // Act
        var info = _manager.GetProjectInfo();

        // Assert
        Assert.That(info, Is.Not.Null);
        Assert.That(info!["rootPath"], Is.EqualTo(Path.GetFullPath(_testRoot)));
        Assert.That(info["fileCount"], Is.EqualTo(2));
        Assert.That(info["openedAt"], Is.TypeOf<string>());
        Assert.That(info["openedAt"].ToString(), Is.Not.Empty);
    }

    [Test]
    public void IsValidPath_ShouldReturnFalse_WhenNoProjectOpen()
    {
        // Act
        var isValid = _manager.IsValidPath(Path.Combine(_testRoot, "file.txt"));

        // Assert
        Assert.That(isValid, Is.False);
    }

    [Test]
    public void IsValidPath_ShouldReturnTrue_WhenPathInsideProjectRoot()
    {
        // Arrange
        _manager.OpenProject(_testRoot);

        // Act
        var isValid = _manager.IsValidPath(Path.Combine(_testRoot, "subdir", "file.txt"));

        // Assert
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void IsValidPath_ShouldReturnFalse_WhenPathOutsideProjectRoot()
    {
        // Arrange
        _manager.OpenProject(_testRoot);
        var outsidePath = Path.Combine(Path.GetTempPath(), "outside.txt");

        // Act
        var isValid = _manager.IsValidPath(outsidePath);

        // Assert
        Assert.That(isValid, Is.False);
    }

    [Test]
    public void IsValidPath_ShouldUseCaseInsensitiveComparison()
    {
        // Arrange
        _manager.OpenProject(_testRoot);
        var upperCasePath = Path.Combine(_testRoot.ToUpper(), "file.txt");

        // Act
        var isValid = _manager.IsValidPath(upperCasePath);

        // Assert (should work on case-insensitive file systems like Windows/macOS)
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void OpenProject_ShouldReplaceCurrentProject_WhenOpeningDifferentProject()
    {
        // Arrange
        var firstProject = _testRoot;
        var secondProject = Path.Combine(Path.GetTempPath(), $"mcpsharp-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(secondProject);

        try
        {
            CreateTestFile("file1.txt", "test"); // In first project
            File.WriteAllText(Path.Combine(secondProject, "file2.txt"), "test"); // In second project

            _manager.OpenProject(firstProject);
            var firstContext = _manager.GetProjectContext();
            Assert.That(firstContext!.FileCount, Is.EqualTo(1));

            // Act
            _manager.OpenProject(secondProject);
            var secondContext = _manager.GetProjectContext();

            // Assert
            Assert.That(secondContext, Is.Not.Null);
            Assert.That(secondContext!.RootPath, Is.EqualTo(Path.GetFullPath(secondProject)));
            Assert.That(secondContext.FileCount, Is.EqualTo(1));
            Assert.That(secondContext.RootPath, Is.Not.EqualTo(firstContext.RootPath));
        }
        finally
        {
            // Cleanup second test directory
            if (Directory.Exists(secondProject))
            {
                Directory.Delete(secondProject, recursive: true);
            }
        }
    }

    [Test]
    public void OpenProject_ShouldCountFilesAccurately_WithNestedDirectories()
    {
        // Arrange
        CreateTestFile("root1.txt", "test");
        CreateTestFile("root2.txt", "test");
        CreateTestFile("dir1/file1.txt", "test");
        CreateTestFile("dir1/file2.txt", "test");
        CreateTestFile("dir1/subdir1/file3.txt", "test");
        CreateTestFile("dir2/file4.txt", "test");

        // Act
        _manager.OpenProject(_testRoot);
        var context = _manager.GetProjectContext();

        // Assert
        Assert.That(context!.FileCount, Is.EqualTo(6));
        Assert.That(context.KnownFiles, Has.Count.EqualTo(6));
    }

    [Test]
    public void GetProjectContext_ShouldReturnNull_InitiallyBeforeOpeningProject()
    {
        // Act
        var context = _manager.GetProjectContext();

        // Assert
        Assert.That(context, Is.Null);
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
