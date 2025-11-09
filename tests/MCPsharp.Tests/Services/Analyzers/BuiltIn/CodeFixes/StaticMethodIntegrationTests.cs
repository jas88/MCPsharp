using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry;

namespace MCPsharp.Tests.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Integration tests for StaticMethod analyzer and fixer
/// These tests verify the components integrate correctly
/// </summary>
public class StaticMethodIntegrationTests
{
    private readonly ILoggerFactory _loggerFactory;

    public StaticMethodIntegrationTests()
    {
        _loggerFactory = NullLoggerFactory.Instance;
    }

    [Fact]
    public void Analyzer_CanBeCreated()
    {
        // Arrange & Act
        var analyzer = new StaticMethodAnalyzer();

        // Assert
        Assert.NotNull(analyzer);
        Assert.Equal("MCP005", StaticMethodAnalyzer.DiagnosticId);
        Assert.Single(analyzer.SupportedDiagnostics);
    }

    [Fact]
    public void Analyzer_DiagnosticDescriptor_HasCorrectProperties()
    {
        // Arrange
        var analyzer = new StaticMethodAnalyzer();

        // Act
        var descriptor = analyzer.SupportedDiagnostics[0];

        // Assert
        Assert.Equal("MCP005", descriptor.Id);
        Assert.Equal("Performance", descriptor.Category);
        Assert.True(descriptor.IsEnabledByDefault);
    }

    [Fact]
    public void Fixer_CanBeCreated()
    {
        // Arrange & Act
        var fixer = new StaticMethodFixer();

        // Assert
        Assert.NotNull(fixer);
        Assert.Contains(StaticMethodAnalyzer.DiagnosticId, fixer.FixableDiagnosticIds);
    }

    [Fact]
    public void Fixer_FixableDiagnosticIds_ContainsMCP005()
    {
        // Arrange
        var fixer = new StaticMethodFixer();

        // Act
        var ids = fixer.FixableDiagnosticIds;

        // Assert
        Assert.Single(ids);
        Assert.Equal("MCP005", ids[0]);
    }

    [Fact]
    public void Provider_CanBeCreated()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<StaticMethodProvider>();

        // Act
        var provider = new StaticMethodProvider(logger);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("StaticMethod", provider.Id);
        Assert.Equal(FixProfile.Balanced, provider.Profile);
        Assert.True(provider.IsFullyAutomated);
        Assert.NotNull(provider.GetAnalyzer());
        Assert.NotNull(provider.GetCodeFixProvider());
    }

    [Fact]
    public void Provider_Properties_AreCorrect()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<StaticMethodProvider>();
        var provider = new StaticMethodProvider(logger);

        // Assert
        Assert.Equal("StaticMethod", provider.Id);
        Assert.Equal("Static Method Converter", provider.Name);
        Assert.Contains("static", provider.Description.ToLowerInvariant());
        Assert.Equal(FixProfile.Balanced, provider.Profile);
        Assert.True(provider.IsFullyAutomated);
    }

    [Fact]
    public void Provider_FixableDiagnosticIds_ContainsMCP005()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<StaticMethodProvider>();
        var provider = new StaticMethodProvider(logger);

        // Act
        var ids = provider.FixableDiagnosticIds;

        // Assert
        Assert.Single(ids);
        Assert.Equal("MCP005", ids[0]);
    }

    [Fact]
    public void Provider_GetAnalyzer_ReturnsStaticMethodAnalyzer()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<StaticMethodProvider>();
        var provider = new StaticMethodProvider(logger);

        // Act
        var analyzer = provider.GetAnalyzer();

        // Assert
        Assert.NotNull(analyzer);
        Assert.IsType<StaticMethodAnalyzer>(analyzer);
    }

    [Fact]
    public void Provider_GetCodeFixProvider_ReturnsStaticMethodFixer()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<StaticMethodProvider>();
        var provider = new StaticMethodProvider(logger);

        // Act
        var fixer = provider.GetCodeFixProvider();

        // Assert
        Assert.NotNull(fixer);
        Assert.IsType<StaticMethodFixer>(fixer);
    }

    [Fact]
    public void Registry_RegistersStaticMethodProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();

        // Act
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Assert
        var providers = registry.GetAllProviders();
        Assert.Contains(providers, p => p.Id == "StaticMethod");
    }

    [Fact]
    public void Registry_GetProviderById_ReturnsStaticMethodProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var provider = registry.GetProvider("StaticMethod");

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("StaticMethod", provider!.Id);
        Assert.IsType<StaticMethodProvider>(provider);
    }

    [Fact]
    public void Registry_GetProvidersByProfile_IncludesStaticMethod()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersByProfile(FixProfile.Balanced);

        // Assert
        Assert.NotEmpty(providers);
        Assert.Contains(providers, p => p.Id == "StaticMethod");
    }

    [Fact]
    public void Registry_GetFullyAutomatedProviders_IncludesStaticMethod()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetFullyAutomatedProviders();

        // Assert
        Assert.NotEmpty(providers);
        Assert.Contains(providers, p => p.Id == "StaticMethod");
    }

    [Fact]
    public void Registry_GetProvidersForDiagnostic_ReturnsMCP005()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersForDiagnostic("MCP005");

        // Assert
        Assert.Single(providers);
        Assert.Equal("StaticMethod", providers[0].Id);
    }

    [Fact]
    public void Registry_Statistics_IncludesAllFourProviders()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var stats = registry.GetStatistics();

        // Assert
        Assert.Equal(4, stats.TotalProviders); // AsyncAwait, ExceptionLogging, UnusedCode, StaticMethod
        Assert.Equal(4, stats.FullyAutomatedCount);
        Assert.True(stats.TotalFixableDiagnostics >= 5); // MCP001, MCP002, MCP003, MCP004, MCP005
        Assert.Contains(FixProfile.Conservative, stats.ProvidersByProfile.Keys);
        Assert.Contains(FixProfile.Balanced, stats.ProvidersByProfile.Keys);
    }

    [Fact]
    public void Registry_AllProviders_AreRegistered()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var allProviders = registry.GetAllProviders();

        // Assert
        Assert.Equal(4, allProviders.Length);
        Assert.Contains(allProviders, p => p.Id == "AsyncAwaitPattern");
        Assert.Contains(allProviders, p => p.Id == "ExceptionLogging");
        Assert.Contains(allProviders, p => p.Id == "UnusedCode");
        Assert.Contains(allProviders, p => p.Id == "StaticMethod");
    }

    [Fact]
    public void Registry_GetAnalyzers_ReturnsAllAnalyzers()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var analyzers = registry.GetAllAnalyzers();

        // Assert
        Assert.Equal(4, analyzers.Length);
        Assert.Contains(analyzers, a => a is AsyncAwaitPatternAnalyzer);
        Assert.Contains(analyzers, a => a is ExceptionLoggingAnalyzer);
        Assert.Contains(analyzers, a => a is UnusedCodeAnalyzer);
        Assert.Contains(analyzers, a => a is StaticMethodAnalyzer);
    }

    [Fact]
    public void Registry_GetCodeFixProviders_ReturnsAllFixers()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var fixers = registry.GetAllCodeFixProviders();

        // Assert
        Assert.Equal(4, fixers.Length);
        Assert.Contains(fixers, f => f is AsyncAwaitPatternFixer);
        Assert.Contains(fixers, f => f is ExceptionLoggingFixer);
        Assert.Contains(fixers, f => f is UnusedCodeFixer);
        Assert.Contains(fixers, f => f is StaticMethodFixer);
    }
}
