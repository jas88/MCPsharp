using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry;

namespace MCPsharp.Tests.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Integration tests for AsyncAwaitPattern analyzer and fixer
/// These tests verify the components integrate correctly
/// </summary>
public class AsyncAwaitPatternIntegrationTests
{
    private readonly ILoggerFactory _loggerFactory;

    public AsyncAwaitPatternIntegrationTests()
    {
        _loggerFactory = NullLoggerFactory.Instance;
    }

    [Fact]
    public void Analyzer_CanBeCreated()
    {
        // Arrange & Act
        var analyzer = new AsyncAwaitPatternAnalyzer();

        // Assert
        Assert.NotNull(analyzer);
        Assert.Equal("MCP001", AsyncAwaitPatternAnalyzer.DiagnosticId);
        Assert.Single(analyzer.SupportedDiagnostics);
    }

    [Fact]
    public void Fixer_CanBeCreated()
    {
        // Arrange & Act
        var fixer = new AsyncAwaitPatternFixer();

        // Assert
        Assert.NotNull(fixer);
        Assert.Contains(AsyncAwaitPatternAnalyzer.DiagnosticId, fixer.FixableDiagnosticIds);
    }

    [Fact]
    public void Provider_CanBeCreated()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<AsyncAwaitPatternProvider>();

        // Act
        var provider = new AsyncAwaitPatternProvider(logger);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("AsyncAwaitPattern", provider.Id);
        Assert.Equal(FixProfile.Balanced, provider.Profile);
        Assert.True(provider.IsFullyAutomated);
        Assert.NotNull(provider.GetAnalyzer());
        Assert.NotNull(provider.GetCodeFixProvider());
    }

    [Fact]
    public void Registry_CanBeCreated()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();

        // Act
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Assert
        Assert.NotNull(registry);

        var providers = registry.GetAllProviders();
        Assert.NotEmpty(providers);
        Assert.Contains(providers, p => p.Id == "AsyncAwaitPattern");
    }

    [Fact]
    public void Registry_GetProviderById_ReturnsCorrectProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var provider = registry.GetProvider("AsyncAwaitPattern");

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("AsyncAwaitPattern", provider!.Id);
    }

    [Fact]
    public void Registry_GetProvidersByProfile_ReturnsBalancedProviders()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersByProfile(FixProfile.Balanced);

        // Assert
        Assert.NotEmpty(providers);
        Assert.Contains(providers, p => p.Id == "AsyncAwaitPattern");
    }

    [Fact]
    public void Registry_GetFullyAutomatedProviders_IncludesAsyncAwait()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetFullyAutomatedProviders();

        // Assert
        Assert.NotEmpty(providers);
        Assert.Contains(providers, p => p.Id == "AsyncAwaitPattern");
    }

    [Fact]
    public void Registry_GetStatistics_ReturnsCorrectStats()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var stats = registry.GetStatistics();

        // Assert
        Assert.True(stats.TotalProviders >= 1);
        Assert.True(stats.FullyAutomatedCount >= 1);
        Assert.True(stats.TotalFixableDiagnostics >= 1);
        Assert.Contains(FixProfile.Balanced, stats.ProvidersByProfile.Keys);
    }

    [Fact]
    public void FixConfiguration_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var config = new FixConfiguration();

        // Assert
        Assert.Equal(FixProfile.Balanced, config.Profile);
        Assert.True(config.RequireUserApproval);
        Assert.True(config.CreateBackup);
        Assert.True(config.ValidateAfterApply);
        Assert.False(config.StopOnFirstError);
    }

    [Fact]
    public void FixConfiguration_ConservativeDefault_IsCorrect()
    {
        // Arrange & Act
        var config = FixConfiguration.ConservativeDefault;

        // Assert
        Assert.Equal(FixProfile.Conservative, config.Profile);
        Assert.False(config.RequireUserApproval); // Conservative is safe enough for auto-apply
        Assert.True(config.CreateBackup);
        Assert.True(config.ValidateAfterApply);
    }

    [Fact]
    public void FixConfiguration_BalancedDefault_IsCorrect()
    {
        // Arrange & Act
        var config = FixConfiguration.BalancedDefault;

        // Assert
        Assert.Equal(FixProfile.Balanced, config.Profile);
        Assert.True(config.RequireUserApproval);
        Assert.True(config.CreateBackup);
        Assert.True(config.ValidateAfterApply);
    }

    [Fact]
    public void FixConfiguration_AggressiveDefault_IsCorrect()
    {
        // Arrange & Act
        var config = FixConfiguration.AggressiveDefault;

        // Assert
        Assert.Equal(FixProfile.Aggressive, config.Profile);
        Assert.True(config.RequireUserApproval);
        Assert.True(config.CreateBackup);
        Assert.True(config.ValidateAfterApply);
        Assert.False(config.StopOnFirstError);
    }
}
