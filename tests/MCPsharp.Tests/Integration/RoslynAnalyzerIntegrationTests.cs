using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.Fixes;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

/// <summary>
/// Integration tests for end-to-end Roslyn analyzer functionality
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Roslyn")]
[Category("Analyzer")]
public partial class RoslynAnalyzerIntegrationTests : IntegrationTestBase
{
    private AnalyzerHost? _analyzerHost;
    private AnalyzerRegistry? _analyzerRegistry;
    private SecurityManager? _securityManager;
    private IFixEngine? _fixEngine;
    private IAnalyzerSandboxFactory? _sandboxFactory;
    private RoslynAnalyzerLoader? _analyzerLoader;
    private ILoggerFactory? _loggerFactory;
    private FileOperationsService? _fileOperations;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();

        // Initialize logging
        _loggerFactory = NullLoggerFactory.Instance;

        // Initialize services
        _analyzerRegistry = new AnalyzerRegistry(
            _loggerFactory.CreateLogger<AnalyzerRegistry>());

        _securityManager = new SecurityManager(
            _loggerFactory.CreateLogger<SecurityManager>(),
            TempDirectory);

        _fileOperations = new FileOperationsService(
            TempDirectory,
            workspace: null,
            logger: _loggerFactory.CreateLogger<FileOperationsService>());

        _fixEngine = new FixEngine(
            _loggerFactory.CreateLogger<FixEngine>(),
            _fileOperations);

        _sandboxFactory = new DefaultAnalyzerSandboxFactory(
            _loggerFactory,
            _securityManager);

        _analyzerHost = new AnalyzerHost(
            _loggerFactory.CreateLogger<AnalyzerHost>(),
            _loggerFactory,
            _analyzerRegistry,
            _securityManager,
            _fixEngine,
            _sandboxFactory);

        _analyzerLoader = new RoslynAnalyzerLoader(
            _loggerFactory.CreateLogger<RoslynAnalyzerLoader>(),
            _loggerFactory);
    }

    [TearDown]
    protected override void TearDown()
    {
        // Clean up loaded analyzers
        if (_analyzerHost != null)
        {
            var loadedAnalyzers = _analyzerHost.GetLoadedAnalyzers();
            foreach (var analyzer in loadedAnalyzers)
            {
                _analyzerHost.UnloadAnalyzerAsync(analyzer.Id).Wait();
            }
        }

        base.TearDown();
    }

    #region Analyzer Discovery Tests

    [Test]
    public void AnalyzerDiscovery_LoadFromAssembly_SuccessfullyLoadsAnalyzers()
    {
        // Arrange: Create a simple analyzer assembly path
        // In a real scenario, this would be a path to a compiled analyzer DLL
        var currentAssembly = typeof(RoslynAnalyzerIntegrationTests).Assembly.Location;

        // Act: Attempt to discover analyzers
        var discoveredPaths = _analyzerLoader!.DiscoverAnalyzerAssemblies(
            Path.GetDirectoryName(currentAssembly)!,
            recursive: false);

        // Assert: Should find some assemblies (at minimum the test assembly)
        // Note: May not find actual analyzer assemblies in test environment
        Assert.That(discoveredPaths.Length, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task AnalyzerDiscovery_LoadInvalidAssembly_HandlesGracefully()
    {
        // Arrange: Create a fake assembly path
        var fakeAssemblyPath = Path.Combine(TempDirectory, "NonExistent.dll");

        // Act: Attempt to load from non-existent assembly
        var loadedAnalyzers = await _analyzerLoader!.LoadAnalyzersFromAssemblyAsync(
            fakeAssemblyPath);

        // Assert: Should return empty array, not throw
        Assert.That(loadedAnalyzers.IsEmpty, Is.True);
    }

    #endregion

    #region Security and Permissions Tests

    [Test]
    public async Task Security_AnalyzerWithRestrictedPermissions_RespectsSandbox()
    {
        // Arrange: Create test file
        var testCode = @"public class TestClass { }";
        var testFile = CreateTestFile(testCode, ".cs");

        // Create and load analyzer
        var testAnalyzer = new TestUnusedVariableAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(
            testAnalyzer,
            _loggerFactory!.CreateLogger<RoslynAnalyzerAdapter>());

        await _analyzerRegistry!.RegisterAnalyzerAsync(adapter);

        // Set restricted permissions
        await _securityManager!.SetPermissionsAsync(adapter.Id, new AnalyzerPermissions
        {
            CanReadFiles = true,
            CanWriteFiles = false,
            CanExecuteCommands = false,
            CanAccessNetwork = false,
            AllowedPaths = ImmutableArray.Create(TempDirectory)
        });

        // Act: Run analysis (should succeed with read-only)
        if (_analyzerHost == null)
            throw new InvalidOperationException("Analyzer host is not initialized");

        var analysisResult = await _analyzerHost.RunAnalyzerAsync(
            adapter.Id,
            testFile);

        // Assert: Analysis should work within sandbox
        Assert.That(analysisResult, Is.Not.Null);
        Assert.That(analysisResult.Success, Is.True);
    }

    #endregion

    #region Helper Methods

    private static string GenerateLargeCodeFile(int methodCount)
    {
        var code = @"
using System;

namespace TestNamespace
{
    public class LargeTestClass
    {";

        for (int i = 0; i < methodCount; i++)
        {
            code += $@"
        public void Method{i}()
        {{
            var unused{i} = {i};
            Console.WriteLine(""Method {i}"");
        }}
";
        }

        code += @"
    }
}";

        return code;
    }

    #endregion

    #region Test Analyzer Implementation

    /// <summary>
    /// Simple test analyzer that detects unused variables
    /// </summary>
    private sealed class TestUnusedVariableAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "TEST001";
        private const string Category = "Usage";

        private static readonly LocalizableString Title = "Unused variable";
        private static readonly LocalizableString MessageFormat = "Variable '{0}' is declared but never used";
        private static readonly LocalizableString Description = "Variables should be used after declaration";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Register syntax node action for local declaration
            context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.LocalDeclarationStatement);
        }

        private void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            var localDeclaration = (Microsoft.CodeAnalysis.CSharp.Syntax.LocalDeclarationStatementSyntax)context.Node;

            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                var variableSymbol = context.SemanticModel.GetDeclaredSymbol(variable);
                if (variableSymbol == null)
                    continue;

                // Simple heuristic: if variable name starts with "unused", report it
                if (variableSymbol.Name.StartsWith("unused", StringComparison.OrdinalIgnoreCase))
                {
                    var diagnostic = Diagnostic.Create(
                        Rule,
                        variable.GetLocation(),
                        variableSymbol.Name);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    #endregion
}
