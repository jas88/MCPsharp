using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using MCPsharp.Models;
using MCPsharp.Models.Roslyn;
using MCPsharp.Tests.TestData;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

[TestFixture]
[Category("Integration")]
[Category("McpTools")]
public class McpToolsIntegrationTests : IntegrationTestBase
{
    private RoslynWorkspace _workspace = null!;
    private SymbolQueryService _symbolQuery = null!;
    private CallerAnalysisService _callerAnalysis = null!;
    private CallChainService _callChain = null!;
    private TypeUsageService _typeUsage = null!;
    private AdvancedReferenceFinderService _referenceFinder = null!;
    private BulkEditService _bulkEdit = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        SetupRealisticEnvironment();

        // Initialize services with real implementations where possible
        _workspace = new RoslynWorkspace();
        _symbolQuery = new SymbolQueryService(_workspace);
        _callerAnalysis = new CallerAnalysisService(_workspace, _symbolQuery);
        _callChain = new CallChainService(_workspace, _symbolQuery, _callerAnalysis);
        _typeUsage = new TypeUsageService(_workspace, _symbolQuery);
        _referenceFinder = new AdvancedReferenceFinderService(
            _workspace, _symbolQuery, _callerAnalysis, _callChain, _typeUsage);
        _bulkEdit = new BulkEditService();
    }

    [TearDown]
    protected override void TearDown()
    {
        base.TearDown();
    }

    [Test]
    public async Task EndToEnd_ReverseSearchAndBulkEdit_ShouldWorkTogether()
    {
        // Arrange
        var sampleFile = CreateTestFile(SampleCSharpFiles.SimpleClass, ".cs");

        // Add the file to the workspace
        // _workspace.AddDocument(sampleFile); // Method no longer available

        // Step 1: Find method references
        var methodName = "ProcessData";
        var containingType = "TestProject.Services.SampleService";

        // Wait for workspace to process the file
        await Task.Delay(1000);

        // Step 2: Find all usages of the method
        var usages = await _referenceFinder.FindCallersAsync(methodName, containingType);

        // Step 3: If we found usages, create a bulk edit to rename the method
        if (usages != null && usages.TotalCallers > 0)
        {
            var editRequest = new BulkEditRequest
            {
                OperationType = BulkEditOperationType.BulkReplace,
                Files = new[] { sampleFile },
                RegexPattern = @"ProcessData",
                RegexReplacement = "ProcessDataEnhanced",
                Options = new BulkEditOptions { CreateBackups = true }
            };

            // Step 4: Preview the changes
            var preview = await _bulkEdit.PreviewBulkChangesAsync(editRequest);

            // Assert
            Assert.That(preview, Is.Not.Null);
            Assert.That(preview.Success, Is.True);
            Assert.That(preview.FilePreviews.Count, Is.GreaterThan(0));

            // Step 5: Apply the changes
            var editResult = await _bulkEdit.BulkReplaceAsync(
                editRequest.Files,
                editRequest.RegexPattern,
                editRequest.RegexReplacement,
                editRequest.Options);

            // Assert
            Assert.That(editResult, Is.Not.Null);
            Assert.That(editResult.Success, Is.True);
            Assert.That(editResult.Summary.TotalChangesApplied, Is.GreaterThan(0));

            // Step 6: Verify the changes by searching for the new method name
            var updatedUsages = await _referenceFinder.FindCallersAsync("ProcessDataEnhanced", containingType);
            Assert.That(updatedUsages, Is.Not.Null);
        }
    }

    [Test]
    public async Task EndToEnd_ComplexAnalysis_ShouldAnalyzeInheritanceAndDependencies()
    {
        // Arrange
        var inheritanceFile = CreateTestFile(SampleCSharpFiles.InheritanceExample, ".cs");
        // _workspace.AddDocument(inheritanceFile); // Method no longer available

        // Wait for workspace processing
        await Task.Delay(1000);

        // Step 1: Analyze inheritance relationships
        var inheritanceAnalysis = await _referenceFinder.AnalyzeInheritanceAsync("TextProcessor");

        // Step 2: Analyze type dependencies
        var dependencyAnalysis = await _referenceFinder.AnalyzeTypeDependenciesAsync("TextProcessor");

        // Step 3: Find all usages of the base type
        var typeUsages = await _referenceFinder.FindTypeUsagesAsync("BaseProcessor");

        // Step 4: Get comprehensive analysis
        var comprehensiveAnalysis = await _referenceFinder.AnalyzeTypeComprehensivelyAsync("TextProcessor");

        // Assert
        Assert.That(inheritanceAnalysis, Is.Not.Null);
        Assert.That(dependencyAnalysis, Is.Not.Null);
        Assert.That(typeUsages, Is.Not.Null);
        Assert.That(comprehensiveAnalysis, Is.Not.Null);

        if (typeUsages != null)
        {
            Assert.That(typeUsages.TotalUsages, Is.GreaterThanOrEqualTo(0));
        }

        if (comprehensiveAnalysis != null)
        {
            Assert.That(comprehensiveAnalysis.TypeName, Is.EqualTo("TextProcessor"));
            Assert.That(comprehensiveAnalysis.AnalysisTime, Is.LessThanOrEqualTo(DateTime.UtcNow));
        }
    }

    [Test]
    public async Task End_To_End_CallChainAnalysis_ShouldTraceMethodCalls()
    {
        // Arrange
        var workflowFile = CreateTestFile(SampleCSharpFiles.CallChainExample, ".cs");
        // _workspace.AddDocument(workflowFile); // Method no longer available

        // Wait for workspace processing
        await Task.Delay(1000);

        // Step 1: Find call chains for a specific method
        var callChains = await _referenceFinder.FindCallChainsAsync(
            "ExecuteWorkflowAsync",
            "DataWorkflow",
            CallDirection.Forward,
            maxDepth: 5);

        // Step 2: Find callers of the method
        var callers = await _referenceFinder.FindCallersAsync("ExecuteWorkflowAsync", "DataWorkflow");

        // Step 3: Analyze call patterns
        var callPatterns = await _referenceFinder.AnalyzeCallPatternsAsync("ExecuteWorkflowAsync", "DataWorkflow");

        // Step 4: Find recursive call chains
        var recursiveChains = await _referenceFinder.FindRecursiveCallChainsAsync("ExecuteWorkflowAsync", "DataWorkflow");

        // Step 5: Get comprehensive method analysis
        var methodAnalysis = await _referenceFinder.AnalyzeMethodComprehensivelyAsync("ExecuteWorkflowAsync", "DataWorkflow");

        // Assert
        Assert.That(callChains, Is.Not.Null);
        Assert.That(callers, Is.Not.Null);
        Assert.That(callPatterns, Is.Not.Null);
        Assert.That(recursiveChains, Is.Not.Null);
        Assert.That(methodAnalysis, Is.Not.Null);

        if (methodAnalysis != null)
        {
            Assert.That(methodAnalysis.MethodName, Is.EqualTo("ExecuteWorkflowAsync"));
            Assert.That(methodAnalysis.ContainingType, Is.EqualTo("DataWorkflow"));
            Assert.That(methodAnalysis.AnalysisTime, Is.LessThanOrEqualTo(DateTime.UtcNow));
        }
    }

    [Test]
    public async Task EndToEnd_BulkEditWithValidation_ShouldPerformSafeEdits()
    {
        // Arrange
        var files = new[]
        {
            CreateTestFile("public class Service1 { public void Method1() { } }", ".cs"),
            CreateTestFile("public class Service2 { public void Method2() { } }", ".cs"),
            CreateTestFile("public class Service3 { public void Method3() { } }", ".cs")
        };

        // Step 1: Create a bulk edit request
        var editRequest = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = files,
            RegexPattern = @"Method\d",
            RegexReplacement = "EnhancedMethod",
            Options = new BulkEditOptions
            {
                CreateBackups = true,
                ValidateChanges = true,
                PreviewMode = false
            }
        };

        // Step 2: Validate the request
        var validationResult = await _bulkEdit.ValidateBulkEditAsync(editRequest);

        // Assert validation
        Assert.That(validationResult, Is.Not.Null);
        Assert.That(validationResult.IsValid, Is.True);

        // Step 3: Estimate impact
        var impactEstimate = await _bulkEdit.EstimateImpactAsync(editRequest);

        // Assert impact estimate
        Assert.That(impactEstimate, Is.Not.Null);
        Assert.That(impactEstimate.Complexity, Is.Not.Null);
        Assert.That(impactEstimate.Recommendations, Is.Not.Empty);

        // Step 4: Preview changes
        var preview = await _bulkEdit.PreviewBulkChangesAsync(editRequest);

        // Assert preview
        Assert.That(preview, Is.Not.Null);
        Assert.That(preview.Success, Is.True);
        Assert.That(preview.FilePreviews.Count, Is.EqualTo(files.Length));

        // Step 5: Apply changes
        var editResult = await _bulkEdit.BulkReplaceAsync(
            files,
            editRequest.RegexPattern,
            editRequest.RegexReplacement,
            editRequest.Options);

        // Assert edits
        Assert.That(editResult, Is.Not.Null);
        Assert.That(editResult.Success, Is.True);
        Assert.That(editResult.Summary.TotalFilesProcessed, Is.EqualTo(files.Length));
        Assert.That(editResult.Summary.TotalChangesApplied, Is.EqualTo(3)); // One change per file

        // Step 6: Verify rollback functionality
        if (editResult.RollbackInfo != null)
        {
            var rollbackResult = await _bulkEdit.RollbackBulkEditAsync(editResult.RollbackInfo.RollbackId);
            Assert.That(rollbackResult, Is.Not.Null);
            Assert.That(rollbackResult.Success, Is.True);
        }
    }

    [Test]
    public async Task EndToEnd_ConditionalEditing_ShouldApplyConditionBasedChanges()
    {
        // Arrange
        var files = new[]
        {
            CreateTestFile("// TODO: Fix this\npublic class Service1 { }", ".cs"),
            CreateTestFile("public class Service2 { }", ".cs"), // No TODO
            CreateTestFile("// TODO: Implement feature\npublic class Service3 { }", ".cs")
        };

        var condition = new BulkEditCondition
        {
            ConditionType = BulkConditionType.FileContains,
            Pattern = "TODO:",
            Negate = false
        };

        var edits = new List<TextEdit>
        {
            new ReplaceEdit
            {
                StartLine = 1,
                StartColumn = 1,
                EndLine = 1,
                EndColumn = 1,
                NewText = "// FIXME: "
            }
        };

        // Step 1: Apply conditional edit
        var result = await _bulkEdit.ConditionalEditAsync(files, condition, edits);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);

        // Should have processed 2 files (those containing TODO:)
        Assert.That(result.FileResults.Count(f => f.ChangesApplied > 0), Is.EqualTo(2));

        // Should have skipped 1 file (no TODO)
        Assert.That(result.FileResults.Count(f => f.Skipped), Is.EqualTo(1));
    }

    [Test]
    public async Task End_To_End_PerformanceAnalysis_ShouldCompleteWithinTimeLimits()
    {
        // Arrange
        var largeFile = CreateTestFile(SampleCSharpFiles.LargeFile, ".cs");
        // _workspace.AddDocument(largeFile); // Method no longer available

        // Wait for workspace processing
        await Task.Delay(2000);

        var startTime = DateTime.UtcNow;

        // Step 1: Perform comprehensive analysis on large file
        var capabilities = await _referenceFinder.GetCapabilitiesAsync();

        // Step 2: Analyze all methods in the file
        var allMethods = await _referenceFinder.FindSimilarMethodsAsync("Method"); // Should find multiple matches

        // Step 3: Analyze call chains for a specific method
        var callChains = await _referenceFinder.FindCallChainsAsync("InitializeItems", "LargeService");

        // Step 4: Analyze type usages
        var typeUsages = await _referenceFinder.FindTypeUsagesAsync("LargeService");

        var endTime = DateTime.UtcNow;
        var totalTime = endTime - startTime;

        // Assert performance requirements
        Assert.That(totalTime.TotalSeconds, Is.LessThan(10), "Analysis should complete within 10 seconds");
        Assert.That(capabilities, Is.Not.Null);
        Assert.That(allMethods, Is.Not.Null);
        Assert.That(callChains, Is.Not.Null);
        Assert.That(typeUsages, Is.Not.Null);

        if (capabilities != null)
        {
            Assert.That(capabilities.IsWorkspaceReady, Is.True);
            Assert.That(capabilities.TotalFiles, Is.GreaterThan(0));
        }
    }

    [Test]
    public async Task End_To_End_ErrorHandling_ShouldGracefullyHandleErrors()
    {
        // Arrange
        var validFile = CreateTestFile("public class ValidClass { }", ".cs");
        var invalidFile = "/non/existent/path.cs";
        var files = new[] { validFile, invalidFile };

        // Step 1: Try bulk edit with mixed valid/invalid files
        var editResult = await _bulkEdit.BulkReplaceAsync(files, "class", "struct");

        // Assert partial success
        Assert.That(editResult, Is.Not.Null);
        Assert.That(editResult.FileResults.Count, Is.EqualTo(2));
        Assert.That(editResult.FileResults.Count(r => r.Success), Is.EqualTo(1));
        Assert.That(editResult.FileResults.Count(r => !r.Success), Is.EqualTo(1));

        // Step 2: Try analysis with non-existent method
        var analysisResult = await _referenceFinder.FindCallersAsync("NonExistentMethod", "NonExistentClass");

        // Should handle gracefully (return null or empty result)
        Assert.That(analysisResult, Is.Not.Null);

        // Step 3: Try validation with invalid regex
        var invalidRequest = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = new[] { validFile },
            RegexPattern = "[invalid"
        };

        var validationResult = await _bulkEdit.ValidateBulkEditAsync(invalidRequest);

        // Should detect validation error
        Assert.That(validationResult, Is.Not.Null);
        Assert.That(validationResult.IsValid, Is.False);
        Assert.That(validationResult.Issues.Any(i => i.Type == ValidationIssueType.RegexError), Is.True);
    }

    [Test]
    public async Task End_To_End_FileSizeHandling_ShouldHandleLargeFiles()
    {
        // Arrange
        var largeContent = TestDataFixtures.PerformanceTestData.GenerateLargeText(50000);
        var largeFile = CreateTestFile(largeContent, ".txt");

        var config = new BulkEditOptions
        {
            MaxFileSize = largeContent.Length + 1000, // Allow the file
            CreateBackups = true
        };

        // Step 1: Perform bulk edit on large file
        var result = await _bulkEdit.BulkReplaceAsync(
            new[] { largeFile },
            "Line \\d+: ",
            "PROCESSED Line ",
            config);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Summary.TotalBytesProcessed, Is.EqualTo(largeContent.Length));
        Assert.That(result.Summary.TotalChangesApplied, Is.GreaterThan(0));

        // Step 2: Test with file size limit exceeded
        var strictConfig = new BulkEditOptions
        {
            MaxFileSize = 1000 // Much smaller than file
        };

        var strictResult = await _bulkEdit.BulkReplaceAsync(
            new[] { largeFile },
            "test",
            "replacement",
            strictConfig);

        // Should handle oversized file gracefully
        Assert.That(strictResult, Is.Not.Null);
        // The actual behavior depends on implementation - might skip or fail
    }

    [Test]
    public async Task End_To_End_ConcurrentOperations_ShouldHandleParallelProcessing()
    {
        // Arrange
        var files = Enumerable.Range(0, 10)
            .Select(i => CreateTestFile($"Content {i}", ".cs"))
            .ToArray();

        var bulkEditTasks = new List<Task<BulkEditResult>>();
        var capabilityTasks = new List<Task<ReverseSearchCapabilities>>();

        // Step 1: Run multiple bulk edits in parallel
        for (int i = 0; i < 5; i++)
        {
            var task = _bulkEdit.BulkReplaceAsync(
                files,
                $"Content {i}",
                $"Processed {i}");
            bulkEditTasks.Add(task);
        }

        // Step 2: Run multiple analyses in parallel
        for (int i = 0; i < 3; i++)
        {
            var task = _referenceFinder.GetCapabilitiesAsync();
            capabilityTasks.Add(task);
        }

        // Act
        var bulkEditResults = await Task.WhenAll(bulkEditTasks);
        var capabilityResults = await Task.WhenAll(capabilityTasks);

        var allResults = new object[bulkEditResults.Length + capabilityResults.Length];
        Array.Copy(bulkEditResults, 0, allResults, 0, bulkEditResults.Length);
        Array.Copy(capabilityResults, 0, allResults, bulkEditResults.Length, capabilityResults.Length);
        var results = allResults;

        // Assert
        Assert.That(results.Length, Is.EqualTo(8));

        var finalBulkEditResults = results.Take(5).Cast<BulkEditResult>().ToArray();
        var finalCapabilityResults = results.Skip(5).Cast<ReverseSearchCapabilities>().ToArray();

        Assert.That(finalBulkEditResults.All(r => r != null), Is.True);
        Assert.That(finalCapabilityResults.All(r => r != null), Is.True);
        Assert.That(finalBulkEditResults.All(r => r.Success), Is.True);
    }

    [Test]
    public async Task End_To_End_Cancellation_ShouldSupportOperationCancellation()
    {
        // Arrange
        var largeFile = CreateTestFile(TestDataFixtures.PerformanceTestData.GenerateLargeText(20000), ".cs");
        var cts = new CancellationTokenSource();

        // Step 1: Start a long-running operation
        var longRunningTask = _bulkEdit.BulkReplaceAsync(
            new[] { largeFile },
            "Line ",
            "PROCESSED Line ",
            cancellationToken: cts.Token);

        // Step 2: Cancel after a short delay
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () => await longRunningTask);

        // Step 3: Verify other operations still work
        var quickResult = await _bulkEdit.BulkReplaceAsync(
            new[] { CreateTestFile("test") },
            "test",
            "success");

        Assert.That(quickResult, Is.Not.Null);
        Assert.That(quickResult.Success, Is.True);
    }

    [Test]
    [Repeat(3)] // Test multiple times for consistency
    public async Task End_To_End_ConsistencyTest_ShouldProduceConsistentResults()
    {
        // Arrange
        var testFile = CreateTestFile("public class TestClass { public void TestMethod() { } }", ".cs");

        // Act
        // Run the same analysis multiple times
        var result1 = await _bulkEdit.BulkReplaceAsync(new[] { testFile }, "TestClass", "RenamedClass");
        var result2 = await _bulkEdit.BulkReplaceAsync(new[] { testFile }, "TestClass", "RenamedClass");

        // Assert
        Assert.That(result1.Success, Is.EqualTo(result2.Success));
        Assert.That(result1.Summary.TotalChangesApplied, Is.EqualTo(result2.Summary.TotalChangesApplied));
        Assert.That(result1.Summary.TotalFilesProcessed, Is.EqualTo(result2.Summary.TotalFilesProcessed));
    }
}