using NUnit.Framework;
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
        var analyzer = new AsyncAwaitPatternAnalyzer();

        // Assert
        Assert.That(analyzer, Is.Not.Null);
        Assert.That(AsyncAwaitPatternAnalyzer.DiagnosticId, Is.EqualTo("MCP001"));
        Assert.That(analyzer.SupportedDiagnostics, Has.Length.EqualTo(1));
    }

    [Test]
    public void Fixer_CanBeCreated()
    {
        // Arrange & Act
        var fixer = new AsyncAwaitPatternFixer();

        // Assert
        Assert.That(fixer, Is.Not.Null);
        Assert.That(fixer.FixableDiagnosticIds, Does.Contain(AsyncAwaitPatternAnalyzer.DiagnosticId));
    }

    [Test]
    public void Provider_CanBeCreated()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<AsyncAwaitPatternProvider>();

        // Act
        var provider = new AsyncAwaitPatternProvider(logger);

        // Assert
        Assert.That(provider, Is.Not.Null);
        Assert.That(provider.Id, Is.EqualTo("AsyncAwaitPattern"));
        Assert.That(provider.Profile, Is.EqualTo(FixProfile.Balanced));
        Assert.That(provider.IsFullyAutomated, Is.True);
        Assert.That(provider.GetAnalyzer(), Is.Not.Null);
        Assert.That(provider.GetCodeFixProvider(), Is.Not.Null);
    }

    [Test]
    public void Registry_CanBeCreated()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();

        // Act
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Assert
        Assert.That(registry, Is.Not.Null);

        var providers = registry.GetAllProviders();
        Assert.That(providers, Is.Not.Empty);
        Assert.That(providers, Does.Contain(providers.First(p => p.Id == "AsyncAwaitPattern")));
    }

    [Test]
    public void Registry_GetProviderById_ReturnsCorrectProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var provider = registry.GetProvider("AsyncAwaitPattern");

        // Assert
        Assert.That(provider, Is.Not.Null);
        Assert.That(provider!.Id, Is.EqualTo("AsyncAwaitPattern"));
    }

    [Test]
    public void Registry_GetProvidersByProfile_ReturnsBalancedProviders()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersByProfile(FixProfile.Balanced);

        // Assert
        Assert.That(providers, Is.Not.Empty);
        Assert.That(providers, Does.Contain(providers.First(p => p.Id == "AsyncAwaitPattern")));
    }

    [Test]
    public void Registry_GetFullyAutomatedProviders_IncludesAsyncAwait()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetFullyAutomatedProviders();

        // Assert
        Assert.That(providers, Is.Not.Empty);
        Assert.That(providers, Does.Contain(providers.First(p => p.Id == "AsyncAwaitPattern")));
    }

    [Test]
    public void Registry_GetStatistics_ReturnsCorrectStats()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var stats = registry.GetStatistics();

        // Assert
        Assert.That(stats.TotalProviders, Is.GreaterThanOrEqualTo(1));
        Assert.That(stats.FullyAutomatedCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(stats.TotalFixableDiagnostics, Is.GreaterThanOrEqualTo(1));
        Assert.That(stats.ProvidersByProfile.Keys, Does.Contain(FixProfile.Balanced));
    }

    [Test]
    public void FixConfiguration_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var config = new FixConfiguration();

        // Assert
        Assert.That(config.Profile, Is.EqualTo(FixProfile.Balanced));
        Assert.That(config.RequireUserApproval, Is.True);
        Assert.That(config.CreateBackup, Is.True);
        Assert.That(config.ValidateAfterApply, Is.True);
        Assert.That(config.StopOnFirstError, Is.False);
    }

    [Test]
    public void FixConfiguration_ConservativeDefault_IsCorrect()
    {
        // Arrange & Act
        var config = FixConfiguration.ConservativeDefault;

        // Assert
        Assert.That(config.Profile, Is.EqualTo(FixProfile.Conservative));
        Assert.That(config.RequireUserApproval, Is.False); // Conservative is safe enough for auto-apply
        Assert.That(config.CreateBackup, Is.True);
        Assert.That(config.ValidateAfterApply, Is.True);
    }

    [Test]
    public void FixConfiguration_BalancedDefault_IsCorrect()
    {
        // Arrange & Act
        var config = FixConfiguration.BalancedDefault;

        // Assert
        Assert.That(config.Profile, Is.EqualTo(FixProfile.Balanced));
        Assert.That(config.RequireUserApproval, Is.True);
        Assert.That(config.CreateBackup, Is.True);
        Assert.That(config.ValidateAfterApply, Is.True);
    }

    [Test]
    public void FixConfiguration_AggressiveDefault_IsCorrect()
    {
        // Arrange & Act
        var config = FixConfiguration.AggressiveDefault;

        // Assert
        Assert.That(config.Profile, Is.EqualTo(FixProfile.Aggressive));
        Assert.That(config.RequireUserApproval, Is.True);
        Assert.That(config.CreateBackup, Is.True);
        Assert.That(config.ValidateAfterApply, Is.True);
        Assert.That(config.StopOnFirstError, Is.False);
    }
}
