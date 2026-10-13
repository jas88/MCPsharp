using MCPsharp.Models.Database;
using MCPsharp.Services.Database;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Database;

[TestFixture]
public partial class ProjectDatabaseTests
{
    private static readonly string[] ClassKindFilter = ["Class"];
    private static readonly string[] TestNamespaceFilter = ["Test.Namespace"];

    private ProjectDatabase _db = null!;

    [SetUp]
    public async Task SetUp()
    {
        _db = new ProjectDatabase();
        await _db.OpenInMemoryAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _db.DisposeAsync();
    }

    #region Database Lifecycle Tests

    [Test]
    public async Task OpenInMemoryAsync_ShouldOpenDatabase()
    {
        // Arrange
        var db = new ProjectDatabase();

        // Act
        await db.OpenInMemoryAsync();

        // Assert
        Assert.That(db.IsOpen, Is.True);
        Assert.That(db.DatabasePath, Is.EqualTo(":memory:"));

        await db.DisposeAsync();
    }

    [Test]
    public async Task OpenOrCreateAsync_ShouldCreateDatabaseFile()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-project-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);
        var db = new ProjectDatabase();

        try
        {
            // Act
            await db.OpenOrCreateAsync(tempPath);

            // Assert
            Assert.That(db.IsOpen, Is.True);
            Assert.That(db.DatabasePath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(db.DatabasePath!), Is.True);

            await db.CloseAsync();
        }
        finally
        {
            await db.DisposeAsync();
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, true);
        }
    }

    [Test]
    public async Task CloseAsync_ShouldCloseOpenDatabase()
    {
        // Arrange
        var db = new ProjectDatabase();
        await db.OpenInMemoryAsync();

        // Act
        await db.CloseAsync();

        // Assert
        Assert.That(db.IsOpen, Is.False);

        await db.DisposeAsync();
    }

    [Test]
    public async Task OpenInMemoryAsync_WhenCalledTwice_ShouldCloseFirstConnection()
    {
        // Arrange
        var db = new ProjectDatabase();
        await db.OpenInMemoryAsync();

        // Act
        await db.OpenInMemoryAsync(); // Should close first connection

        // Assert
        Assert.That(db.IsOpen, Is.True);

        await db.DisposeAsync();
    }

    [Test]
    public void GetDatabasePath_ShouldReturnConsistentPath()
    {
        // Arrange
        var projectPath = "/test/project/path";

        // Act
        var path1 = ProjectDatabase.GetDatabasePath(projectPath);
        var path2 = ProjectDatabase.GetDatabasePath(projectPath);

        // Assert
        Assert.That(path1, Is.EqualTo(path2));
        Assert.That(path1, Does.EndWith(".db"));
    }

    [Test]
    public void ComputeProjectHash_ShouldReturnConsistentHash()
    {
        // Arrange
        var projectPath = "/test/project/path";

        // Act
        var hash1 = ProjectDatabase.ComputeProjectHash(projectPath);
        var hash2 = ProjectDatabase.ComputeProjectHash(projectPath);

        // Assert
        Assert.That(hash1, Is.EqualTo(hash2));
        Assert.That(hash1, Has.Length.EqualTo(16));
        Assert.That(hash1, Does.Match("^[a-f0-9]+$"));
    }

    #endregion

    #region Project Operations Tests

    [Test]
    public async Task GetOrCreateProjectAsync_CreatesNewProject()
    {
        // Arrange & Act
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Assert
        Assert.That(projectId, Is.GreaterThan(0));

        var project = await _db.GetProjectByIdAsync(projectId);
        Assert.That(project, Is.Not.Null);
        Assert.That(project!.Name, Is.EqualTo("TestProject"));
        Assert.That(project.RootPath, Is.EqualTo("/test/path"));
    }

    [Test]
    public async Task GetOrCreateProjectAsync_ReturnsExistingProject()
    {
        // Arrange
        var projectId1 = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var projectId2 = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Assert
        Assert.That(projectId2, Is.EqualTo(projectId1));
    }

    [Test]
    public async Task GetOrCreateProjectAsync_UpdatesOpenedAt()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var project1 = await _db.GetProjectByIdAsync(projectId);
        var firstOpenedAt = project1!.OpenedAt;

        await Task.Delay(100); // Ensure time difference

        // Act
        await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Assert
        var project2 = await _db.GetProjectByIdAsync(projectId);
        Assert.That(project2!.OpenedAt, Is.GreaterThan(firstOpenedAt));
    }

    [Test]
    public async Task GetProjectByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var project = await _db.GetProjectByIdAsync(999999);

        // Assert
        Assert.That(project, Is.Null);
    }

    [Test]
    public async Task GetProjectByPathAsync_ReturnsProject()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var project = await _db.GetProjectByPathAsync("/test/path");

        // Assert
        Assert.That(project, Is.Not.Null);
        Assert.That(project!.Id, Is.EqualTo(projectId));
        Assert.That(project.Name, Is.EqualTo("TestProject"));
    }

    [Test]
    public async Task UpdateProjectStatsAsync_UpdatesSolutionCount()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        await _db.UpdateProjectStatsAsync(projectId, solutionCount: 5);

        // Assert
        var project = await _db.GetProjectByIdAsync(projectId);
        Assert.That(project!.SolutionCount, Is.EqualTo(5));
    }

    [Test]
    public async Task UpdateProjectStatsAsync_UpdatesMultipleStats()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        await _db.UpdateProjectStatsAsync(projectId, solutionCount: 2, projectCount: 10, fileCount: 100);

        // Assert
        var project = await _db.GetProjectByIdAsync(projectId);
        Assert.That(project!.SolutionCount, Is.EqualTo(2));
        Assert.That(project.ProjectCount, Is.EqualTo(10));
        Assert.That(project.FileCount, Is.EqualTo(100));
    }

    [Test]
    public async Task SetProjectStructureAsync_StoresKeyValue()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        await _db.SetProjectStructureAsync(projectId, "test-key", "{\"value\":42}");

        // Assert
        var result = await _db.GetProjectStructureAsync(projectId, "test-key");
        Assert.That(result, Is.EqualTo("{\"value\":42}"));
    }

    [Test]
    public async Task SetProjectStructureAsync_UpdatesExistingKey()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        await _db.SetProjectStructureAsync(projectId, "test-key", "{\"value\":42}");

        // Act
        await _db.SetProjectStructureAsync(projectId, "test-key", "{\"value\":100}");

        // Assert
        var result = await _db.GetProjectStructureAsync(projectId, "test-key");
        Assert.That(result, Is.EqualTo("{\"value\":100}"));
    }

    [Test]
    public async Task GetProjectStructureAsync_ReturnsNull_WhenKeyNotFound()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var result = await _db.GetProjectStructureAsync(projectId, "nonexistent-key");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetProjectStructureKeysAsync_ReturnsAllKeys()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        await _db.SetProjectStructureAsync(projectId, "key1", "{}");
        await _db.SetProjectStructureAsync(projectId, "key2", "{}");
        await _db.SetProjectStructureAsync(projectId, "key3", "{}");

        // Act
        var keys = await _db.GetProjectStructureKeysAsync(projectId);

        // Assert
        Assert.That(keys, Has.Count.EqualTo(3));
        Assert.That(keys, Does.Contain("key1"));
        Assert.That(keys, Does.Contain("key2"));
        Assert.That(keys, Does.Contain("key3"));
    }

    [Test]
    public async Task DeleteProjectAsync_RemovesProject()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        await _db.DeleteProjectAsync(projectId);

        // Assert
        var project = await _db.GetProjectByIdAsync(projectId);
        Assert.That(project, Is.Null);
    }

    [Test]
    public async Task GetDatabaseStatsAsync_ReturnsCorrectCounts()
    {
        // Arrange
        _ = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var stats = await _db.GetDatabaseStatsAsync();

        // Assert
        Assert.That(stats, Does.ContainKey("projects"));
        Assert.That(stats["projects"], Is.EqualTo(1));
        Assert.That(stats, Does.ContainKey("files"));
        Assert.That(stats, Does.ContainKey("symbols"));
        Assert.That(stats, Does.ContainKey("symbol_references"));
    }

    #endregion

    #region Helper Methods

    private static DbSymbol CreateTestSymbol(
        long fileId,
        string name,
        string kind,
        string? ns = null,
        int line = 10)
    {
        return new DbSymbol
        {
            FileId = fileId,
            Name = name,
            Kind = kind,
            Namespace = ns,
            ContainingType = null,
            Line = line,
            Column = 5,
            EndLine = line + 5,
            EndColumn = 6,
            Accessibility = "public",
            Signature = null
        };
    }

    private static DbReference CreateTestReference(
        long fromSymbolId,
        long toSymbolId,
        long fileId,
        int line,
        string kind = "Call")
    {
        return new DbReference
        {
            FromSymbolId = fromSymbolId,
            ToSymbolId = toSymbolId,
            ReferenceKind = kind,
            FileId = fileId,
            Line = line,
            Column = 10
        };
    }

    #endregion
}
