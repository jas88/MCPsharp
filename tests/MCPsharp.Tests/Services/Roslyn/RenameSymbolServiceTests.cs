using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Tests.Services.Roslyn;

/// <summary>
/// Comprehensive test suite for rename symbol functionality
/// </summary>
public class RenameSymbolServiceTests : TestBase
{
    private readonly RenameSymbolService _service;
    private readonly RoslynWorkspace _workspace;
    private readonly Mock<ILogger<RenameSymbolService>> _loggerMock;

    public RenameSymbolServiceTests()
    {
        _workspace = new RoslynWorkspace();
        var referenceFinder = new AdvancedReferenceFinderService(
            _workspace,
            new SymbolQueryService(_workspace),
            new Mock<ICallerAnalysisService>().Object,
            new Mock<ICallChainService>().Object,
            new Mock<ITypeUsageService>().Object);

        var symbolQuery = new SymbolQueryService(_workspace);
        _loggerMock = new Mock<ILogger<RenameSymbolService>>();

        _service = new RenameSymbolService(
            _workspace,
            referenceFinder,
            symbolQuery,
            _loggerMock.Object);
    }

    #region Basic Rename Tests

    [Fact]
    public async Task RenameLocalVariable_Success()
    {
        // Arrange
        var code = @"
public class TestClass
{
    public void TestMethod()
    {
        int oldVariable = 42;
        var result = oldVariable + 10;
        Console.WriteLine(oldVariable);
    }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "oldVariable",
            NewName = "newVariable",
            SymbolKind = SymbolKind.Local
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.RenamedCount); // 3 references to the variable
        Assert.Single(result.FilesModified);
    }

    [Fact]
    public async Task RenameParameter_UpdatesAllReferences()
    {
        // Arrange
        var code = @"
public class TestClass
{
    public int Calculate(int oldParam)
    {
        if (oldParam < 0)
            return -oldParam;
        return oldParam * 2;
    }

    public void Caller()
    {
        Calculate(oldParam: 42);
    }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "oldParam",
            NewName = "value",
            SymbolKind = SymbolKind.Parameter
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.RenamedCount >= 4); // Declaration + 3 uses + named argument
    }

    #endregion

    #region Class Rename Tests

    [Fact]
    public async Task RenameClass_UpdatesAllReferences()
    {
        // Arrange
        var code = @"
public class OldClassName
{
    public OldClassName() { }

    public static OldClassName Create()
    {
        return new OldClassName();
    }
}

public class Consumer
{
    public void UseClass()
    {
        var instance = new OldClassName();
        OldClassName staticRef = OldClassName.Create();
    }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("test.cs", code);

        var request = new RenameRequest
        {
            OldName = "OldClassName",
            NewName = "NewClassName",
            SymbolKind = SymbolKind.Class
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.RenamedCount >= 6); // Class declaration, constructor, return types, instantiations
    }

    [Fact]
    public async Task RenamePartialClass_UpdatesAllParts()
    {
        // Arrange
        var code1 = @"
public partial class PartialClass
{
    public void Method1() { }
}";

        var code2 = @"
public partial class PartialClass
{
    public void Method2() { }
}";

        await _workspace.InitializeTestWorkspaceAsync();
        await _workspace.AddInMemoryDocumentAsync("file1.cs", code1);
        await _workspace.AddInMemoryDocumentAsync("file2.cs", code2);

        var request = new RenameRequest
        {
            OldName = "PartialClass",
            NewName = "RenamedPartialClass",
            SymbolKind = SymbolKind.Class
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.FilesModified.Count); // Both files should be modified
    }

    #endregion

    #region Interface and Implementation Tests

    [Fact]
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
            SymbolKind = SymbolKind.Interface
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.RenamedCount >= 3); // Interface declaration, implementation, parameter type
    }

    [Fact]
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
            SymbolKind = SymbolKind.Method
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.RenamedCount >= 3); // Interface method + implicit impl + explicit impl
    }

    #endregion

    #region Method Rename Tests

    [Fact]
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
            SymbolKind = SymbolKind.Method
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.RenamedCount >= 4); // Virtual declaration, override, base call, instance call
    }

    [Fact]
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
            RenameOverloads = false // Only rename specific overload
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        // Should rename only one overload and its calls
    }

    #endregion

    #region Property Rename Tests

    [Fact]
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
            SymbolKind = SymbolKind.Property
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.RenamedCount >= 3); // Property declaration + 2 uses
    }

    [Fact]
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
            SymbolKind = SymbolKind.Property
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.RenamedCount >= 3); // Property declaration + 2 uses
    }

    #endregion

    #region Conflict Detection Tests

    [Fact]
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
            SymbolKind = SymbolKind.Property
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.Conflicts);
        Assert.Contains(result.Conflicts, c => c.Type == ConflictType.NameCollision);
    }

    [Fact]
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
            SymbolKind = SymbolKind.Method
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        // Should have warning about hiding inherited member
        Assert.True(result.Conflicts.Any(c => c.Type == ConflictType.HidesInheritedMember));
    }

    #endregion

    #region Validation Tests

    [Fact]
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
        Assert.False(result.Success);
        Assert.Contains("not a valid C# identifier", result.Errors[0]);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.Contains("is a C# keyword", result.Errors[0]);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.Contains("not found", result.Errors[0]);
    }

    #endregion

    #region Preview Mode Tests

    [Fact]
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
        var docId = await _workspace.AddInMemoryDocumentAsync("test.cs", originalCode);

        var request = new RenameRequest
        {
            OldName = "OldName",
            NewName = "NewName",
            SymbolKind = SymbolKind.Class,
            PreviewOnly = true
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Preview);
        Assert.NotEmpty(result.Preview);

        // Verify original code is unchanged
        var document = _workspace.GetDocument("test.cs");
        Assert.NotNull(document);
        var text = await document.GetTextAsync();
        Assert.Contains("OldName", text.ToString());
        Assert.DoesNotContain("NewName", text.ToString());
    }

    #endregion

    #region Edge Case Tests

    [Fact]
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
            SymbolKind = SymbolKind.Class
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        // Constructors should be renamed with the class
    }

    [Fact]
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
            SymbolKind = SymbolKind.Namespace
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.RenamedCount >= 2); // Namespace declaration + using statement
    }

    [Fact]
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
            SymbolKind = SymbolKind.TypeParameter
        };

        // Act
        var result = await _service.RenameSymbolAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.RenamedCount >= 4); // Declaration + 3 uses
    }

    #endregion
}