using MCPsharp.Services.Database;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

public partial class SqliteCacheIntegrationTests
{
    [Test]
    public async Task Database_PersistsSymbolsAcrossOperations()
    {
        // Arrange
        var projectId = await _database.GetOrCreateProjectAsync(_testProjectPath, "TestProject");

        var fileId = await _database.UpsertFileAsync(
            projectId,
            "src/Test.cs",
            "abc123hash",
            1000,
            "csharp");

        var symbols = new[]
        {
            new Models.Database.DbSymbol
            {
                FileId = fileId,
                Name = "TestClass",
                Kind = "Class",
                Namespace = "TestNamespace",
                Line = 10,
                Column = 1,
                EndLine = 50,
                EndColumn = 1,
                Accessibility = "public"
            },
            new Models.Database.DbSymbol
            {
                FileId = fileId,
                Name = "TestMethod",
                Kind = "Method",
                Namespace = "TestNamespace",
                ContainingType = "TestClass",
                Line = 15,
                Column = 5,
                EndLine = 25,
                EndColumn = 5,
                Accessibility = "public",
                Signature = "void TestMethod()"
            }
        };

        // Act
        await _database.UpsertSymbolsBatchAsync(symbols);
        var foundSymbols = await _database.FindSymbolsByNameAsync("Test");

        // Assert
        Assert.That(foundSymbols, Has.Count.EqualTo(2));
        Assert.That(foundSymbols.Any(s => s.Name == "TestClass"), Is.True);
        Assert.That(foundSymbols.Any(s => s.Name == "TestMethod"), Is.True);

        // Verify class details
        var testClass = foundSymbols.First(s => s.Name == "TestClass");
        Assert.That(testClass.Kind, Is.EqualTo("Class"));
        Assert.That(testClass.Namespace, Is.EqualTo("TestNamespace"));
        Assert.That(testClass.Accessibility, Is.EqualTo("public"));

        // Verify method details
        var testMethod = foundSymbols.First(s => s.Name == "TestMethod");
        Assert.That(testMethod.Kind, Is.EqualTo("Method"));
        Assert.That(testMethod.ContainingType, Is.EqualTo("TestClass"));
        Assert.That(testMethod.Signature, Is.EqualTo("void TestMethod()"));
    }

    [Test]
    public async Task Database_CallChainAnalysis()
    {
        // Arrange - Create a call chain: A -> B -> C
        var projectId = await _database.GetOrCreateProjectAsync(_testProjectPath, "Test");
        var fileId = await _database.UpsertFileAsync(projectId, "test.cs", "hash", 100, "csharp");

        // Create symbols
        var symbolA = new Models.Database.DbSymbol
        {
            FileId = fileId,
            Name = "MethodA",
            Kind = "Method",
            Line = 1,
            Column = 1,
            EndLine = 5,
            EndColumn = 1,
            Accessibility = "public"
        };
        var symbolB = new Models.Database.DbSymbol
        {
            FileId = fileId,
            Name = "MethodB",
            Kind = "Method",
            Line = 10,
            Column = 1,
            EndLine = 15,
            EndColumn = 1,
            Accessibility = "public"
        };
        var symbolC = new Models.Database.DbSymbol
        {
            FileId = fileId,
            Name = "MethodC",
            Kind = "Method",
            Line = 20,
            Column = 1,
            EndLine = 25,
            EndColumn = 1,
            Accessibility = "public"
        };

        await _database.UpsertSymbolsBatchAsync(new[] { symbolA, symbolB, symbolC });

        // Get the IDs
        var symbols = await _database.FindSymbolsByExactNameAsync("MethodA");
        var idA = symbols[0].Id;
        symbols = await _database.FindSymbolsByExactNameAsync("MethodB");
        var idB = symbols[0].Id;
        symbols = await _database.FindSymbolsByExactNameAsync("MethodC");
        var idC = symbols[0].Id;

        // Create references: A calls B, B calls C
        var references = new[]
        {
            new Models.Database.DbReference
            {
                FromSymbolId = idA, ToSymbolId = idB, ReferenceKind = "Call",
                FileId = fileId, Line = 3, Column = 5
            },
            new Models.Database.DbReference
            {
                FromSymbolId = idB, ToSymbolId = idC, ReferenceKind = "Call",
                FileId = fileId, Line = 12, Column = 5
            }
        };

        await _database.UpsertReferencesBatchAsync(references);

        // Act - Find call chain from A forward
        var callChain = await _database.GetCallChainAsync(idA, ProjectDatabase.CallChainDirection.Forward, 5);

        // Assert
        Assert.That(callChain, Has.Count.GreaterThanOrEqualTo(2)); // B and C
        Assert.That(callChain.Any(s => s.Name == "MethodB"), Is.True);
        Assert.That(callChain.Any(s => s.Name == "MethodC"), Is.True);

        // Verify backward call chain from C
        var backwardChain = await _database.GetCallChainAsync(idC, ProjectDatabase.CallChainDirection.Backward, 5);
        Assert.That(backwardChain.Any(s => s.Name == "MethodA"), Is.True);
        Assert.That(backwardChain.Any(s => s.Name == "MethodB"), Is.True);
    }

    [Test]
    public async Task Database_FindCallersAndCallees()
    {
        // Arrange
        var projectId = await _database.GetOrCreateProjectAsync(_testProjectPath, "Test");
        var fileId = await _database.UpsertFileAsync(projectId, "test.cs", "hash", 100, "csharp");

        var symbols = new[]
        {
            new Models.Database.DbSymbol
            {
                FileId = fileId, Name = "Caller", Kind = "Method",
                Line = 1, Column = 1, EndLine = 5, EndColumn = 1, Accessibility = "public"
            },
            new Models.Database.DbSymbol
            {
                FileId = fileId, Name = "Target", Kind = "Method",
                Line = 10, Column = 1, EndLine = 15, EndColumn = 1, Accessibility = "public"
            },
            new Models.Database.DbSymbol
            {
                FileId = fileId, Name = "Callee", Kind = "Method",
                Line = 20, Column = 1, EndLine = 25, EndColumn = 1, Accessibility = "public"
            }
        };

        await _database.UpsertSymbolsBatchAsync(symbols);

        var allSymbols = await _database.FindSymbolsByExactNameAsync("Caller");
        var callerId = allSymbols[0].Id;
        allSymbols = await _database.FindSymbolsByExactNameAsync("Target");
        var targetId = allSymbols[0].Id;
        allSymbols = await _database.FindSymbolsByExactNameAsync("Callee");
        var calleeId = allSymbols[0].Id;

        var references = new[]
        {
            new Models.Database.DbReference
            {
                FromSymbolId = callerId, ToSymbolId = targetId, ReferenceKind = "Call",
                FileId = fileId, Line = 3, Column = 5
            },
            new Models.Database.DbReference
            {
                FromSymbolId = targetId, ToSymbolId = calleeId, ReferenceKind = "Call",
                FileId = fileId, Line = 12, Column = 5
            }
        };

        await _database.UpsertReferencesBatchAsync(references);

        // Act
        var callers = await _database.FindCallersAsync(targetId);
        var callees = await _database.FindCalleesAsync(targetId);

        // Assert
        Assert.That(callers, Has.Count.EqualTo(1));
        Assert.That(callers[0].Caller.Name, Is.EqualTo("Caller"));
        Assert.That(callers[0].Reference.ReferenceKind, Is.EqualTo("Call"));

        Assert.That(callees, Has.Count.EqualTo(1));
        Assert.That(callees[0].Callee.Name, Is.EqualTo("Callee"));
        Assert.That(callees[0].Reference.ReferenceKind, Is.EqualTo("Call"));
    }
}
