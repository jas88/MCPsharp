using MCPsharp.Services.Roslyn;
using NUnit.Framework;
using SymbolKind = MCPsharp.Services.Roslyn.SymbolKind;

namespace MCPsharp.Tests.Services.Roslyn;

public partial class RenameSymbolServiceTests
{
    #region Conflict Detection Tests

    [Test]
    public async Task RenameWithNameCollision_ReturnsError()
    {
        // Arrange
        var code = @"
public class TestClass
{
    public string ExistingName { get; set; }
    public string OldName { get; set; }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "OldName",
            NewName = "ExistingName",
            SymbolKind = SymbolKind.Property,
            ForcePublicApiChange = true  // Need this since the properties are public
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Conflicts, Is.Not.Empty);
        Assert.That(result.Conflicts, Does.Contain(result.Conflicts.FirstOrDefault(c => c.Type == ConflictType.NameCollision)));
    }

    [Test]
    public async Task RenameHidingInheritedMember_ReturnsWarning()
    {
        // Arrange
        var code = @"
public class BaseClass
{
    public virtual void ExistingMethod() { }
}

public class DerivedClass : BaseClass
{
    public void OldMethod() { }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "OldMethod",
            NewName = "ExistingMethod",
            ContainingType = "DerivedClass",
            SymbolKind = SymbolKind.Method,
            ForcePublicApiChange = true
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        // Should have warning about hiding inherited member
        Assert.That(result.Conflicts, Does.Contain(result.Conflicts.FirstOrDefault(c => c.Type == ConflictType.HidesInheritedMember)));
    }

    #endregion

    #region Validation Tests

    [Test]
    public async Task RenameToInvalidIdentifier_ReturnsError()
    {
        // Arrange
        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", "public class Test { }");

        var request = new RenameRequest
        {
            OldName = "Test",
            NewName = "123Invalid", // Invalid identifier
            SymbolKind = SymbolKind.Class
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors[0], Does.Contain("not a valid C# identifier"));
    }

    [Test]
    public async Task RenameToKeyword_RequiresFlag()
    {
        // Arrange
        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", "public class Test { }");

        var request = new RenameRequest
        {
            OldName = "Test",
            NewName = "class", // C# keyword
            SymbolKind = SymbolKind.Class,
            AllowKeywordNames = false
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors[0], Does.Contain("is a C# keyword"));
    }

    [Test]
    public async Task RenameNonExistentSymbol_ReturnsError()
    {
        // Arrange
        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", "public class Test { }");

        var request = new RenameRequest
        {
            OldName = "NonExistent",
            NewName = "NewName",
            SymbolKind = SymbolKind.Class
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors[0], Does.Contain("not found"));
    }

    #endregion

    #region Preview Mode Tests

    [Test]
    public async Task RenameInPreviewMode_DoesNotApplyChanges()
    {
        // Arrange
        var originalCode = @"
public class OldName
{
    public void Test()
    {
        var instance = new OldName();
    }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        _ = await _workspace.AddInMemoryDocumentAsync("test.cs", originalCode);

        var request = new RenameRequest
        {
            OldName = "OldName",
            NewName = "NewName",
            SymbolKind = SymbolKind.Class,
            PreviewOnly = true,
            ForcePublicApiChange = true
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Preview, Is.Not.Null);
        Assert.That(result.Preview, Is.Not.Empty);

        // Verify original code is unchanged
        var document = _workspace.GetDocument("test.cs");
        Assert.That(document, Is.Not.Null);
        var text = await document.GetTextAsync();
        Assert.That(text.ToString(), Does.Contain("OldName"));
        Assert.That(text.ToString(), Does.Not.Contain("NewName"));
    }

    #endregion
}
