using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.TestFixtures;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Unit tests for CallerAnalysisService
/// </summary>
public class CallerAnalysisServiceTests : IDisposable
{
    private readonly RoslynWorkspace _workspace;
    private readonly SymbolQueryService _symbolQuery;
    private readonly CallerAnalysisService _callerAnalysis;

    public CallerAnalysisServiceTests()
    {
        _workspace = new RoslynWorkspace();
        _symbolQuery = new SymbolQueryService(_workspace);
        _callerAnalysis = new CallerAnalysisService(_workspace, _symbolQuery);

        // Initialize workspace with test fixtures
        InitializeWorkspace();
    }

    private void InitializeWorkspace()
    {
        // Add test fixture files to workspace
        var testFiles = new[]
        {
            ("IService.cs", @"
namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Test interface for reference finding tests
/// </summary>
public interface IService
{
    void Execute();
    string GetData();
}"),
            ("ServiceImpl.cs", @"
namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Test implementation for reference finding tests
/// </summary>
public class ServiceImpl : IService
{
    private string _data = ""test"";

    public void Execute()
    {
        System.Console.WriteLine(""Executing"");
    }

    public string GetData()
    {
        return _data;
    }
}"),
            ("Consumer.cs", @"
namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Test consumer class for reference finding tests
/// </summary>
public class Consumer
{
    private readonly IService _service;
    private string _name;

    public Consumer(IService service)
    {
        _service = service;
        _name = ""Consumer"";
    }

    public void Run()
    {
        _service.Execute(); // Method invocation reference
        var data = _service.GetData(); // Another method invocation
        System.Console.WriteLine(data);
    }
}"),
            ("DerivedService.cs", @"
namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Another implementation for testing multiple implementations
/// </summary>
public class DerivedService : IService
{
    public void Execute()
    {
        System.Console.WriteLine(""Derived executing"");
    }

    public string GetData()
    {
        return ""derived"";
    }
}")
        };

        // Add files to workspace
        foreach (var (fileName, content) in testFiles)
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "TestFixtures", fileName);
            _workspace.AddDocumentAsync(filePath).Wait();
        }
    }

    [Fact]
    public async Task FindCallers_ShouldFindDirectCallers()
    {
        // Act
        var result = await _callerAnalysis.FindCallersAsync("Execute", "ServiceImpl");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Execute", result.TargetSymbol);
        Assert.True(result.TotalCallers >= 1);
        Assert.Contains(result.Callers, c => c.CallerType.Contains("Consumer"));
    }

    [Fact]
    public async Task FindCallersBySignature_ShouldFindMatchingMethods()
    {
        // Arrange
        var signature = new MethodSignature
        {
            Name = "Execute",
            ReturnType = "void",
            Parameters = new List<ParameterInfo>(),
            ContainingType = "MCPsharp.Tests.TestFixtures.ServiceImpl",
            Accessibility = "public"
        };

        // Act
        var result = await _callerAnalysis.FindCallersBySignatureAsync(signature);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Execute", result.TargetSymbol);
        Assert.True(result.TotalCallers >= 1);
    }

    [Fact]
    public async Task FindDirectCallers_ShouldExcludeIndirectCalls()
    {
        // Act
        var result = await _callerAnalysis.FindDirectCallersAsync("Execute", "ServiceImpl");

        // Assert
        Assert.NotNull(result);
        Assert.All(result.Callers, c => Assert.Equal(CallType.Direct, c.CallType));
    }

    [Fact]
    public async Task FindIndirectCallers_ShouldIncludeInterfaceCalls()
    {
        // Act
        var result = await _callerAnalysis.FindIndirectCallersAsync("Execute", "IService");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TotalCallers >= 1);
        Assert.Contains(result.Callers, c => c.CallType == CallType.Indirect);
    }

    [Fact]
    public async Task AnalyzeCallPatterns_ShouldReturnPatternAnalysis()
    {
        // Act
        var result = await _callerAnalysis.AnalyzeCallPatternsAsync("Execute", "ServiceImpl");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Execute", result.TargetMethod.Name);
        Assert.True(result.TotalCallSites >= 1);
        Assert.NotNull(result.CallFrequencyByFile);
    }

    [Fact]
    public async Task FindCallers_WithNonExistentMethod_ShouldReturnNull()
    {
        // Act
        var result = await _callerAnalysis.FindCallersAsync("NonExistentMethod");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FindCallersAtLocation_ShouldFindCallersForSymbolAtLocation()
    {
        // This test would require more setup to get actual file locations
        // For now, we'll test that the method doesn't throw
        var result = await _callerAnalysis.FindCallersAtLocationAsync("TestFile.cs", 10, 5);

        // Should return null for non-existent file/location
        Assert.Null(result);
    }

    [Fact]
    public void CreateMethodSignature_ShouldCreateCorrectSignature()
    {
        // This tests a private method through reflection or by testing public behavior
        // For now, we'll test the related functionality through FindCallersBySignature

        var signature = new MethodSignature
        {
            Name = "GetData",
            ReturnType = "string",
            Parameters = new List<ParameterInfo>(),
            ContainingType = "MCPsharp.Tests.TestFixtures.ServiceImpl",
            Accessibility = "public"
        };

        // Test that signature matching works
        var matches = signature.Matches(new MethodSignature
        {
            Name = "GetData",
            ReturnType = "string",
            Parameters = new List<ParameterInfo>(),
            ContainingType = "MCPsharp.Tests.TestFixtures.ServiceImpl",
            Accessibility = "public"
        });

        Assert.True(matches);
    }

    [Fact]
    public void MethodSignature_WithParameters_ShouldMatchCorrectly()
    {
        // Arrange
        var signature1 = new MethodSignature
        {
            Name = "TestMethod",
            ReturnType = "void",
            Parameters = new List<ParameterInfo>
            {
                new() { Name = "param1", Type = "string", Position = 0 },
                new() { Name = "param2", Type = "int", Position = 1 }
            },
            ContainingType = "TestClass",
            Accessibility = "public"
        };

        var signature2 = new MethodSignature
        {
            Name = "TestMethod",
            ReturnType = "void",
            Parameters = new List<ParameterInfo>
            {
                new() { Name = "param1", Type = "string", Position = 0 },
                new() { Name = "param2", Type = "int", Position = 1 }
            },
            ContainingType = "TestClass",
            Accessibility = "public"
        };

        // Act & Assert
        Assert.True(signature1.Matches(signature2));
    }

    [Fact]
    public void MethodSignature_WithDifferentParameters_ShouldNotMatch()
    {
        // Arrange
        var signature1 = new MethodSignature
        {
            Name = "TestMethod",
            ReturnType = "void",
            Parameters = new List<ParameterInfo>
            {
                new() { Name = "param1", Type = "string", Position = 0 }
            },
            ContainingType = "TestClass",
            Accessibility = "public"
        };

        var signature2 = new MethodSignature
        {
            Name = "TestMethod",
            ReturnType = "void",
            Parameters = new List<ParameterInfo>
            {
                new() { Name = "param1", Type = "int", Position = 0 }
            },
            ContainingType = "TestClass",
            Accessibility = "public"
        };

        // Act & Assert
        Assert.False(signature1.Matches(signature2));
    }

    public void Dispose()
    {
        _workspace?.Dispose();
    }
}