using FluentAssertions;
using MCPsharp.Services;
using Xunit;

namespace MCPsharp.Tests.Services;

public class ProjectContextManagerTests : IDisposable
{
    private readonly string _testRoot;
    private readonly ProjectContextManager _manager;

    public ProjectContextManagerTests()
    {
        // Create a temporary test directory
        _testRoot = Path.Combine(Path.GetTempPath(), $"mcpsharp-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);
        _manager = new ProjectContextManager();
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Fact]
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
        context.Should().NotBeNull();
        context!.RootPath.Should().Be(Path.GetFullPath(_testRoot));
        context.OpenedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        context.FileCount.Should().Be(3);
        context.KnownFiles.Should().HaveCount(3);
    }

    [Fact]
    public void OpenProject_ShouldThrowDirectoryNotFoundException_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        Action act = () => _manager.OpenProject(nonExistentPath);
        act.Should().Throw<DirectoryNotFoundException>()
            .WithMessage($"Directory does not exist: {Path.GetFullPath(nonExistentPath)}");
    }

    [Fact]
    public void OpenProject_ShouldThrowArgumentException_WhenPathIsFile()
    {
        // Arrange
        var filePath = CreateTestFile("testfile.txt", "test");

        // Act & Assert
        Action act = () => _manager.OpenProject(filePath);
        act.Should().Throw<ArgumentException>()
            .WithMessage($"Path is a file, not a directory: {Path.GetFullPath(filePath)}");
    }

    [Fact]
    public void OpenProject_ShouldNormalizePath_WithGetFullPath()
    {
        // Arrange
        var relativePath = Path.Combine(_testRoot, "subdir");
        Directory.CreateDirectory(relativePath);

        // Act
        _manager.OpenProject(relativePath);
        var context = _manager.GetProjectContext();

        // Assert
        context!.RootPath.Should().Be(Path.GetFullPath(relativePath));
    }

    [Fact]
    public void CloseProject_ShouldClearContext()
    {
        // Arrange
        _manager.OpenProject(_testRoot);
        _manager.GetProjectContext().Should().NotBeNull();

        // Act
        _manager.CloseProject();
        var context = _manager.GetProjectContext();

        // Assert
        context.Should().BeNull();
    }

    [Fact]
    public void GetProjectInfo_ShouldReturnNull_WhenNoProjectOpen()
    {
        // Act
        var info = _manager.GetProjectInfo();

        // Assert
        info.Should().BeNull();
    }

    [Fact]
    public void GetProjectInfo_ShouldReturnProjectMetadata_WhenProjectOpen()
    {
        // Arrange
        CreateTestFile("file1.txt", "test");
        CreateTestFile("file2.txt", "test");
        _manager.OpenProject(_testRoot);

        // Act
        var info = _manager.GetProjectInfo();

        // Assert
        info.Should().NotBeNull();
        info!["rootPath"].Should().Be(Path.GetFullPath(_testRoot));
        info["fileCount"].Should().Be(2);
        info["openedAt"].Should().BeOfType<string>();
        info["openedAt"].ToString().Should().NotBeEmpty();
    }

    [Fact]
    public void IsValidPath_ShouldReturnFalse_WhenNoProjectOpen()
    {
        // Act
        var isValid = _manager.IsValidPath(Path.Combine(_testRoot, "file.txt"));

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void IsValidPath_ShouldReturnTrue_WhenPathInsideProjectRoot()
    {
        // Arrange
        _manager.OpenProject(_testRoot);

        // Act
        var isValid = _manager.IsValidPath(Path.Combine(_testRoot, "subdir", "file.txt"));

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void IsValidPath_ShouldReturnFalse_WhenPathOutsideProjectRoot()
    {
        // Arrange
        _manager.OpenProject(_testRoot);
        var outsidePath = Path.Combine(Path.GetTempPath(), "outside.txt");

        // Act
        var isValid = _manager.IsValidPath(outsidePath);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void IsValidPath_ShouldUseCaseInsensitiveComparison()
    {
        // Arrange
        _manager.OpenProject(_testRoot);
        var upperCasePath = Path.Combine(_testRoot.ToUpper(), "file.txt");

        // Act
        var isValid = _manager.IsValidPath(upperCasePath);

        // Assert (should work on case-insensitive file systems like Windows/macOS)
        isValid.Should().BeTrue();
    }

    [Fact]
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
            firstContext!.FileCount.Should().Be(1);

            // Act
            _manager.OpenProject(secondProject);
            var secondContext = _manager.GetProjectContext();

            // Assert
            secondContext.Should().NotBeNull();
            secondContext!.RootPath.Should().Be(Path.GetFullPath(secondProject));
            secondContext.FileCount.Should().Be(1);
            secondContext.RootPath.Should().NotBe(firstContext.RootPath);
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

    [Fact]
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
        context!.FileCount.Should().Be(6);
        context.KnownFiles.Should().HaveCount(6);
    }

    [Fact]
    public void GetProjectContext_ShouldReturnNull_InitiallyBeforeOpeningProject()
    {
        // Act
        var context = _manager.GetProjectContext();

        // Assert
        context.Should().BeNull();
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
