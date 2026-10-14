using MCPsharp.Models.Database;
using MCPsharp.Services.Database;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Database;

public partial class ProjectDatabaseTests
{
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
        Assert.That(symbolId, Is.GreaterThan(0));

        var retrievedSymbol = await _db.GetSymbolByIdAsync(symbolId);
        Assert.That(retrievedSymbol, Is.Not.Null);
        Assert.That(retrievedSymbol!.Name, Is.EqualTo("TestClass"));
        Assert.That(retrievedSymbol.Kind, Is.EqualTo("Class"));
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
        Assert.That(symbols, Has.Count.EqualTo(2));
        Assert.That(symbols, Has.All.Matches<DbSymbol>(s => s is not null && s.Name.Contains("Test")));
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
        Assert.That(symbols, Has.Count.EqualTo(1));
        Assert.That(symbols[0].Name, Is.EqualTo("TestClass"));
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
        Assert.That(symbols, Has.Count.EqualTo(1));
        Assert.That(symbols[0].Name, Is.EqualTo("TestClass"));
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
        Assert.That(symbols, Has.Count.EqualTo(3));
        // Should be ordered by line, column
        Assert.That(symbols[0].Line, Is.EqualTo(1));
        Assert.That(symbols[1].Line, Is.EqualTo(5));
        Assert.That(symbols[2].Line, Is.EqualTo(10));
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
            kinds: ClassKindFilter,
            namespaces: TestNamespaceFilter);

        // Assert
        Assert.That(symbols, Has.Count.EqualTo(1));
        Assert.That(symbols[0].Name, Is.EqualTo("TestClass"));
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
        Assert.That(symbols, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SearchSymbolsAsync_KindsAndNamespacesWithoutQuery_AppliesEachFilter()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "A", "Class", ns: "N1"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "B", "Interface", ns: "N2"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "C", "Method", ns: "N1"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "D", "Class", ns: "N3"));

        // Act
        var both = await _db.SearchSymbolsAsync(kinds: ["Class", "Interface"], namespaces: ["N1", "N2"]);
        var kindsOnly = await _db.SearchSymbolsAsync(kinds: ["Class"]);
        var namespacesOnly = await _db.SearchSymbolsAsync(namespaces: ["N1"]);
        var emptyLists = await _db.SearchSymbolsAsync(kinds: [], namespaces: []);

        // Assert
        Assert.That(string.Join(",", both.Select(s => s.Name)), Is.EqualTo("A,B"));
        Assert.That(string.Join(",", kindsOnly.Select(s => s.Name)), Is.EqualTo("A,D"));
        Assert.That(string.Join(",", namespacesOnly.Select(s => s.Name)), Is.EqualTo("A,C"));
        Assert.That(emptyLists, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task SearchSymbolsAsync_TreatsLikeWildcardsInQueryLiterally()
    {
        // Arrange
        var projectId = await _db.GetOrCreateProjectAsync("/test/path", "TestProject");
        var fileId = await _db.UpsertFileAsync(projectId, "Program.cs", "hash1", 100, "csharp");

        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Foo_Bar", "Class", ns: "N1"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "FooXBar", "Class", ns: "N1"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Pct%Name", "Class", ns: "N1"));
        await _db.UpsertSymbolAsync(CreateTestSymbol(fileId, "Back\\Slash", "Class", ns: "N1"));

        // Act
        var underscore = await _db.SearchSymbolsAsync(query: "Foo_Bar");
        var underscoreFiltered = await _db.SearchSymbolsAsync(query: "Foo_Bar", kinds: ClassKindFilter, namespaces: ["N1"]);
        var percent = await _db.SearchSymbolsAsync(query: "%");
        var backslash = await _db.SearchSymbolsAsync(query: "k\\S", kinds: ClassKindFilter);
        var byName = await _db.FindSymbolsByNameAsync("o_B");

        // Assert
        Assert.That(underscore.Select(s => s.Name), Is.EqualTo(FooBarOnly));
        Assert.That(underscoreFiltered.Select(s => s.Name), Is.EqualTo(FooBarOnly));
        Assert.That(percent.Select(s => s.Name), Is.EqualTo(PercentNameOnly));
        Assert.That(backslash.Select(s => s.Name), Is.EqualTo(BackslashNameOnly));
        Assert.That(byName.Select(s => s.Name), Is.EqualTo(FooBarOnly));
    }

    [Test]
    public async Task SearchSymbolsByKindQuery_UsesKindIndex()
    {
        var plan = await ExplainSymbolSearchAsync(cmd => cmd.CommandText = "EXPLAIN QUERY PLAN " + ProjectDatabase.SearchSymbolsByKindQuery);

        Assert.That(plan, Has.Some.Contains("USING INDEX idx_symbols_kind"), string.Join("; ", plan));
    }

    [Test]
    public async Task SearchSymbolsByNamespaceQuery_UsesNamespaceIndex()
    {
        var plan = await ExplainSymbolSearchAsync(cmd => cmd.CommandText = "EXPLAIN QUERY PLAN " + ProjectDatabase.SearchSymbolsByNamespaceQuery);

        Assert.That(plan, Has.Some.Contains("USING INDEX idx_symbols_namespace"), string.Join("; ", plan));
    }

    private static async Task<List<string>> ExplainSymbolSearchAsync(Action<SqliteCommand> setCommandText)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = ProjectDatabaseSchema.CreateAllTables;
            await create.ExecuteNonQueryAsync();
        }

        await using var explain = connection.CreateCommand();
        setCommandText(explain);
        explain.Parameters.AddWithValue("$query", DBNull.Value);
        explain.Parameters.AddWithValue("$kinds", "[\"Class\"]");
        explain.Parameters.AddWithValue("$namespaces", "[\"N1\"]");
        explain.Parameters.AddWithValue("$limit", 100);

        var plan = new List<string>();
        await using var reader = await explain.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            plan.Add(reader.GetString(3));
        return plan;
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
        Assert.That(counts, Does.ContainKey("Class"));
        Assert.That(counts["Class"], Is.EqualTo(2));
        Assert.That(counts, Does.ContainKey("Method"));
        Assert.That(counts["Method"], Is.EqualTo(1));
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
        Assert.That(symbols, Is.Empty);
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
        Assert.That(allSymbols, Has.Count.EqualTo(3));
    }

    #endregion
}
