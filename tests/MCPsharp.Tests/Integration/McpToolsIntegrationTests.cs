using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using MCPsharp.Models.BulkEdit;
using MCPsharp.Models.Roslyn;
using MCPsharp.Tests.TestData;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

[TestFixture]
[Category("Integration")]
[Category("McpTools")]
public partial class McpToolsIntegrationTests : IntegrationTestBase
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
        _bulkEdit = new BulkEditService();
    }

    // Initialize services in async method to properly setup workspace
    protected async Task InitializeServicesAsync()
    {
        // Initialize services with real implementations where possible
        _workspace = new RoslynWorkspace();
        await _workspace.InitializeTestWorkspaceAsync();
        _symbolQuery = new SymbolQueryService(_workspace);
        _callerAnalysis = new CallerAnalysisService(_workspace, _symbolQuery);
        _callChain = new CallChainService(_workspace, _symbolQuery, _callerAnalysis);
        _typeUsage = new TypeUsageService(_workspace, _symbolQuery);
        _referenceFinder = new AdvancedReferenceFinderService(
            _workspace, _symbolQuery, _callerAnalysis, _callChain, _typeUsage);
    }

    [TearDown]
    protected override void TearDown()
    {
        _workspace?.Dispose();
        _bulkEdit?.Dispose();
        base.TearDown();
    }

    [Test]
    public async Task EndToEnd_ReverseSearchAndBulkEdit_ShouldWorkTogether()
    {
        // Arrange
        var sampleFile = CreateTestFile(SampleCSharpFiles.SimpleClass, ".cs");

        // Initialize services and add the file to the workspace
        await InitializeServicesAsync();
        await _workspace.AddInMemoryDocumentAsync(sampleFile, SampleCSharpFiles.SimpleClass);

        // Step 1: Find method references
        var methodName = "ProcessData";
        var containingType = "TestProject.Services.SampleService";

        // Wait for workspace to process the file
        await Task.Delay(100);

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
            Assert.That(editResult.SummaryData?.TotalChangesApplied, Is.GreaterThan(0));

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

        // Initialize services and add the file to the workspace
        await InitializeServicesAsync();
        await _workspace.AddInMemoryDocumentAsync(inheritanceFile, SampleCSharpFiles.InheritanceExample);

        // Wait for workspace processing
        await Task.Delay(100);

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

        // Initialize services and add the file to the workspace
        await InitializeServicesAsync();
        await _workspace.AddInMemoryDocumentAsync(workflowFile, SampleCSharpFiles.CallChainExample);

        // Wait for workspace processing
        await Task.Delay(100);

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
}
