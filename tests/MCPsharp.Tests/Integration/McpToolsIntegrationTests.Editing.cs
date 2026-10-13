using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;
using MCPsharp.Tests.TestData;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

public partial class McpToolsIntegrationTests
{
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
        Assert.That(editResult.SummaryData?.TotalFilesProcessed, Is.EqualTo(files.Length));
        Assert.That(editResult.SummaryData?.TotalChangesApplied, Is.EqualTo(3)); // One change per file

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
        Assert.That(result.FileResults.Count(f => f.ChangeCount > 0), Is.EqualTo(2));

        // Should have skipped 1 file (no TODO); a skipped file is not a failure
        Assert.That(result.FileResults.Count(f => f.Skipped && f.ChangeCount == 0), Is.EqualTo(1));
        Assert.That(result.FailedFiles, Is.EqualTo(0));
    }

    [Test]
    public async Task End_To_End_PerformanceAnalysis_ShouldCompleteWithinTimeLimits()
    {
        // Arrange
        var largeFile = CreateTestFile(SampleCSharpFiles.LargeFile, ".cs");

        // Initialize services and add the file to the workspace
        await InitializeServicesAsync();
        await _workspace.AddInMemoryDocumentAsync(largeFile, SampleCSharpFiles.LargeFile);

        // Wait for workspace processing
        await Task.Delay(200);

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

        // Initialize services
        await InitializeServicesAsync();
        await _workspace.AddInMemoryDocumentAsync(validFile, "public class ValidClass { }");

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
}
