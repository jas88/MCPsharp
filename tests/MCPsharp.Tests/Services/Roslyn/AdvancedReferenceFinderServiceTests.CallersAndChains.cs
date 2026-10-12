using NSubstitute;
using NUnit.Framework;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Tests.Services.Roslyn;

public partial class AdvancedReferenceFinderServiceTests
{
    [Test]
    public async Task FindCallersAsync_WithValidParameters_ShouldReturnCallerResult()
    {
        // Arrange
        var methodName = "ProcessData";
        var containingType = "SampleService";
        var expected = new CallerResult
        {
            TargetSymbol = methodName,
            TargetSignature = new MethodSignature
            {
                Name = methodName,
                ContainingType = containingType,
                Accessibility = "public",
                ReturnType = "void"
            },
            Callers = new List<CallerInfo>
            {
                new CallerInfo { File = "test.cs", Line = 1, Column = 1, CallerMethod = "Test", CallerType = "TestClass", CallExpression = "Test()", CallType = CallType.Direct, Confidence = ConfidenceLevel.High, CallerSignature = null, IsRecursive = false }
            }
        };

        _mockCallerAnalysis.FindCallersAsync(methodName, containingType, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindCallersAsync(methodName, containingType, true);

        // Assert
        Assert.That(result, Is.Not.Null);
        if (result == null)
            throw new InvalidOperationException("Result should not be null");
        Assert.That(result.TargetSymbol, Is.EqualTo(methodName));
        Assert.That(result.TargetSignature, Is.Not.Null);
        if (result.TargetSignature == null)
            throw new InvalidOperationException("TargetSignature should not be null");
        Assert.That(result.TargetSignature.ContainingType, Is.EqualTo(containingType));
        Assert.That(result.TotalCallers, Is.EqualTo(1)); // We added 1 caller

        await _mockCallerAnalysis.Received(1).FindCallersAsync(methodName, containingType, Arg.Any<CancellationToken>());
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public async Task FindCallersAsync_WithIndirectParameter_ShouldCallCorrectService(bool includeIndirect)
    {
        // Arrange
        var methodName = "TestMethod";
        var expected = new CallerResult
        {
            TargetSymbol = "TestMethod",
            TargetSignature = new MethodSignature
            {
                Name = "TestMethod",
                ContainingType = "TestClass",
                ReturnType = "void",
                Accessibility = "public"
            },
            Callers = new List<CallerInfo>
            {
                new() { CallerMethod = "Caller1", CallerType = "TestType", File = "test.cs", Line = 1, Column = 1, CallExpression = "TestMethod()", CallType = CallType.Direct, Confidence = ConfidenceLevel.High, CallerSignature = null },
                new() { CallerMethod = "Caller2", CallerType = "TestType", File = "test.cs", Line = 2, Column = 1, CallExpression = "TestMethod()", CallType = CallType.Direct, Confidence = ConfidenceLevel.High, CallerSignature = null },
                new() { CallerMethod = "Caller3", CallerType = "TestType", File = "test.cs", Line = 3, Column = 1, CallExpression = "TestMethod()", CallType = CallType.Direct, Confidence = ConfidenceLevel.High, CallerSignature = null }
            }
        };

        if (includeIndirect)
        {
            _mockCallerAnalysis.FindCallersAsync(methodName, null, Arg.Any<CancellationToken>())
                .Returns(expected);
        }
        else
        {
            _mockCallerAnalysis.FindDirectCallersAsync(methodName, null, Arg.Any<CancellationToken>())
                .Returns(expected);
        }

        // Act
        var result = await _service.FindCallersAsync(methodName, null, includeIndirect);

        // Assert
        Assert.That(result, Is.Not.Null);

        if (includeIndirect)
        {
            await _mockCallerAnalysis.Received(1).FindCallersAsync(methodName, null, Arg.Any<CancellationToken>());
            await _mockCallerAnalysis.DidNotReceive().FindDirectCallersAsync(methodName, null, Arg.Any<CancellationToken>());
        }
        else
        {
            await _mockCallerAnalysis.Received(1).FindDirectCallersAsync(methodName, null, Arg.Any<CancellationToken>());
            await _mockCallerAnalysis.DidNotReceive().FindCallersAsync(methodName, null, Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task FindCallersAtLocationAsync_WithValidLocation_ShouldReturnCallerResult()
    {
        // Arrange
        var filePath = "/test/SampleService.cs";
        var line = 15;
        var column = 10;
        var expected = new CallerResult
        {
            TargetSymbol = "ProcessData",
            TargetSignature = new MethodSignature
            {
                Name = "ProcessData",
                ContainingType = "SampleService",
                Accessibility = "public",
                ReturnType = "void"
            },
            Callers = new List<CallerInfo>
            {
                new CallerInfo { File = "test.cs", Line = 1, Column = 1, CallerMethod = "Test", CallerType = "TestClass", CallExpression = "Test()", CallType = CallType.Direct, Confidence = ConfidenceLevel.High, CallerSignature = null, IsRecursive = false },
                new CallerInfo { File = "test2.cs", Line = 2, Column = 2, CallerMethod = "Test2", CallerType = "TestClass2", CallExpression = "Test2()", CallType = CallType.Indirect, Confidence = ConfidenceLevel.Medium, CallerSignature = null, IsRecursive = false },
                new CallerInfo { File = "test3.cs", Line = 3, Column = 3, CallerMethod = "Test3", CallerType = "TestClass3", CallExpression = "Test3()", CallType = CallType.Direct, Confidence = ConfidenceLevel.High, CallerSignature = null, IsRecursive = false }
            }
        };

        _mockCallerAnalysis.FindCallersAtLocationAsync(filePath, line, column, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindCallersAtLocationAsync(filePath, line, column, false);

        // Assert
        Assert.That(result, Is.Not.Null);
        if (result == null)
            throw new InvalidOperationException("Result should not be null");
        Assert.That(result.TargetSignature, Is.Not.Null);
        if (result.TargetSignature == null)
            throw new InvalidOperationException("TargetSignature should not be null");
        Assert.That(result.TargetSignature.Name, Is.EqualTo("ProcessData"));

        await _mockCallerAnalysis.Received(1).FindCallersAtLocationAsync(filePath, line, column, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FindCallersAtLocationAsync_WithIndirectFalse_ShouldFilterToDirectCallers()
    {
        // Arrange
        var filePath = "/test/SampleService.cs";
        var line = 15;
        var column = 10;
        var mockCallers = new List<CallerInfo>
        {
            new()
            {
                File = "test.cs",
                Line = 1,
                Column = 1,
                CallerMethod = "DirectCaller",
                CallerType = "TestClass",
                CallExpression = "DirectCaller()",
                CallType = CallType.Direct,
                Confidence = ConfidenceLevel.High,
                CallerSignature = new MethodSignature
                {
                    Name = "DirectCaller",
                    ContainingType = "TestClass",
                    ReturnType = "void",
                    Accessibility = "public"
                }
            },
            new()
            {
                File = "test2.cs",
                Line = 2,
                Column = 2,
                CallerMethod = "IndirectCaller",
                CallerType = "TestClass2",
                CallExpression = "IndirectCaller()",
                CallType = CallType.Indirect,
                Confidence = ConfidenceLevel.Medium,
                CallerSignature = new MethodSignature
                {
                    Name = "IndirectCaller",
                    ContainingType = "TestClass2",
                    ReturnType = "void",
                    Accessibility = "public"
                }
            }
        };

        var expected = new CallerResult
        {
            TargetSymbol = "ProcessData",
            TargetSignature = new MethodSignature
            {
                Name = "ProcessData",
                ContainingType = "SampleService",
                ReturnType = "void",
                Accessibility = "public"
            },
            Callers = mockCallers
        };

        _mockCallerAnalysis.FindCallersAtLocationAsync(filePath, line, column, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindCallersAtLocationAsync(filePath, line, column, false);

        // Assert
        Assert.That(result, Is.Not.Null);
        if (result == null)
            throw new InvalidOperationException("Result should not be null");
        Assert.That(result.Callers, Is.Not.Null);
        if (result.Callers == null)
            throw new InvalidOperationException("Callers should not be null");
        Assert.That(result.Callers.Count, Is.EqualTo(1));
        Assert.That(result.Callers[0].CallerMethod, Is.EqualTo("DirectCaller"));
    }

    [Test]
    public async Task FindCallChainsAsync_WithValidParameters_ShouldReturnCallChains()
    {
        // Arrange
        var methodName = "ProcessData";
        var containingType = "SampleService";
        var direction = CallDirection.Backward;
        var maxDepth = 5;
        var expected = new CallChainResult
        {
            TargetMethod = new MethodSignature
            {
                Name = methodName,
                ContainingType = containingType ?? "TestClass",
                ReturnType = "void",
                Accessibility = "public"
            },
            Direction = direction,
            Paths = new List<CallChainPath>
            {
                new CallChainPath
                {
                    Steps = new List<CallChainStep>(),
                    Confidence = ConfidenceLevel.High
                },
                new CallChainPath
                {
                    Steps = new List<CallChainStep>(),
                    Confidence = ConfidenceLevel.High
                },
                new CallChainPath
                {
                    Steps = new List<CallChainStep>(),
                    Confidence = ConfidenceLevel.High
                }
            }
        };

        _mockCallChain.FindCallChainsAsync(methodName, containingType, maxDepth, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindCallChainsAsync(methodName, containingType, direction, maxDepth);

        // Assert
        Assert.That(result, Is.Not.Null);
        if (result == null)
            throw new InvalidOperationException("Result should not be null");
        Assert.That(result.TargetMethod, Is.Not.Null);
        if (result.TargetMethod == null)
            throw new InvalidOperationException("TargetMethod should not be null");
        Assert.That(result.TargetMethod.Name, Is.EqualTo(methodName));
        Assert.That(result.Direction, Is.EqualTo(direction));
        Assert.That(result.TotalPaths, Is.EqualTo(3));

        await _mockCallChain.Received(1).FindCallChainsAsync(methodName, containingType, maxDepth, Arg.Any<CancellationToken>());
    }

    [Test]
    [TestCase(CallDirection.Backward, 1)]
    [TestCase(CallDirection.Forward, 10)]
    [TestCase(CallDirection.Backward, 0)]
    [TestCase(CallDirection.Forward, 100)]
    public async Task FindCallChainsAsync_WithVariousParameters_ShouldPassThroughCorrectly(CallDirection direction, int maxDepth)
    {
        // Arrange
        var methodName = "TestMethod";
        var expected = new CallChainResult
        {
            TargetMethod = new MethodSignature
            {
                Name = "TestMethod",
                ContainingType = "TestClass",
                ReturnType = "void",
                Accessibility = "public"
            },
            Direction = direction
        };

        // Set up mocks for both forward and backward call chains
        _mockCallChain.FindCallChainsAsync(methodName, null, maxDepth, Arg.Any<CancellationToken>())
            .Returns(expected);
        _mockCallChain.FindForwardCallChainsAsync(methodName, null, maxDepth, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindCallChainsAsync(methodName, null, direction, maxDepth);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify the correct method was called based on direction
        if (direction == CallDirection.Forward)
        {
            await _mockCallChain.Received(1).FindForwardCallChainsAsync(methodName, null, maxDepth, Arg.Any<CancellationToken>());
        }
        else
        {
            await _mockCallChain.Received(1).FindCallChainsAsync(methodName, null, maxDepth, Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task FindCallChainsAtLocationAsync_WithValidLocation_ShouldReturnCallChains()
    {
        // Arrange
        var filePath = "/test/SampleService.cs";
        var line = 20;
        var column = 5;
        var expected = new CallChainResult
        {
            TargetMethod = new MethodSignature
            {
                Name = "TargetMethod",
                ContainingType = "SampleService",
                ReturnType = "void",
                Accessibility = "public"
            },
            Direction = CallDirection.Backward,
            Paths = new List<CallChainPath>
            {
                new CallChainPath
                {
                    Steps = new List<CallChainStep>(),
                    Confidence = ConfidenceLevel.High
                },
                new CallChainPath
                {
                    Steps = new List<CallChainStep>(),
                    Confidence = ConfidenceLevel.High
                }
            }
        };

        _mockCallChain.FindCallChainsAtLocationAsync(filePath, line, column, Arg.Any<CallDirection>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindCallChainsAtLocationAsync(filePath, line, column);

        // Assert
        Assert.That(result, Is.Not.Null);
        if (result == null)
            throw new InvalidOperationException("Result should not be null");
        Assert.That(result.TotalPaths, Is.EqualTo(2));

        await _mockCallChain.Received(1).FindCallChainsAtLocationAsync(filePath, line, column, Arg.Any<CallDirection>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
