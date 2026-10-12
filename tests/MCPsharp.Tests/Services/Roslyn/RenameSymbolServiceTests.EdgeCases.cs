using NUnit.Framework;
using MCPsharp.Services.Roslyn;
using SymbolKind = MCPsharp.Services.Roslyn.SymbolKind;

namespace MCPsharp.Tests.Services.Roslyn;

public partial class RenameSymbolServiceTests
{
    #region Edge Case Tests

    [Test]
    public async Task RenameConstructor_HandledSpecially()
    {
        // Arrange
        var code = @"
public class OldClass
{
    public OldClass() { }
    public OldClass(int value) { }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "OldClass",
            NewName = "NewClass",
            SymbolKind = SymbolKind.Class,
            ForcePublicApiChange = true
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.True);
        // Constructors should be renamed with the class
    }

    [Test]
    public async Task RenameNamespace_Success()
    {
        // Arrange
        var code = @"
namespace OldNamespace
{
    public class TestClass { }
}

namespace Consumer
{
    using OldNamespace;

    public class User
    {
        private TestClass _test;
    }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "OldNamespace",
            NewName = "NewNamespace",
            SymbolKind = SymbolKind.Namespace,
            ForcePublicApiChange = true
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.RenamedCount >= 2, Is.True); // Namespace declaration + using statement
    }

    [Test]
    public async Task RenameGenericTypeParameter_Success()
    {
        // Arrange
        var code = @"
public class Container<TOld>
{
    private TOld _value;

    public TOld GetValue() => _value;
    public void SetValue(TOld value) => _value = value;
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "TOld",
            NewName = "TNew",
            SymbolKind = SymbolKind.TypeParameter,
            ForcePublicApiChange = true
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.RenamedCount >= 4, Is.True); // Declaration + 3 uses
    }

    #endregion
}
