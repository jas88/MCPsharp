using MCPsharp.Models.Database;
using MCPsharp.Services.Database;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Database;

public partial class ProjectDatabaseTests
{
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
        Assert.That(refId, Is.GreaterThan(0));
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
        Assert.That(callers, Has.Count.EqualTo(1));
        Assert.That(callers[0].Caller.Name, Is.EqualTo("Caller"));
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
        Assert.That(callees, Has.Count.EqualTo(1));
        Assert.That(callees[0].Callee.Name, Is.EqualTo("Callee"));
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
        Assert.That(chain, Has.Count.EqualTo(2));
        Assert.That(chain, Does.Contain(chain.First(s => s.Name == "MethodA")));
        Assert.That(chain, Does.Contain(chain.First(s => s.Name == "MethodB")));
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
        Assert.That(chain, Has.Count.EqualTo(2));
        Assert.That(chain, Does.Contain(chain.First(s => s.Name == "MethodB")));
        Assert.That(chain, Does.Contain(chain.First(s => s.Name == "MethodC")));
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
        Assert.That(chain, Has.Count.EqualTo(2));
        Assert.That(chain, Does.Contain(chain.First(s => s.Name == "MethodB")));
        Assert.That(chain, Does.Contain(chain.First(s => s.Name == "MethodC")));
        Assert.That(chain, Does.Not.Contain(chain.FirstOrDefault(s => s.Name == "MethodD")));
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
        Assert.That(references, Has.Count.EqualTo(2));
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
        Assert.That(counts, Does.ContainKey("Call"));
        Assert.That(counts["Call"], Is.EqualTo(2));
        Assert.That(counts, Does.ContainKey("TypeUsage"));
        Assert.That(counts["TypeUsage"], Is.EqualTo(1));
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
        Assert.That(references, Is.Empty);
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
        Assert.That(callees, Has.Count.EqualTo(2));
    }

    #endregion
}
