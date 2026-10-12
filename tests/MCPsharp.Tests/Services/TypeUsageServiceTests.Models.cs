using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class TypeUsageServiceTests
{
    [Test]
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
        Assert.That(description.ToLowerInvariant(), Does.Contain("instantiation"));
        Assert.That(description.ToLowerInvariant(), Does.Contain("testmethod"));
        Assert.That(description.ToLowerInvariant(), Does.Contain("testclass"));
    }

    [Test]
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
        Assert.That(description.ToLowerInvariant(), Does.Contain("generic"));
        Assert.That(description, Does.Contain("string"));
    }

    [Test]
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
        Assert.That(analysis.TargetType, Is.EqualTo("DerivedClass"));
        Assert.That(analysis.BaseClasses, Has.Count.EqualTo(1));
        Assert.That(analysis.DerivedClasses, Has.Count.EqualTo(1));
        Assert.That(analysis.InheritanceDepth, Is.EqualTo(1));
        Assert.That(analysis.InheritanceChain.Count, Is.EqualTo(2));
    }

    [Test]
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
        Assert.That(analysis.TargetType, Is.EqualTo("Service"));
        Assert.That(analysis.Dependencies, Has.Count.EqualTo(1));
        Assert.That(analysis.TotalDependencies, Is.EqualTo(1));
        Assert.That(analysis.HasCircularDependencies, Is.False);
    }

    [Test]
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
        Assert.That(opportunities.TotalOpportunities, Is.EqualTo(1));
        Assert.That(opportunities.UnusedTypes, Has.Count.EqualTo(1));
        Assert.That(opportunities.OpportunityBreakdown.ContainsKey("Unused Types"), Is.True);
        Assert.That(opportunities.OpportunityBreakdown["Unused Types"], Is.EqualTo(1));
    }
}
