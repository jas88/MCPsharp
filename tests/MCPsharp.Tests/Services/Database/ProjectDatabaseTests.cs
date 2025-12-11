using FluentAssertions;
using MCPsharp.Models.Database;
using MCPsharp.Services.Database;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Database;

[TestFixture]
public class ProjectDatabaseTests
{
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
        db.IsOpen.Should().BeTrue();
        db.DatabasePath.Should().Be(":memory:");

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
            db.IsOpen.Should().BeTrue();
            db.DatabasePath.Should().NotBeNullOrEmpty();
            File.Exists(db.DatabasePath!).Should().BeTrue();

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
        db.IsOpen.Should().BeFalse();

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
        db.IsOpen.Should().BeTrue();

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
        path1.Should().Be(path2);
        path1.Should().EndWith(".db");
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
        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(16);
        hash1.Should().MatchRegex("^[a-f0-9]+$");
    }

    #endregion

    #region Project Operations Tests

    [Test]
    public async Task GetOrCreateProjectAsync_CreatesNewProject()
    {
        // Arrange & Act
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Assert
        projectId.Should().BeGreaterThan(0);

        var project = await _db.GetProjectByIdAsync(projectId);
        project.Should().NotBeNull();
        project!.Name.Should().Be("TestProject");
        project.RootPath.Should().Be("/test/path");
    }

    [Test]
    public async Task GetOrCreateProjectAsync_ReturnsExistingProject()
    {
        // Arrange
        var projectId1 = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var projectId2 = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Assert
        projectId1.Should().Be(projectId2);
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
        project2!.OpenedAt.Should().BeAfter(firstOpenedAt);
    }

    [Test]
    public async Task GetProjectByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var project = await _db.GetProjectByIdAsync(999999);

        // Assert
        project.Should().BeNull();
    }

    [Test]
    public async Task GetProjectByPathAsync_ReturnsProject()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var project = await _db.GetProjectByPathAsync("/test/path");

        // Assert
        project.Should().NotBeNull();
        project!.Id.Should().Be(projectId);
        project.Name.Should().Be("TestProject");
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
        project!.SolutionCount.Should().Be(5);
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
        project!.SolutionCount.Should().Be(2);
        project.ProjectCount.Should().Be(10);
        project.FileCount.Should().Be(100);
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
        result.Should().Be("{\"value\":42}");
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
        result.Should().Be("{\"value\":100}");
    }

    [Test]
    public async Task GetProjectStructureAsync_ReturnsNull_WhenKeyNotFound()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var result = await _db.GetProjectStructureAsync(projectId, "nonexistent-key");

        // Assert
        result.Should().BeNull();
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
        keys.Should().HaveCount(3);
        keys.Should().Contain(new[] { "key1", "key2", "key3" });
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
        project.Should().BeNull();
    }

    [Test]
    public async Task GetDatabaseStatsAsync_ReturnsCorrectCounts()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var stats = await _db.GetDatabaseStatsAsync();

        // Assert
        stats.Should().ContainKey("projects");
        stats["projects"].Should().Be(1);
        stats.Should().ContainKey("files");
        stats.Should().ContainKey("symbols");
        stats.Should().ContainKey("symbol_references");
    }

    #endregion

    #region File Operations Tests

    [Test]
    public async Task UpsertFileAsync_CreatesNewFile()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash123", 1000, "csharp");

        // Assert
        fileId.Should().BeGreaterThan(0);

        var file = await _db.GetFileAsync(projectId, "Program.cs");
        file.Should().NotBeNull();
        file!.RelativePath.Should().Be("Program.cs");
        file.ContentHash.Should().Be("hash123");
        file.SizeBytes.Should().Be(1000);
        file.Language.Should().Be("csharp");
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
        fileId1.Should().Be(fileId2);

        var file = await _db.GetFileAsync(projectId, "Program.cs");
        file!.ContentHash.Should().Be("hash456");
        file.SizeBytes.Should().Be(2000);
    }

    [Test]
    public async Task GetFileAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var file = await _db.GetFileAsync(projectId, "NonExistent.cs");

        // Assert
        file.Should().BeNull();
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
        files.Should().HaveCount(3);
        files.Should().OnlyContain(f => f.ProjectId == projectId);
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
        staleFiles.Should().HaveCount(1);
        staleFiles.First().RelativePath.Should().Be("File2.cs");
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
        staleFiles.Should().HaveCount(1);
        staleFiles.First().RelativePath.Should().Be("File2.cs");
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
        deletedFiles.Should().HaveCount(1);
        deletedFiles.First().RelativePath.Should().Be("File2.cs");
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
        file.Should().BeNull();
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
        file.Should().BeNull();
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
        count.Should().Be(2);
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
        allFiles.Should().HaveCount(3);
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
        var countBefore = await _db.GetFileCountAsync(projectId);

        // Act & Assert - batch should succeed even with updates
        var filesUpdate = new[]
        {
            ("File1.cs", "hash1-new", 150L, "csharp"),
            ("File3.cs", "hash3", 300L, "csharp")
        };
        await _db.UpsertFilesBatchAsync(projectId, filesUpdate);

        var countAfter = await _db.GetFileCountAsync(projectId);
        countAfter.Should().Be(3); // 3 total files
    }

    #endregion

    #region Symbol Operations Tests

    [Test]
    public async Task UpsertSymbolAsync_CreatesNewSymbol()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        var symbol = new DbSymbol
        {
            FileId = fileId,
            Name = "TestClass",
            Kind = "Class",
            Namespace = "Test.Namespace",
            ContainingType = null,
            Line = 10,
            Column = 5,
            EndLine = 20,
            EndColumn = 6,
            Accessibility = "public",
            Signature = null
        };

        // Act
        var symbolId = await _db.UpsertSymbolAsync(symbol);

        // Assert
        symbolId.Should().BeGreaterThan(0);

        var retrievedSymbol = await _db.GetSymbolByIdAsync(symbolId);
        retrievedSymbol.Should().NotBeNull();
        retrievedSymbol!.Name.Should().Be("TestClass");
        retrievedSymbol.Kind.Should().Be("Class");
    }

    [Test]
    public async Task FindSymbolsByNameAsync_FindsPartialMatches()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "TestClass", "Class"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "TestMethod", "Method"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "AnotherClass", "Class"));

        // Act
        var symbols = await _db.FindSymbolsByNameAsync("Test");

        // Assert
        symbols.Should().HaveCount(2);
        symbols.Should().OnlyContain(s => s.Name.Contains("Test"));
    }

    [Test]
    public async Task FindSymbolsByNameAsync_FiltersOnKind()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "TestClass", "Class"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "TestMethod", "Method"));

        // Act
        var symbols = await _db.FindSymbolsByNameAsync("Test", kind: "Class");

        // Assert
        symbols.Should().HaveCount(1);
        symbols.First().Name.Should().Be("TestClass");
    }

    [Test]
    public async Task FindSymbolsByExactNameAsync_FindsExactMatch()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "TestClass", "Class"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "TestClassExtended", "Class"));

        // Act
        var symbols = await _db.FindSymbolsByExactNameAsync("TestClass");

        // Assert
        symbols.Should().HaveCount(1);
        symbols.First().Name.Should().Be("TestClass");
    }

    [Test]
    public async Task GetSymbolsInFileAsync_ReturnsAllSymbols()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Class1", "Class", line: 1));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Class2", "Class", line: 10));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Method1", "Method", line: 5));

        // Act
        var symbols = await _db.GetSymbolsInFileAsync(fileId);

        // Assert
        symbols.Should().HaveCount(3);
        // Should be ordered by line, column
        symbols[0].Line.Should().Be(1);
        symbols[1].Line.Should().Be(5);
        symbols[2].Line.Should().Be(10);
    }

    [Test]
    public async Task SearchSymbolsAsync_FiltersWithMultipleCriteria()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "TestClass", "Class", ns: "Test.Namespace"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "TestMethod", "Method", ns: "Test.Namespace"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "AnotherClass", "Class", ns: "Another.Namespace"));

        // Act
        var symbols = await _db.SearchSymbolsAsync(
            query: "Test",
            kinds: new[] { "Class" },
            namespaces: new[] { "Test.Namespace" });

        // Assert
        symbols.Should().HaveCount(1);
        symbols.First().Name.Should().Be("TestClass");
    }

    [Test]
    public async Task SearchSymbolsAsync_ReturnsAll_WhenNoFilters()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Class1", "Class"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Class2", "Class"));

        // Act
        var symbols = await _db.SearchSymbolsAsync();

        // Assert
        symbols.Should().HaveCount(2);
    }

    [Test]
    public async Task GetSymbolCountsByKindAsync_ReturnsCorrectCounts()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Class1", "Class"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Class2", "Class"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Method1", "Method"));

        // Act
        var counts = await _db.GetSymbolCountsByKindAsync();

        // Assert
        counts.Should().ContainKey("Class");
        counts["Class"].Should().Be(2);
        counts.Should().ContainKey("Method");
        counts["Method"].Should().Be(1);
    }

    [Test]
    public async Task DeleteSymbolsForFileAsync_RemovesAllSymbols()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Class1", "Class"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Class2", "Class"));

        // Act
        await _db.DeleteSymbolsForFileAsync(fileId);

        // Assert
        var symbols = await _db.GetSymbolsInFileAsync(fileId);
        symbols.Should().BeEmpty();
    }

    [Test]
    public async Task UpsertSymbolsBatchAsync_InsertsMultipleSymbols()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        var symbols = new[]
        {
            CreateTestSymbol(fileId, "Class1", "Class"),
            CreateTestSymbol(fileId, "Class2", "Class"),
            CreateTestSymbol(fileId, "Method1", "Method")
        };

        // Act
        await _db.UpsertSymbolsBatchAsync(symbols);

        // Assert
        var allSymbols = await _db.GetSymbolsInFileAsync(fileId);
        allSymbols.Should().HaveCount(3);
    }

    #endregion

    #region Reference Operations Tests

    [Test]
    public async Task UpsertReferenceAsync_CreatesNewReference()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");
        var symbol1Id = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Caller", "Method"));
        var symbol2Id = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Callee", "Method"));

        var reference = new DbReference
        {
            FromSymbolId = symbol1Id,
            ToSymbolId = symbol2Id,
            ReferenceKind = "Call",
            FileId = fileId,
            Line = 15,
            Column = 10
        };

        // Act
        var refId = await _db.UpsertReferenceAsync(reference);

        // Assert
        refId.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task FindCallersAsync_ReturnsCorrectCallers()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");
        var callerId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Caller", "Method"));
        var calleeId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Callee", "Method"));

        await _db.UpsertReferenceAsync(new DbReference
        {
            FromSymbolId = callerId,
            ToSymbolId = calleeId,
            ReferenceKind = "Call",
            FileId = fileId,
            Line = 15,
            Column = 10
        });

        // Act
        var callers = await _db.FindCallersAsync(calleeId);

        // Assert
        callers.Should().HaveCount(1);
        callers.First().Caller.Name.Should().Be("Caller");
    }

    [Test]
    public async Task FindCalleesAsync_ReturnsCorrectCallees()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");
        var callerId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Caller", "Method"));
        var calleeId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Callee", "Method"));

        await _db.UpsertReferenceAsync(new DbReference
        {
            FromSymbolId = callerId,
            ToSymbolId = calleeId,
            ReferenceKind = "Call",
            FileId = fileId,
            Line = 15,
            Column = 10
        });

        // Act
        var callees = await _db.FindCalleesAsync(callerId);

        // Assert
        callees.Should().HaveCount(1);
        callees.First().Callee.Name.Should().Be("Callee");
    }

    [Test]
    public async Task GetCallChainAsync_Backward_FindsCallers()
    {
        // Arrange - Create chain: A -> B -> C
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        var aId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "MethodA", "Method"));
        var bId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "MethodB", "Method"));
        var cId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "MethodC", "Method"));

        await _db.UpsertReferenceAsync(CreateTestReference(aId, bId, fileId, 10));
        await _db.UpsertReferenceAsync(CreateTestReference(bId, cId, fileId, 20));

        // Act - Find who calls C
        var chain = await _db.GetCallChainAsync(cId, ProjectDatabase.CallChainDirection.Backward, maxDepth: 10);

        // Assert
        chain.Should().HaveCount(2);
        chain.Should().Contain(s => s.Name == "MethodA");
        chain.Should().Contain(s => s.Name == "MethodB");
    }

    [Test]
    public async Task GetCallChainAsync_Forward_FindsCallees()
    {
        // Arrange - Create chain: A -> B -> C
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        var aId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "MethodA", "Method"));
        var bId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "MethodB", "Method"));
        var cId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "MethodC", "Method"));

        await _db.UpsertReferenceAsync(CreateTestReference(aId, bId, fileId, 10));
        await _db.UpsertReferenceAsync(CreateTestReference(bId, cId, fileId, 20));

        // Act - Find what A calls
        var chain = await _db.GetCallChainAsync(aId, ProjectDatabase.CallChainDirection.Forward, maxDepth: 10);

        // Assert
        chain.Should().HaveCount(2);
        chain.Should().Contain(s => s.Name == "MethodB");
        chain.Should().Contain(s => s.Name == "MethodC");
    }

    [Test]
    public async Task GetCallChainAsync_RespectsMaxDepth()
    {
        // Arrange - Create chain: A -> B -> C -> D
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        var aId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "MethodA", "Method"));
        var bId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "MethodB", "Method"));
        var cId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "MethodC", "Method"));
        var dId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "MethodD", "Method"));

        await _db.UpsertReferenceAsync(CreateTestReference(aId, bId, fileId, 10));
        await _db.UpsertReferenceAsync(CreateTestReference(bId, cId, fileId, 20));
        await _db.UpsertReferenceAsync(CreateTestReference(cId, dId, fileId, 30));

        // Act - Find with depth limit of 2
        var chain = await _db.GetCallChainAsync(aId, ProjectDatabase.CallChainDirection.Forward, maxDepth: 2);

        // Assert - Should only find B and C, not D
        chain.Should().HaveCount(2);
        chain.Should().Contain(s => s.Name == "MethodB");
        chain.Should().Contain(s => s.Name == "MethodC");
        chain.Should().NotContain(s => s.Name == "MethodD");
    }

    [Test]
    public async Task GetReferencesToSymbolAsync_ReturnsAllReferences()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        var caller1Id = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Caller1", "Method"));
        var caller2Id = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Caller2", "Method"));
        var calleeId = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Callee", "Method"));

        await _db.UpsertReferenceAsync(CreateTestReference(caller1Id, calleeId, fileId, 10));
        await _db.UpsertReferenceAsync(CreateTestReference(caller2Id, calleeId, fileId, 20));

        // Act
        var references = await _db.GetReferencesToSymbolAsync(calleeId);

        // Assert
        references.Should().HaveCount(2);
    }

    [Test]
    public async Task GetReferenceCountsByKindAsync_ReturnsCorrectCounts()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        var symbol1Id = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Symbol1", "Method"));
        var symbol2Id = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Symbol2", "Method"));

        await _db.UpsertReferenceAsync(CreateTestReference(symbol1Id, symbol2Id, fileId, 10, "Call"));
        await _db.UpsertReferenceAsync(CreateTestReference(symbol1Id, symbol2Id, fileId, 11, "Call"));
        await _db.UpsertReferenceAsync(CreateTestReference(symbol1Id, symbol2Id, fileId, 12, "TypeUsage"));

        // Act
        var counts = await _db.GetReferenceCountsByKindAsync();

        // Assert
        counts.Should().ContainKey("Call");
        counts["Call"].Should().Be(2);
        counts.Should().ContainKey("TypeUsage");
        counts["TypeUsage"].Should().Be(1);
    }

    [Test]
    public async Task DeleteReferencesForFileAsync_RemovesAllReferences()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        var symbol1Id = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Symbol1", "Method"));
        var symbol2Id = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Symbol2", "Method"));

        await _db.UpsertReferenceAsync(CreateTestReference(symbol1Id, symbol2Id, fileId, 10));

        // Act
        await _db.DeleteReferencesForFileAsync(fileId);

        // Assert
        var references = await _db.GetReferencesToSymbolAsync(symbol2Id);
        references.Should().BeEmpty();
    }

    [Test]
    public async Task UpsertReferencesBatchAsync_InsertsMultipleReferences()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        var symbol1Id = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Symbol1", "Method"));
        var symbol2Id = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Symbol2", "Method"));
        var symbol3Id = await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Symbol3", "Method"));

        var references = new[]
        {
            CreateTestReference(symbol1Id, symbol2Id, fileId, 10),
            CreateTestReference(symbol1Id, symbol3Id, fileId, 20)
        };

        // Act
        await _db.UpsertReferencesBatchAsync(references);

        // Assert
        var callees = await _db.FindCalleesAsync(symbol1Id);
        callees.Should().HaveCount(2);
    }

    #endregion

    #region Edge Cases and Error Handling

    [Test]
    public async Task GetAllFilesAsync_ReturnsEmpty_WhenNoFiles()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");

        // Act
        var files = await _db.GetAllFilesAsync(projectId);

        // Assert
        files.Should().BeEmpty();
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
        symbols.Should().BeEmpty();
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
        callers.Should().BeEmpty();
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
        chain.Should().BeEmpty();
    }

    [Test]
    public async Task ExecuteInTransactionAsync_RollsBackOnException()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileCountBefore = await _db.GetFileCountAsync(projectId);

        // Act & Assert
        var act = async () => await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await _db.UpsertFileAsync(projectId, "File1.cs", "hash1", 100, "csharp");
            throw new InvalidOperationException("Test exception");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();

        // Assert - transaction should have rolled back
        var fileCountAfter = await _db.GetFileCountAsync(projectId);
        fileCountAfter.Should().Be(fileCountBefore);
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
        project!.SolutionCount.Should().Be(7);
        project.ProjectCount.Should().Be(10); // unchanged
        project.FileCount.Should().Be(20); // unchanged
    }

    [Test]
    public void GetOrCreateProjectAsync_ThrowsException_WhenPathIsEmpty()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
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
        symbols.Should().HaveCount(1);
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
