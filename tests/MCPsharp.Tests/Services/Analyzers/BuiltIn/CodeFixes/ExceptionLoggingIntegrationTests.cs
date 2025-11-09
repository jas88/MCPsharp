using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry;

namespace MCPsharp.Tests.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Integration tests for ExceptionLogging analyzer and fixer
/// These tests verify the components integrate correctly
/// </summary>
public class ExceptionLoggingIntegrationTests
{
    private readonly ILoggerFactory _loggerFactory;

    public ExceptionLoggingIntegrationTests()
    {
        _loggerFactory = NullLoggerFactory.Instance;
    }

    [Fact]
    public void Analyzer_CanBeCreated()
    {
        // Arrange & Act
        var analyzer = new ExceptionLoggingAnalyzer();

        // Assert
        Assert.NotNull(analyzer);
        Assert.Equal("MCP002", ExceptionLoggingAnalyzer.DiagnosticId);
        Assert.Single(analyzer.SupportedDiagnostics);
    }

    [Fact]
    public void Analyzer_DiagnosticDescriptor_HasCorrectProperties()
    {
        // Arrange
        var analyzer = new ExceptionLoggingAnalyzer();

        // Act
        var descriptor = analyzer.SupportedDiagnostics[0];

        // Assert
        Assert.Equal("MCP002", descriptor.Id);
        Assert.Equal("Logging", descriptor.Category);
        Assert.True(descriptor.IsEnabledByDefault);
    }

    [Fact]
    public void Fixer_CanBeCreated()
    {
        // Arrange & Act
        var fixer = new ExceptionLoggingFixer();

        // Assert
        Assert.NotNull(fixer);
        Assert.Contains(ExceptionLoggingAnalyzer.DiagnosticId, fixer.FixableDiagnosticIds);
    }

    [Fact]
    public void Fixer_FixableDiagnosticIds_ContainsMCP002()
    {
        // Arrange
        var fixer = new ExceptionLoggingFixer();

        // Act
        var ids = fixer.FixableDiagnosticIds;

        // Assert
        Assert.Single(ids);
        Assert.Equal("MCP002", ids[0]);
    }

    [Fact]
    public void Provider_CanBeCreated()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ExceptionLoggingProvider>();

        // Act
        var provider = new ExceptionLoggingProvider(logger);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("ExceptionLogging", provider.Id);
        Assert.Equal(FixProfile.Balanced, provider.Profile);
        Assert.True(provider.IsFullyAutomated);
        Assert.NotNull(provider.GetAnalyzer());
        Assert.NotNull(provider.GetCodeFixProvider());
    }

    [Fact]
    public void Provider_Properties_AreCorrect()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ExceptionLoggingProvider>();
        var provider = new ExceptionLoggingProvider(logger);

        // Assert
        Assert.Equal("ExceptionLogging", provider.Id);
        Assert.Equal("Exception Logging Fixer", provider.Name);
        Assert.Contains("exception", provider.Description.ToLowerInvariant());
        Assert.Equal(FixProfile.Balanced, provider.Profile);
        Assert.True(provider.IsFullyAutomated);
    }

    [Fact]
    public void Provider_FixableDiagnosticIds_ContainsMCP002()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ExceptionLoggingProvider>();
        var provider = new ExceptionLoggingProvider(logger);

        // Act
        var ids = provider.FixableDiagnosticIds;

        // Assert
        Assert.Single(ids);
        Assert.Equal("MCP002", ids[0]);
    }

    [Fact]
    public void Provider_GetAnalyzer_ReturnsExceptionLoggingAnalyzer()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ExceptionLoggingProvider>();
        var provider = new ExceptionLoggingProvider(logger);

        // Act
        var analyzer = provider.GetAnalyzer();

        // Assert
        Assert.NotNull(analyzer);
        Assert.IsType<ExceptionLoggingAnalyzer>(analyzer);
    }

    [Fact]
    public void Provider_GetCodeFixProvider_ReturnsExceptionLoggingFixer()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ExceptionLoggingProvider>();
        var provider = new ExceptionLoggingProvider(logger);

        // Act
        var fixer = provider.GetCodeFixProvider();

        // Assert
        Assert.NotNull(fixer);
        Assert.IsType<ExceptionLoggingFixer>(fixer);
    }

    [Fact]
    public void Registry_RegistersExceptionLoggingProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();

        // Act
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Assert
        var providers = registry.GetAllProviders();
        Assert.Contains(providers, p => p.Id == "ExceptionLogging");
    }

    [Fact]
    public void Registry_GetProviderById_ReturnsExceptionLoggingProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var provider = registry.GetProvider("ExceptionLogging");

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("ExceptionLogging", provider!.Id);
        Assert.IsType<ExceptionLoggingProvider>(provider);
    }

    [Fact]
    public void Registry_GetProvidersByProfile_IncludesExceptionLogging()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersByProfile(FixProfile.Balanced);

        // Assert
        Assert.NotEmpty(providers);
        Assert.Contains(providers, p => p.Id == "ExceptionLogging");
    }

    [Fact]
    public void Registry_GetFullyAutomatedProviders_IncludesExceptionLogging()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetFullyAutomatedProviders();

        // Assert
        Assert.NotEmpty(providers);
        Assert.Contains(providers, p => p.Id == "ExceptionLogging");
    }

    [Fact]
    public void Registry_GetProvidersForDiagnostic_ReturnsMCP002()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersForDiagnostic("MCP002");

        // Assert
        Assert.Single(providers);
        Assert.Equal("ExceptionLogging", providers[0].Id);
    }

    [Fact]
    public void Registry_Statistics_IncludesExceptionLoggingProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var stats = registry.GetStatistics();

        // Assert
        Assert.True(stats.TotalProviders >= 2); // At least AsyncAwait and ExceptionLogging
        Assert.True(stats.FullyAutomatedCount >= 2);
        Assert.Contains(FixProfile.Balanced, stats.ProvidersByProfile.Keys);
    }
}
