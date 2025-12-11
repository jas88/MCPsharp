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
    private readonly ILoggerFactory _loggerFactory;

    public UnusedCodeIntegrationTests()
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
        Assert.That(d => d.Id == "MCP003", Does.Contain(diagnostics));
        Assert.That(d => d.Id == "MCP004", Does.Contain(diagnostics));
    }

    [Test]
    public void Analyzer_DiagnosticDescriptors_HaveCorrectCategories()
    {
        // Arrange
        var analyzer = new UnusedCodeAnalyzer();

        // Act
        var diagnostics = analyzer.SupportedDiagnostics;

        // Assert
        Assert.All(diagnostics, d => Assert.That(d.Category, Is.EqualTo("CodeQuality")));
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
        Assert.That(provider.GetAnalyzer(, Is.Not.Null));
        Assert.That(provider.GetCodeFixProvider(, Is.Not.Null));
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
        Assert.That(provider.Description.ToLowerInvariant(, Does.Contain("unused")));
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
        Assert.IsType<UnusedCodeAnalyzer>(analyzer);
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
        Assert.IsType<UnusedCodeFixer>(fixer);
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
        Assert.That(p => p.Id == "UnusedCode", Does.Contain(providers));
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
        Assert.IsType<UnusedCodeProvider>(provider);
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
        Assert.That(p => p.Id == "UnusedCode", Does.Contain(providers));
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
        Assert.That(p => p.Id == "UnusedCode", Does.Contain(providers));
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
        Assert.Single(providers);
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
        Assert.Single(providers);
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
        Assert.That(stats.TotalProviders >= 3, Is.True); // AsyncAwait, ExceptionLogging, UnusedCode
        Assert.That(stats.FullyAutomatedCount >= 3, Is.True);
        Assert.That(stats.ProvidersByProfile.Keys, Does.Contain(FixProfile.Conservative));
    }
}
