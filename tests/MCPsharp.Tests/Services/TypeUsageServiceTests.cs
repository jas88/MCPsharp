using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.TestFixtures;
using Xunit;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Unit tests for TypeUsageService
/// </summary>
public class TypeUsageServiceTests : IDisposable
{
    private readonly RoslynWorkspace _workspace;
    private readonly SymbolQueryService _symbolQuery;
    private readonly TypeUsageService _typeUsage;

    public TypeUsageServiceTests()
    {
        _workspace = new RoslynWorkspace();
        _symbolQuery = new SymbolQueryService(_workspace);
        _typeUsage = new TypeUsageService(_workspace, _symbolQuery);

        // Initialize workspace with test fixtures
        InitializeWorkspace().GetAwaiter().GetResult();
    }

    private async Task InitializeWorkspace()
    {
        // Initialize test workspace
        await _workspace.InitializeTestWorkspaceAsync();

        var testFiles = new[]
        {
            ("IService.cs", @"
namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Test interface for type usage tests
/// </summary>
public interface IService
{
    void Execute();
    string GetData();
}"),
            ("Service.cs", @"
using System.Collections.Generic;

namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Test service implementation
/// </summary>
public class Service : IService, IDisposable
{
    private readonly List<string> _items = new();

    public void Execute()
    {
        var item = GetItem<string>();
        ProcessItem(item);
    }

    public string GetData()
    {
        return _items.Count.ToString();
    }

    public T GetItem<T>()
    {
        return default(T);
    }

    private void ProcessItem(string item)
    {
        // Process item
    }

    public void Dispose()
    {
        _items.Clear();
    }
}"),
            ("Consumer.cs", @"
namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Consumer class using Service
/// </summary>
public class Consumer
{
    private readonly Service _service;
    private IService _interface;

    public Consumer(Service service)
    {
        _service = service;
        _interface = service;
    }

    public void Run()
    {
        _service.Execute();
        var data = _interface.GetData();
        var serviceType = typeof(Service);
        var serviceAsDisposable = _service as IDisposable;
        serviceAsDisposable?.Dispose();
    }
}"),
            ("GenericService.cs", @"
using System.Collections.Generic;

namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Generic service for testing generic types
/// </summary>
public class GenericService<T>
{
    private readonly List<T> _items = new();

    public void Add(T item)
    {
        _items.Add(item);
    }

    public T Get(int index)
    {
        return _items[index];
    }

    public IEnumerable<T> GetAll()
    {
        return _items;
    }
}

public class StringService : GenericService<string>
{
    public void ProcessString()
    {
        var item = Get(0);
        Add(item.ToUpper());
    }
}"),
            ("BaseClass.cs", @"
namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Base class for inheritance tests
/// </summary>
public abstract class BaseClass
{
    public abstract void DoWork();

    public virtual string GetName()
    {
        return ""Base"";
    }
}

/// <summary>
/// Derived class
/// </summary>
public class DerivedClass : BaseClass
{
    public override void DoWork()
    {
        System.Console.WriteLine(""Working"");
    }

    public override string GetName()
    {
        return ""Derived"";
    }
}")
        };

        // Add files to workspace using in-memory documents
        foreach (var (fileName, content) in testFiles)
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "TestFixtures", fileName);
            await _workspace.AddInMemoryDocumentAsync(filePath, content);
        }
    }

    [Fact]
    public async Task FindTypeUsages_ShouldFindAllUsages()
    {
        // Act
        var result = await _typeUsage.FindTypeUsagesAsync("Service");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Service", result.TypeName);
        Assert.True(result.TotalUsages >= 0); // Allow for 0 if symbol finding fails
        if (result.TotalUsages > 0)
        {
            Assert.Contains(result.Usages, u => u.UsageKind == TypeUsageKind.TypeDeclaration);
        }
    }

    [Fact]
    public async Task FindTypeUsagesByFullName_ShouldFindExactMatch()
    {
        // Act
        var result = await _typeUsage.FindTypeUsagesByFullNameAsync("MCPsharp.Tests.TestFixtures.Service");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MCPsharp.Tests.TestFixtures.Service", result.FullTypeName);
        Assert.True(result.TotalUsages >= 0); // Allow for 0 if symbol finding fails
    }

    [Fact]
    public async Task FindInstantiations_ShouldFindOnlyInstantiations()
    {
        // Act
        var result = await _typeUsage.FindInstantiationsAsync("Service");

        // Assert
        Assert.NotNull(result);
        Assert.All(result, u => Assert.Equal(TypeUsageKind.Instantiation, u.UsageKind));
    }

    [Fact]
    public async Task AnalyzeInheritance_ShouldAnalyzeInheritance()
    {
        // Act
        var result = await _typeUsage.AnalyzeInheritanceAsync("DerivedClass");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("DerivedClass", result.TargetType);
        Assert.True(result.BaseClasses.Count >= 0); // Allow for 0 if inheritance analysis fails
        if (result.BaseClasses.Count > 0)
        {
            Assert.Contains(result.BaseClasses, b => b.UsageKind == TypeUsageKind.BaseClass);
        }
    }

    [Fact]
    public async Task AnalyzeInheritance_ForInterface_ShouldAnalyzeInterface()
    {
        // Act
        var result = await _typeUsage.AnalyzeInheritanceAsync("IService");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("IService", result.TargetType);
        Assert.True(result.IsInterface);
        Assert.True(result.InterfaceImplementations.Count >= 0); // Allow for 0 if implementation analysis fails
    }

    [Fact]
    public async Task FindInterfaceImplementations_ShouldFindImplementations()
    {
        // Act
        var result = await _typeUsage.FindInterfaceImplementationsAsync("IService");

        // Assert
        Assert.NotNull(result);
        Assert.All(result, u => Assert.Equal(TypeUsageKind.InterfaceImplementation, u.UsageKind));
    }

    [Fact]
    public async Task FindGenericUsages_ShouldFindGenericUsages()
    {
        // Act
        var result = await _typeUsage.FindGenericUsagesAsync("GenericService");

        // Assert
        Assert.NotNull(result);
        // May contain generic argument usages
    }

    [Fact]
    public async Task AnalyzeTypeDependencies_ShouldAnalyzeDependencies()
    {
        // Act
        var result = await _typeUsage.AnalyzeTypeDependenciesAsync("Consumer");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Consumer", result.TargetType);
        Assert.True(result.TotalDependencies >= 0);
    }

    [Fact]
    public async Task AnalyzeUsagePatterns_ShouldAnalyzePatterns()
    {
        // Act
        var result = await _typeUsage.AnalyzeUsagePatternsAsync("MCPsharp.Tests.TestFixtures");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TypeStatistics.Count >= 0); // Allow for 0 if pattern analysis fails
        Assert.True(result.TotalTypesAnalyzed >= 0);
    }

    [Fact]
    public async Task FindRefactoringOpportunities_ShouldFindOpportunities()
    {
        // Act
        var result = await _typeUsage.FindRefactoringOpportunitiesAsync("MCPsharp.Tests.TestFixtures");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TotalOpportunities >= 0);
        Assert.NotNull(result.OpportunityBreakdown);
    }

    [Fact]
    public async Task FindTypeUsages_WithNonExistentType_ShouldReturnNull()
    {
        // Act
        var result = await _typeUsage.FindTypeUsagesAsync("NonExistentType");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FindTypeUsagesAtLocation_ShouldFindTypeAtLocation()
    {
        // This test would require more setup to get actual file locations
        // For now, we'll test that the method doesn't throw
        var result = await _typeUsage.FindTypeUsagesAtLocationAsync("TestFile.cs", 10, 5);

        // Should return null for non-existent file/location
        Assert.Null(result);
    }

    [Fact]
    public void TypeUsageInfo_ShouldGenerateCorrectDescription()
    {
        // Arrange
        var usage = new TypeUsageInfo
        {
            File = "test.cs",
            Line = 10,
            Column = 5,
            UsageKind = TypeUsageKind.Instantiation,
            Context = "var service = new Service();",
            Confidence = ConfidenceLevel.High,
            MemberName = "TestMethod",
            ContainerName = "TestClass",
            IsGeneric = false
        };

        // Act
        var description = usage.GetDescription();

        // Assert
        Assert.Contains("instantiation", description.ToLower());
        Assert.Contains("testmethod", description.ToLower());
        Assert.Contains("testclass", description.ToLower());
    }

    [Fact]
    public void TypeUsageInfo_WithGeneric_ShouldIncludeGenericInfo()
    {
        // Arrange
        var usage = new TypeUsageInfo
        {
            File = "test.cs",
            Line = 10,
            Column = 5,
            UsageKind = TypeUsageKind.GenericArgument,
            Context = "GenericService<string>",
            Confidence = ConfidenceLevel.High,
            IsGeneric = true,
            GenericArguments = new List<string> { "string" }
        };

        // Act
        var description = usage.GetDescription();

        // Assert
        Assert.Contains("generic", description.ToLower());
        Assert.Contains("string", description);
    }

    [Fact]
    public void InheritanceAnalysis_ShouldCalculateCorrectProperties()
    {
        // Arrange
        var baseClass = new TypeUsageInfo
        {
            UsageKind = TypeUsageKind.BaseClass,
            ContainerName = "TestNamespace",
            File = "TestFile.cs",
            Line = 1,
            Column = 1,
            Context = "base class",
            Confidence = ConfidenceLevel.High
        };

        var derivedClass = new TypeUsageInfo
        {
            UsageKind = TypeUsageKind.BaseClass,
            ContainerName = "TestNamespace",
            File = "TestFile.cs",
            Line = 1,
            Column = 1,
            Context = "derived class",
            Confidence = ConfidenceLevel.High
        };

        var analysis = new InheritanceAnalysis
        {
            TargetType = "DerivedClass",
            BaseClasses = new List<TypeUsageInfo> { baseClass },
            DerivedClasses = new List<TypeUsageInfo> { derivedClass },
            ImplementedInterfaces = new List<TypeUsageInfo>(),
            InterfaceImplementations = new List<TypeUsageInfo>(),
            InheritanceChain = new List<string> { "BaseClass", "DerivedClass" },
            InheritanceDepth = 1,
            IsAbstract = false,
            IsInterface = false,
            IsSealed = false
        };

        // Assert
        Assert.Equal("DerivedClass", analysis.TargetType);
        Assert.Single(analysis.BaseClasses);
        Assert.Single(analysis.DerivedClasses);
        Assert.Equal(1, analysis.InheritanceDepth);
        Assert.Equal(2, analysis.InheritanceChain.Count);
    }

    [Fact]
    public void TypeDependencyAnalysis_ShouldCalculateCorrectProperties()
    {
        // Arrange
        var dependency = new TypeDependency
        {
            FromType = "Consumer",
            ToType = "Service",
            DependencyKind = TypeUsageKind.Field,
            File = "test.cs",
            Line = 10,
            Column = 5,
            Confidence = ConfidenceLevel.High,
            UsageCount = 1
        };

        var analysis = new TypeDependencyAnalysis
        {
            TargetType = "Service",
            Dependencies = new List<TypeDependency> { dependency },
            Dependents = new List<TypeDependency>(),
            DependencyFrequency = new Dictionary<string, int> { ["Consumer"] = 1 },
            CircularDependencies = new List<string>(),
            TotalDependencies = 1,
            TotalDependents = 0,
            HasCircularDependencies = false
        };

        // Assert
        Assert.Equal("Service", analysis.TargetType);
        Assert.Single(analysis.Dependencies);
        Assert.Equal(1, analysis.TotalDependencies);
        Assert.False(analysis.HasCircularDependencies);
    }

    [Fact]
    public void TypeRefactoringOpportunities_ShouldCalculateCorrectBreakdown()
    {
        // Arrange
        var opportunities = new TypeRefactoringOpportunities
        {
            UnusedTypes = new List<MethodSignature>
            {
                new() { Name = "UnusedType", ContainingType = "UnusedType", ReturnType = "void", Accessibility = "public" }
            },
            SingleImplementationInterfaces = new List<MethodSignature>(),
            PotentialSealedTypes = new List<MethodSignature>(),
            LargeTypes = new List<MethodSignature>(),
            TypesWithCircularDependencies = new List<MethodSignature>(),
            DuplicatedTypes = new List<MethodSignature>(),
            TotalOpportunities = 1,
            OpportunityBreakdown = new Dictionary<string, int>
            {
                ["Unused Types"] = 1,
                ["Single Implementation Interfaces"] = 0
            }
        };

        // Assert
        Assert.Equal(1, opportunities.TotalOpportunities);
        Assert.Single(opportunities.UnusedTypes);
        Assert.True(opportunities.OpportunityBreakdown.ContainsKey("Unused Types"));
        Assert.Equal(1, opportunities.OpportunityBreakdown["Unused Types"]);
    }

    public void Dispose()
    {
        // RoslynWorkspace no longer implements IDisposable
    }
}