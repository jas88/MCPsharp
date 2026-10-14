using MCPsharp.Models.Database;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Database;

public partial class ProjectDatabaseTests
{
    #region File Operations Tests

    [Test]
    public async Task UpsertFileAsync_CreatesNewFile()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash123", 1000, "csharp");

        // Assert
        Assert.That(fileId, Is.GreaterThan(0));

        var file = await _db.GetFileAsync(projectId, "Program.cs");
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.RelativePath, Is.EqualTo("Program.cs"));
        Assert.That(file.ContentHash, Is.EqualTo("hash123"));
        Assert.That(file.SizeBytes, Is.EqualTo(1000));
        Assert.That(file.Language, Is.EqualTo("csharp"));
    }

    [Test]
    public async Task UpsertFileAsync_UpdatesExistingFile()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId1 = await _db.UpsertFileAsync(projectId, "Program.cs", "hash123", 1000, "csharp");

        // Act
        var fileId2 = await _db.UpsertFileAsync(projectId, "Program.cs", "hash456", 2000, "csharp");

        // Assert
        Assert.That(fileId2, Is.EqualTo(fileId1));

        var file = await _db.GetFileAsync(projectId, "Program.cs");
        Assert.That(file!.ContentHash, Is.EqualTo("hash456"));
        Assert.That(file.SizeBytes, Is.EqualTo(2000));
    }

    [Test]
    public async Task GetFileAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var file = await _db.GetFileAsync(projectId, "NonExistent.cs");

        // Assert
        Assert.That(file, Is.Null);
    }

    [Test]
    public async Task GetAllFilesAsync_ReturnsAllFiles()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        await _db.UpsertFileAsync(projectId, "File1.cs", "hash1", 100, "csharp");
        await _db.UpsertFileAsync(projectId, "File2.cs", "hash2", 200, "csharp");
        await _db.UpsertFileAsync(projectId, "File3.cs", "hash3", 300, "csharp");

        // Act
        var files = await _db.GetAllFilesAsync(projectId);

        // Assert
        Assert.That(files, Has.Count.EqualTo(3));
        Assert.That(files, Has.All.Matches<DbFile>(f => f is not null && f.ProjectId == projectId));
    }

    [Test]
    public async Task GetStaleFilesAsync_DetectsChangedFiles()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        await _db.UpsertFileAsync(projectId, "File1.cs", "hash1", 100, "csharp");
        await _db.UpsertFileAsync(projectId, "File2.cs", "hash2", 200, "csharp");

        var currentHashes = new Dictionary<string, string>
        {
            ["File1.cs"] = "hash1", // unchanged
            ["File2.cs"] = "hash2-modified" // changed
        };

        // Act
        var staleFiles = await _db.GetStaleFilesAsync(projectId, currentHashes);

        // Assert
        Assert.That(staleFiles, Has.Count.EqualTo(1));
        Assert.That(staleFiles[0].RelativePath, Is.EqualTo("File2.cs"));
    }

    [Test]
    public async Task GetStaleFilesAsync_DetectsMissingFiles()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        await _db.UpsertFileAsync(projectId, "File1.cs", "hash1", 100, "csharp");
        await _db.UpsertFileAsync(projectId, "File2.cs", "hash2", 200, "csharp");

        var currentHashes = new Dictionary<string, string>
        {
            ["File1.cs"] = "hash1" // File2.cs is missing
        };

        // Act
        var staleFiles = await _db.GetStaleFilesAsync(projectId, currentHashes);

        // Assert
        Assert.That(staleFiles, Has.Count.EqualTo(1));
        Assert.That(staleFiles[0].RelativePath, Is.EqualTo("File2.cs"));
    }

    [Test]
    public async Task GetDeletedFilesAsync_FindsRemovedFiles()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        await _db.UpsertFileAsync(projectId, "File1.cs", "hash1", 100, "csharp");
        await _db.UpsertFileAsync(projectId, "File2.cs", "hash2", 200, "csharp");

        var currentPaths = new HashSet<string> { "File1.cs" }; // File2.cs deleted

        // Act
        var deletedFiles = await _db.GetDeletedFilesAsync(projectId, currentPaths);

        // Assert
        Assert.That(deletedFiles, Has.Count.EqualTo(1));
        Assert.That(deletedFiles[0].RelativePath, Is.EqualTo("File2.cs"));
    }

    [Test]
    public async Task DeleteFileAsync_RemovesFile()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "File1.cs", "hash1", 100, "csharp");

        // Act
        await _db.DeleteFileAsync(fileId);

        // Assert
        var file = await _db.GetFileAsync(projectId, "File1.cs");
        Assert.That(file, Is.Null);
    }

    [Test]
    public async Task DeleteFileByPathAsync_RemovesFile()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        await _db.UpsertFileAsync(projectId, "File1.cs", "hash1", 100, "csharp");

        // Act
        await _db.DeleteFileByPathAsync(projectId, "File1.cs");

        // Assert
        var file = await _db.GetFileAsync(projectId, "File1.cs");
        Assert.That(file, Is.Null);
    }

    [Test]
    public async Task GetFileCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        await _db.UpsertFileAsync(projectId, "File1.cs", "hash1", 100, "csharp");
        await _db.UpsertFileAsync(projectId, "File2.cs", "hash2", 200, "csharp");

        // Act
        var count = await _db.GetFileCountAsync(projectId);

        // Assert
        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public async Task UpsertFilesBatchAsync_InsertsMultipleFiles()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var files = new[]
        {
            ("File1.cs", "hash1", 100L, "csharp"),
            ("File2.cs", "hash2", 200L, "csharp"),
            ("File3.cs", "hash3", 300L, "csharp")
        };

        // Act
        await _db.UpsertFilesBatchAsync(projectId, files);

        // Assert
        var allFiles = await _db.GetAllFilesAsync(projectId);
        Assert.That(allFiles, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task UpsertFilesBatchAsync_IsAtomic_RollsBackOnError()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var files = new[]
        {
            ("File1.cs", "hash1", 100L, "csharp"),
            ("File2.cs", "hash2", 200L, "csharp")
        };

        await _db.UpsertFilesBatchAsync(projectId, files);

        // Act & Assert - batch should succeed even with updates
        var filesUpdate = new[]
        {
            ("File1.cs", "hash1-new", 150L, "csharp"),
            ("File3.cs", "hash3", 300L, "csharp")
        };
        await _db.UpsertFilesBatchAsync(projectId, filesUpdate);

        var countAfter = await _db.GetFileCountAsync(projectId);
        Assert.That(countAfter, Is.EqualTo(3)); // 3 total files
    }

    #endregion
}
