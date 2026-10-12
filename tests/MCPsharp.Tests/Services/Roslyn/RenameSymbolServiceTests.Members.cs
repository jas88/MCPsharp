using NUnit.Framework;
using Microsoft.CodeAnalysis;
using MCPsharp.Services.Roslyn;
using SymbolKind = MCPsharp.Services.Roslyn.SymbolKind;

namespace MCPsharp.Tests.Services.Roslyn;

public partial class RenameSymbolServiceTests
{
    #region Interface and Implementation Tests

    [Test]
    public async Task RenameInterface_UpdatesImplementations()
    {
        // Arrange
        var code = @"
public interface IOldInterface
{
    void DoSomething();
}

public class Implementation : IOldInterface
{
    public void DoSomething() { }
}

public class Consumer
{
    public void UseInterface(IOldInterface instance)
    {
        instance.DoSomething();
    }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "IOldInterface",
            NewName = "INewInterface",
            SymbolKind = SymbolKind.Interface,
            ForcePublicApiChange = true
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.RenamedCount >= 3, Is.True); // Interface declaration, implementation, parameter type
    }

    [Test]
    public async Task RenameInterfaceMethod_UpdatesImplementations()
    {
        // Arrange
        var code = @"
public interface IService
{
    void OldMethod();
}

public class ServiceImpl : IService
{
    public void OldMethod()
    {
        Console.WriteLine(""Implementation"");
    }
}

public class ExplicitImpl : IService
{
    void IService.OldMethod()
    {
        Console.WriteLine(""Explicit"");
    }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "OldMethod",
            NewName = "NewMethod",
            ContainingType = "IService",
            SymbolKind = SymbolKind.Method,
            ForcePublicApiChange = true
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.RenamedCount >= 3, Is.True); // Interface method + implicit impl + explicit impl
    }

    #endregion

    #region Method Rename Tests

    [Test]
    public async Task RenameVirtualMethod_UpdatesOverrides()
    {
        // Arrange
        var code = @"
public class BaseClass
{
    public virtual void OldMethod() { }
}

public class DerivedClass : BaseClass
{
    public override void OldMethod()
    {
        base.OldMethod();
    }
}

public class Consumer
{
    public void Test()
    {
        BaseClass instance = new DerivedClass();
        instance.OldMethod();
    }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "OldMethod",
            NewName = "NewMethod",
            ContainingType = "BaseClass",
            SymbolKind = SymbolKind.Method,
            ForcePublicApiChange = true
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.True, $"Rename failed. Errors: [{string.Join(", ", result.Errors)}], Conflicts: [{string.Join("; ", result.Conflicts.Select(c => c.Description))}]");
        Assert.That(result.RenamedCount >= 4, Is.True); // Virtual declaration, override, base call, instance call
    }

    [Test]
    public async Task RenameOverloadedMethod_SingleOverload()
    {
        // Arrange
        var code = @"
public class TestClass
{
    public void Process(int value) { }
    public void Process(string value) { }
    public void Process(int x, int y) { }

    public void Test()
    {
        Process(42);
        Process(""text"");
        Process(1, 2);
    }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "Process",
            NewName = "Execute",
            SymbolKind = SymbolKind.Method,
            RenameOverloads = false, // Only rename specific overload
            ForcePublicApiChange = true
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.True);
        // Should rename only one overload and its calls
    }

    #endregion

    #region Property Rename Tests

    [Test]
    public async Task RenameProperty_WithBackingField()
    {
        // Arrange
        var code = @"
public class TestClass
{
    private string _oldProperty;

    public string OldProperty
    {
        get => _oldProperty;
        set => _oldProperty = value;
    }

    public void Test()
    {
        OldProperty = ""test"";
        var value = OldProperty;
    }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "OldProperty",
            NewName = "NewProperty",
            SymbolKind = SymbolKind.Property,
            ForcePublicApiChange = true
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.RenamedCount >= 3, Is.True); // Property declaration + 2 uses
    }

    [Test]
    public async Task RenameAutoProperty_Success()
    {
        // Arrange
        var code = @"
public class TestClass
{
    public string OldProperty { get; set; }

    public void Test()
    {
        OldProperty = ""test"";
        var value = OldProperty;
    }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "OldProperty",
            NewName = "NewProperty",
            SymbolKind = SymbolKind.Property,
            ForcePublicApiChange = true
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.That(result.Success, Is.True, $"Rename failed. Errors: [{string.Join(", ", result.Errors)}], Conflicts: {result.Conflicts.Count}");
        Assert.That(result.RenamedCount >= 3, Is.True); // Property declaration + 2 uses
    }

    #endregion
}
