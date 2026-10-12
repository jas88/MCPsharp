using MCPsharp.Services.Database;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Database;

public partial class ProjectDatabaseTests
{
    #region Edge Cases and Error Handling

    [Test]
    public async Task GetAllFilesAsync_ReturnsEmpty_WhenNoFiles()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var files = await _db.GetAllFilesAsync(projectId);

        // Assert
        Assert.That(files, Is.Empty);
    }

    [Test]
    public async Task GetSymbolsInFileAsync_ReturnsEmpty_WhenNoSymbols()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        // Act
        var symbols = await _db.GetSymbolsInFileAsync(fileId);

        // Assert
        Assert.That(symbols, Is.Empty);
    }

    [Test]
    public async Task FindCallersAsync_ReturnsEmpty_WhenNoCallers()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");
        var symbolId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Method", "Method"));

        // Act
        var callers = await _db.FindCallersAsync(symbolId);

        // Assert
        Assert.That(callers, Is.Empty);
    }

    [Test]
    public async Task GetCallChainAsync_ReturnsEmpty_WhenNoChain()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");
        var symbolId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Method", "Method"));

        // Act
        var chain = await _db.GetCallChainAsync(symbolId, ProjectDatabase.CallChainDirection.Forward);

        // Assert
        Assert.That(chain, Is.Empty);
    }

    [Test]
    public async Task ExecuteInTransactionAsync_RollsBackOnException()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileCountBefore = await _db.GetFileCountAsync(projectId);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await _db.UpsertFileAsync(projectId, "File1.cs", "hash1", 100, "csharp");
            throw new InvalidOperationException("Test exception");
        }));

        // Assert - transaction should have rolled back
        var fileCountAfter = await _db.GetFileCountAsync(projectId);
        Assert.That(fileCountAfter, Is.EqualTo(fileCountBefore));
    }

    [Test]
    public async Task UpdateProjectStatsAsync_IgnoresNullValues()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        await _db.UpdateProjectStatsAsync(projectId, solutionCount: 5, projectCount: 10, fileCount: 20);

        // Act - Update only solution count
        await _db.UpdateProjectStatsAsync(projectId, solutionCount: 7);

        // Assert
        var project = await _db.GetProjectByIdAsync(projectId);
        Assert.That(project!.SolutionCount, Is.EqualTo(7));
        Assert.That(project.ProjectCount, Is.EqualTo(10)); // unchanged
        Assert.That(project.FileCount, Is.EqualTo(20)); // unchanged
    }

    [Test]
    public async Task GetOrCreateProjectAsync_ThrowsException_WhenPathIsEmpty()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _db.GetOrCreateProjectAsync("", "TestProject"));
    }

    [Test]
    public async Task SearchSymbolsAsync_HandlesEmptyFilters()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "TestClass", "Class"));

        // Act
        var symbols = await _db.SearchSymbolsAsync(
            kinds: Array.Empty<string>(),
            namespaces: Array.Empty<string>());

        // Assert
        Assert.That(symbols, Has.Count.EqualTo(1));
    }

    #endregion
}
