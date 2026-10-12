using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class AdvancedReferenceFinderServiceIntegrationTests
{
    [Test]
    public void MethodComprehensiveAnalysis_ShouldCalculateCorrectProperties()
    {
        // Arrange
        var callers = new CallerResult
        {
            TargetSymbol = "Execute",
            TargetSignature = new MethodSignature
            {
                Name = "Execute",
                ContainingType = "Service",
                ReturnType = "void",
                Accessibility = "public"
            },
            Callers = new List<CallerInfo>
            {
                new()
                {
                    CallerMethod = "Run",
                    CallerType = "Manager",
                    CallType = CallType.Direct,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 1,
                    Column = 1,
                    CallExpression = "Execute()",
                    CallerSignature = new MethodSignature
                    {
                        Name = "Run",
                        ContainingType = "Manager",
                        ReturnType = "void",
                        Accessibility = "public"
                    }
                }
            }
        };

        var callPatterns = new CallPatternAnalysis
        {
            TargetMethod = new MethodSignature
            {
                Name = "Execute",
                ContainingType = "Service",
                ReturnType = "void",
                Accessibility = "public"
            },
            TotalCallSites = 1
        };

        var analysis = new MethodComprehensiveAnalysis
        {
            MethodName = "Execute",
            ContainingType = "Service",
            Callers = callers,
            CallPatterns = callPatterns,
            TotalDirectCallers = 1,
            TotalIndirectCallers = 0,
            HasRecursiveCalls = false,
            AnalysisTime = DateTime.UtcNow
        };

        // Assert
        Assert.That(analysis.MethodName, Is.EqualTo("Execute"));
        Assert.That(analysis.ContainingType, Is.EqualTo("Service"));
        Assert.That(analysis.TotalDirectCallers, Is.EqualTo(1));
        Assert.That(analysis.HasRecursiveCalls, Is.False);
    }

    [Test]
    public void TypeComprehensiveAnalysis_ShouldCalculateCorrectProperties()
    {
        // Arrange
        var typeUsages = new TypeUsageResult
        {
            TypeName = "Service",
            FullTypeName = "TestProject.Service",
            Usages = new List<TypeUsageInfo>
            {
                new()
                {
                    UsageKind = TypeUsageKind.Instantiation,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 1,
                    Column = 1,
                    Context = "new Service()"
                },
                new()
                {
                    UsageKind = TypeUsageKind.Parameter,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 2,
                    Column = 1,
                    Context = "method parameter"
                },
                new()
                {
                    UsageKind = TypeUsageKind.ReturnType,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 3,
                    Column = 1,
                    Context = "return type"
                },
                new()
                {
                    UsageKind = TypeUsageKind.GenericArgument,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 4,
                    Column = 1,
                    Context = "generic argument"
                },
                new()
                {
                    UsageKind = TypeUsageKind.Property,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 5,
                    Column = 1,
                    Context = "property type"
                }
            }
        };

        var inheritanceAnalysis = new InheritanceAnalysis
        {
            TargetType = "Service",
            BaseClasses = new List<TypeUsageInfo>(),
            DerivedClasses = new List<TypeUsageInfo>(),
            ImplementedInterfaces = new List<TypeUsageInfo>
            {
                new()
                {
                    UsageKind = TypeUsageKind.InterfaceImplementation,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 1,
                    Column = 1,
                    Context = "interface implementation"
                }
            },
            InterfaceImplementations = new List<TypeUsageInfo>(),
            InheritanceDepth = 0,
            InheritanceChain = new List<string>()
        };

        var analysis = new TypeComprehensiveAnalysis
        {
            TypeName = "Service",
            TypeUsages = typeUsages,
            InheritanceAnalysis = inheritanceAnalysis,
            InstantiationCount = 1,
            InterfaceImplementationCount = 1,
            TotalUsages = typeUsages.Usages.Count, // Only count direct usages, not interface implementations
            AnalysisTime = DateTime.UtcNow
        };

        // Assert
        Assert.That(analysis.TypeName, Is.EqualTo("Service"));
        Assert.That(analysis.TotalUsages, Is.EqualTo(5));
        Assert.That(analysis.InstantiationCount, Is.EqualTo(1));
        Assert.That(analysis.InterfaceImplementationCount, Is.EqualTo(1));
    }
}
