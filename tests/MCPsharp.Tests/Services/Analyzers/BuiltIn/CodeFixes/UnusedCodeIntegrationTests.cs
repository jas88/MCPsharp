using Xunit;
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

    [Fact]
    public void Analyzer_CanBeCreated()
    {
        // Arrange & Act
        var analyzer = new UnusedCodeAnalyzer();

        // Assert
        Assert.NotNull(analyzer);
        Assert.Equal("MCP003", UnusedCodeAnalyzer.UnusedLocalDiagnosticId);
        Assert.Equal("MCP004", UnusedCodeAnalyzer.UnusedFieldDiagnosticId);
        Assert.Equal(2, analyzer.SupportedDiagnostics.Length);
    }

    [Fact]
    public void Analyzer_SupportedDiagnostics_ContainsBothRules()
    {
        // Arrange
        var analyzer = new UnusedCodeAnalyzer();

        // Act
        var diagnostics = analyzer.SupportedDiagnostics;

        // Assert
        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, d => d.Id == "MCP003");
        Assert.Contains(diagnostics, d => d.Id == "MCP004");
    }

    [Fact]
    public void Analyzer_DiagnosticDescriptors_HaveCorrectCategories()
    {
        // Arrange
        var analyzer = new UnusedCodeAnalyzer();

        // Act
        var diagnostics = analyzer.SupportedDiagnostics;

        // Assert
        Assert.All(diagnostics, d => Assert.Equal("CodeQuality", d.Category));
    }

    [Fact]
    public void Fixer_CanBeCreated()
    {
        // Arrange & Act
        var fixer = new UnusedCodeFixer();

        // Assert
        Assert.NotNull(fixer);
        Assert.Contains(UnusedCodeAnalyzer.UnusedLocalDiagnosticId, fixer.FixableDiagnosticIds);
        Assert.Contains(UnusedCodeAnalyzer.UnusedFieldDiagnosticId, fixer.FixableDiagnosticIds);
    }

    [Fact]
    public void Fixer_FixableDiagnosticIds_ContainsBothMCP003AndMCP004()
    {
        // Arrange
        var fixer = new UnusedCodeFixer();

        // Act
        var ids = fixer.FixableDiagnosticIds;

        // Assert
        Assert.Equal(2, ids.Length);
        Assert.Contains("MCP003", ids);
        Assert.Contains("MCP004", ids);
    }

    [Fact]
    public void Provider_CanBeCreated()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<UnusedCodeProvider>();

        // Act
        var provider = new UnusedCodeProvider(logger);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("UnusedCode", provider.Id);
        Assert.Equal(FixProfile.Conservative, provider.Profile);
        Assert.True(provider.IsFullyAutomated);
        Assert.NotNull(provider.GetAnalyzer());
        Assert.NotNull(provider.GetCodeFixProvider());
    }

    [Fact]
    public void Provider_Properties_AreCorrect()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<UnusedCodeProvider>();
        var provider = new UnusedCodeProvider(logger);

        // Assert
        Assert.Equal("UnusedCode", provider.Id);
        Assert.Equal("Unused Code Remover", provider.Name);
        Assert.Contains("unused", provider.Description.ToLowerInvariant());
        Assert.Equal(FixProfile.Conservative, provider.Profile);
        Assert.True(provider.IsFullyAutomated);
    }

    [Fact]
    public void Provider_FixableDiagnosticIds_ContainsBothMCP003AndMCP004()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<UnusedCodeProvider>();
        var provider = new UnusedCodeProvider(logger);

        // Act
        var ids = provider.FixableDiagnosticIds;

        // Assert
        Assert.Equal(2, ids.Length);
        Assert.Contains("MCP003", ids);
        Assert.Contains("MCP004", ids);
    }

    [Fact]
    public void Provider_GetAnalyzer_ReturnsUnusedCodeAnalyzer()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<UnusedCodeProvider>();
        var provider = new UnusedCodeProvider(logger);

        // Act
        var analyzer = provider.GetAnalyzer();

        // Assert
        Assert.NotNull(analyzer);
        Assert.IsType<UnusedCodeAnalyzer>(analyzer);
    }

    [Fact]
    public void Provider_GetCodeFixProvider_ReturnsUnusedCodeFixer()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<UnusedCodeProvider>();
        var provider = new UnusedCodeProvider(logger);

        // Act
        var fixer = provider.GetCodeFixProvider();

        // Assert
        Assert.NotNull(fixer);
        Assert.IsType<UnusedCodeFixer>(fixer);
    }

    [Fact]
    public void Registry_RegistersUnusedCodeProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();

        // Act
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Assert
        var providers = registry.GetAllProviders();
        Assert.Contains(providers, p => p.Id == "UnusedCode");
    }

    [Fact]
    public void Registry_GetProviderById_ReturnsUnusedCodeProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var provider = registry.GetProvider("UnusedCode");

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("UnusedCode", provider!.Id);
        Assert.IsType<UnusedCodeProvider>(provider);
    }

    [Fact]
    public void Registry_GetProvidersByProfile_IncludesUnusedCodeInConservative()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersByProfile(FixProfile.Conservative);

        // Assert
        Assert.NotEmpty(providers);
        Assert.Contains(providers, p => p.Id == "UnusedCode");
    }

    [Fact]
    public void Registry_GetFullyAutomatedProviders_IncludesUnusedCode()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetFullyAutomatedProviders();

        // Assert
        Assert.NotEmpty(providers);
        Assert.Contains(providers, p => p.Id == "UnusedCode");
    }

    [Fact]
    public void Registry_GetProvidersForDiagnostic_ReturnsMCP003()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersForDiagnostic("MCP003");

        // Assert
        Assert.Single(providers);
        Assert.Equal("UnusedCode", providers[0].Id);
    }

    [Fact]
    public void Registry_GetProvidersForDiagnostic_ReturnsMCP004()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersForDiagnostic("MCP004");

        // Assert
        Assert.Single(providers);
        Assert.Equal("UnusedCode", providers[0].Id);
    }

    [Fact]
    public void Registry_Statistics_IncludesUnusedCodeProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var stats = registry.GetStatistics();

        // Assert
        Assert.True(stats.TotalProviders >= 3); // AsyncAwait, ExceptionLogging, UnusedCode
        Assert.True(stats.FullyAutomatedCount >= 3);
        Assert.Contains(FixProfile.Conservative, stats.ProvidersByProfile.Keys);
    }
}
