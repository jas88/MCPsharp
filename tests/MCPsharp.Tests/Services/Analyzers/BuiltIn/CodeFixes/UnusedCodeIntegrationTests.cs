using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry;

namespace MCPsharp.Tests.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Integration tests for UnusedCode analyzer and fixer
/// These tests verify the components integrate correctly
/// </summary>
public class UnusedCodeIntegrationTests
{
    private ILoggerFactory _loggerFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerFactory = NullLoggerFactory.Instance;
    }

    [Test]
    public void Analyzer_CanBeCreated()
    {
        // Arrange & Act
        var analyzer = new UnusedCodeAnalyzer();

        // Assert
        Assert.That(analyzer, Is.Not.Null);
        Assert.That(UnusedCodeAnalyzer.UnusedLocalDiagnosticId, Is.EqualTo("MCP003"));
        Assert.That(UnusedCodeAnalyzer.UnusedFieldDiagnosticId, Is.EqualTo("MCP004"));
        Assert.That(analyzer.SupportedDiagnostics.Length, Is.EqualTo(2));
    }

    [Test]
    public void Analyzer_SupportedDiagnostics_ContainsBothRules()
    {
        // Arrange
        var analyzer = new UnusedCodeAnalyzer();

        // Act
        var diagnostics = analyzer.SupportedDiagnostics;

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(2));
        Assert.That(diagnostics, Does.Contain(diagnostics.First(d => d.Id == "MCP003")));
        Assert.That(diagnostics, Does.Contain(diagnostics.First(d => d.Id == "MCP004")));
    }

    [Test]
    public void Analyzer_DiagnosticDescriptors_HaveCorrectCategories()
    {
        // Arrange
        var analyzer = new UnusedCodeAnalyzer();

        // Act
        var diagnostics = analyzer.SupportedDiagnostics;

        // Assert
        Assert.That(diagnostics, Has.All.Property("Category").EqualTo("CodeQuality"));
    }

    [Test]
    public void Fixer_CanBeCreated()
    {
        // Arrange & Act
        var fixer = new UnusedCodeFixer();

        // Assert
        Assert.That(fixer, Is.Not.Null);
        Assert.That(fixer.FixableDiagnosticIds, Does.Contain(UnusedCodeAnalyzer.UnusedLocalDiagnosticId));
        Assert.That(fixer.FixableDiagnosticIds, Does.Contain(UnusedCodeAnalyzer.UnusedFieldDiagnosticId));
    }

    [Test]
    public void Fixer_FixableDiagnosticIds_ContainsBothMCP003AndMCP004()
    {
        // Arrange
        var fixer = new UnusedCodeFixer();

        // Act
        var ids = fixer.FixableDiagnosticIds;

        // Assert
        Assert.That(ids.Length, Is.EqualTo(2));
        Assert.That(ids, Does.Contain("MCP003"));
        Assert.That(ids, Does.Contain("MCP004"));
    }

    [Test]
    public void Provider_CanBeCreated()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<UnusedCodeProvider>();

        // Act
        var provider = new UnusedCodeProvider(logger);

        // Assert
        Assert.That(provider, Is.Not.Null);
        Assert.That(provider.Id, Is.EqualTo("UnusedCode"));
        Assert.That(provider.Profile, Is.EqualTo(FixProfile.Conservative));
        Assert.That(provider.IsFullyAutomated, Is.True);
        Assert.That(provider.GetAnalyzer(), Is.Not.Null);
        Assert.That(provider.GetCodeFixProvider(), Is.Not.Null);
    }

    [Test]
    public void Provider_Properties_AreCorrect()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<UnusedCodeProvider>();
        var provider = new UnusedCodeProvider(logger);

        // Assert
        Assert.That(provider.Id, Is.EqualTo("UnusedCode"));
        Assert.That(provider.Name, Is.EqualTo("Unused Code Remover"));
        Assert.That(provider.Description.ToLowerInvariant(), Does.Contain("unused"));
        Assert.That(provider.Profile, Is.EqualTo(FixProfile.Conservative));
        Assert.That(provider.IsFullyAutomated, Is.True);
    }

    [Test]
    public void Provider_FixableDiagnosticIds_ContainsBothMCP003AndMCP004()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<UnusedCodeProvider>();
        var provider = new UnusedCodeProvider(logger);

        // Act
        var ids = provider.FixableDiagnosticIds;

        // Assert
        Assert.That(ids.Length, Is.EqualTo(2));
        Assert.That(ids, Does.Contain("MCP003"));
        Assert.That(ids, Does.Contain("MCP004"));
    }

    [Test]
    public void Provider_GetAnalyzer_ReturnsUnusedCodeAnalyzer()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<UnusedCodeProvider>();
        var provider = new UnusedCodeProvider(logger);

        // Act
        var analyzer = provider.GetAnalyzer();

        // Assert
        Assert.That(analyzer, Is.Not.Null);
        Assert.That(analyzer, Is.TypeOf<UnusedCodeAnalyzer>());
    }

    [Test]
    public void Provider_GetCodeFixProvider_ReturnsUnusedCodeFixer()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<UnusedCodeProvider>();
        var provider = new UnusedCodeProvider(logger);

        // Act
        var fixer = provider.GetCodeFixProvider();

        // Assert
        Assert.That(fixer, Is.Not.Null);
        Assert.That(fixer, Is.TypeOf<UnusedCodeFixer>());
    }

    [Test]
    public void Registry_RegistersUnusedCodeProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();

        // Act
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Assert
        var providers = registry.GetAllProviders();
        Assert.That(providers, Does.Contain(providers.First(p => p.Id == "UnusedCode")));
    }

    [Test]
    public void Registry_GetProviderById_ReturnsUnusedCodeProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var provider = registry.GetProvider("UnusedCode");

        // Assert
        Assert.That(provider, Is.Not.Null);
        Assert.That(provider!.Id, Is.EqualTo("UnusedCode"));
        Assert.That(provider, Is.TypeOf<UnusedCodeProvider>());
    }

    [Test]
    public void Registry_GetProvidersByProfile_IncludesUnusedCodeInConservative()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersByProfile(FixProfile.Conservative);

        // Assert
        Assert.That(providers, Is.Not.Empty);
        Assert.That(providers, Does.Contain(providers.First(p => p.Id == "UnusedCode")));
    }

    [Test]
    public void Registry_GetFullyAutomatedProviders_IncludesUnusedCode()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetFullyAutomatedProviders();

        // Assert
        Assert.That(providers, Is.Not.Empty);
        Assert.That(providers, Does.Contain(providers.First(p => p.Id == "UnusedCode")));
    }

    [Test]
    public void Registry_GetProvidersForDiagnostic_ReturnsMCP003()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersForDiagnostic("MCP003");

        // Assert
        Assert.That(providers, Has.Length.EqualTo(1));
        Assert.That(providers[0].Id, Is.EqualTo("UnusedCode"));
    }

    [Test]
    public void Registry_GetProvidersForDiagnostic_ReturnsMCP004()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersForDiagnostic("MCP004");

        // Assert
        Assert.That(providers, Has.Length.EqualTo(1));
        Assert.That(providers[0].Id, Is.EqualTo("UnusedCode"));
    }

    [Test]
    public void Registry_Statistics_IncludesUnusedCodeProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var stats = registry.GetStatistics();

        // Assert
        Assert.That(stats.TotalProviders, Is.GreaterThanOrEqualTo(3)); // AsyncAwait, ExceptionLogging, UnusedCode
        Assert.That(stats.FullyAutomatedCount, Is.GreaterThanOrEqualTo(3));
        Assert.That(stats.ProvidersByProfile.Keys, Does.Contain(FixProfile.Conservative));
    }
}
