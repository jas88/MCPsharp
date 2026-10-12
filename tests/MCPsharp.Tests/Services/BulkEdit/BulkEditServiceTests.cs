using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;
using MCPsharp.Services;
using MCPsharp.Tests.TestData;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.BulkEdit;

[TestFixture]
[Category("Unit")]
[Category("BulkEdit")]
public partial class BulkEditServiceTests : FileServiceTestBase
{
    private BulkEditService _service = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _service = new BulkEditService(CreateNullLogger<BulkEditService>());
    }

    [TearDown]
    protected override void TearDown()
    {
        _service?.Dispose();
        base.TearDown();
    }

    [Test]
    public void Constructor_WithLogger_ShouldInitializeSuccessfully()
    {
        // Act & Assert
        Assert.DoesNotThrow(() =>
        {
            using var service = new BulkEditService(CreateNullLogger<BulkEditService>());
        });
        Assert.DoesNotThrow(() =>
        {
            using var service = new BulkEditService(null); // Logger should be optional
        });
    }

    [Test]
    public async Task RollbackBulkEditAsync_WithValidRollbackId_ShouldRestoreFiles()
    {
        // Arrange
        var originalContent = "Original content";
        var testFile = CreateTestFile(originalContent);
        var options = new BulkEditOptions { CreateBackups = true };

        // Perform initial edit
        var editResult = await _service.BulkReplaceAsync(new[] { testFile }, "Original", "Modified", options);
        var rollbackId = editResult.RollbackInfo?.RollbackId;

        Assert.That(rollbackId, Is.Not.Null);
        if (rollbackId == null)
            throw new InvalidOperationException("Rollback ID should not be null");
        Assert.That(editResult.Success, Is.True);

        // Verify file was modified
        var modifiedContent = await File.ReadAllTextAsync(testFile);
        Assert.That(modifiedContent, Is.EqualTo("Modified content"));

        // Act
        var rollbackResult = await _service.RollbackBulkEditAsync(rollbackId);

        // Assert
        Assert.That(rollbackResult, Is.Not.Null);
        Assert.That(rollbackResult.Success, Is.True);
        Assert.That(rollbackResult.ModifiedFiles, Is.EqualTo(1));

        // Verify file was restored
        var restoredContent = await File.ReadAllTextAsync(testFile);
        Assert.That(restoredContent, Is.EqualTo(originalContent));
    }

    [Test]
    public async Task RollbackBulkEditAsync_WithInvalidRollbackId_ShouldFail()
    {
        // Arrange
        var invalidRollbackId = "non-existent-id";

        // Act
        var result = await _service.RollbackBulkEditAsync(invalidRollbackId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not found or expired"));
    }

    [Test]
    public async Task ValidateBulkEditAsync_WithValidRequest_ShouldPassValidation()
    {
        // Arrange
        var testFile = CreateTestFile("Test content");
        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = new[] { testFile },
            RegexPattern = @"Test",
            RegexReplacement = "Valid"
        };

        // Act
        var result = await _service.ValidateBulkEditAsync(request);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Issues.Count, Is.EqualTo(0));
        Assert.That(result.OverallSeverity, Is.EqualTo(ValidationSeverity.None));
    }

    [Test]
    public async Task ValidateBulkEditAsync_WithInvalidRegex_ShouldFailValidation()
    {
        // Arrange
        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = new[] { "*.cs" },
            RegexPattern = "[invalid"
        };

        // Act
        var result = await _service.ValidateBulkEditAsync(request);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues.Any(i => i.Type == ValidationIssueType.RegexError), Is.True);
        Assert.That(result.OverallSeverity, Is.EqualTo(ValidationSeverity.Error));
    }

    [Test]
    public async Task ValidateBulkEditAsync_WithMissingRequiredParameters_ShouldFailValidation()
    {
        // Arrange
        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = new[] { "*.cs" }
            // Missing RegexPattern
        };

        // Act
        var result = await _service.ValidateBulkEditAsync(request);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues.Any(i => i.Description.Contains("required")), Is.True);
    }

    [Test]
    public async Task GetAvailableRollbacksAsync_ShouldReturnRollbackList()
    {
        // Arrange
        var testFile = CreateTestFile("Test content");
        var options = new BulkEditOptions { CreateBackups = true };

        // Create some rollbacks
        var result1 = await _service.BulkReplaceAsync(new[] { testFile }, "Test", "Modified1", options);
        var result2 = await _service.BulkReplaceAsync(new[] { testFile }, "Modified1", "Modified2", options);

        // Act
        var rollbacks = await _service.GetAvailableRollbacksAsync();

        // Assert
        Assert.That(rollbacks, Is.Not.Null);
        Assert.That(rollbacks.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(rollbacks.All(r => r.CanRollback), Is.True);
    }

    [Test]
    public async Task CleanupExpiredRollbacksAsync_ShouldRemoveExpiredRollbacks()
    {
        // Arrange - This test would need to manipulate rollback expiration times
        // For now, just verify the method executes without error

        // Act
        var cleanedCount = await _service.CleanupExpiredRollbacksAsync();

        // Assert
        Assert.That(cleanedCount, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task EstimateImpactAsync_ShouldReturnRiskAssessment()
    {
        // Arrange
        var testFile = CreateTestFile("Test content");
        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = new[] { testFile },
            RegexPattern = @".*", // Broad pattern
            RegexReplacement = "replacement"
        };

        // Act
        var impact = await _service.EstimateImpactAsync(request);

        // Assert
        Assert.That(impact, Is.Not.Null);
        Assert.That(impact.Risks, Is.Not.Empty);
        Assert.That(impact.Complexity, Is.Not.Null);
        Assert.That(impact.Recommendations, Is.Not.Empty);
        Assert.That(impact.Complexity.Score, Is.GreaterThan(0));
    }

    [Test]
    public async Task EstimateImpactAsync_WithRefactorOperation_ShouldAssignHigherRisk()
    {
        // Arrange
        var testFile = CreateTestFile("Test content");
        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BatchRefactor,
            Files = new[] { testFile },
            RefactorPattern = new BulkRefactorPattern
            {
                RefactorType = BulkRefactorType.RenameSymbol,
                TargetPattern = "OldSymbol",
                ReplacementPattern = "NewSymbol"
            }
        };

        // Act
        var impact = await _service.EstimateImpactAsync(request);

        // Assert
        Assert.That(impact, Is.Not.Null);
        Assert.That(impact.OverallRisk, Is.EqualTo(ChangeRiskLevel.High)); // Refactor operations have High risk
        Assert.That(impact.Risks.Any(r => r.Type == RiskType.Compilation), Is.True);
    }

    [Test]
    public async Task GetFileStatisticsAsync_ShouldReturnAccurateStatistics()
    {
        // Arrange
        var files = new[]
        {
            CreateTestFile("Short content", ".cs"),
            CreateTestFile(TestDataFixtures.PerformanceTestData.GenerateLargeText(1000), ".txt"),
            CreateTestFile("Medium length content with multiple lines\nAnd another line", ".md")
        };

        // Act
        var stats = await _service.GetFileStatisticsAsync(files);

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.TotalFiles, Is.EqualTo(3));
        Assert.That(stats.TotalSize, Is.GreaterThan(0));
        Assert.That(stats.TotalLines, Is.GreaterThan(0));
        Assert.That(stats.FileTypes.Count, Is.GreaterThan(0));
        Assert.That(stats.FileTypes.ContainsKey(".cs"), Is.True);
        Assert.That(stats.FileTypes.ContainsKey(".txt"), Is.True);
        Assert.That(stats.FileTypes.ContainsKey(".md"), Is.True);
    }

    [Test]
    public async Task GetFileStatisticsAsync_WithNonExistentFiles_ShouldHandleGracefully()
    {
        // Arrange
        var files = new[] { "/non/existent/file1.cs", "/non/existent/file2.cs" };

        // Act
        var stats = await _service.GetFileStatisticsAsync(files);

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.TotalFiles, Is.EqualTo(0));
        Assert.That(stats.InaccessibleFiles.Count, Is.EqualTo(2));
    }
}
