using NUnit.Framework;
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
        var analyzer = new ExceptionLoggingAnalyzer();

        // Assert
        Assert.That(analyzer, Is.Not.Null);
        Assert.That(ExceptionLoggingAnalyzer.DiagnosticId, Is.EqualTo("MCP002"));
        Assert.That(analyzer.SupportedDiagnostics, Has.Length.EqualTo(1));
    }

    [Test]
    public void Analyzer_DiagnosticDescriptor_HasCorrectProperties()
    {
        // Arrange
        var analyzer = new ExceptionLoggingAnalyzer();

        // Act
        var descriptor = analyzer.SupportedDiagnostics[0];

        // Assert
        Assert.That(descriptor.Id, Is.EqualTo("MCP002"));
        Assert.That(descriptor.Category, Is.EqualTo("Logging"));
        Assert.That(descriptor.IsEnabledByDefault, Is.True);
    }

    [Test]
    public void Fixer_CanBeCreated()
    {
        // Arrange & Act
        var fixer = new ExceptionLoggingFixer();

        // Assert
        Assert.That(fixer, Is.Not.Null);
        Assert.That(fixer.FixableDiagnosticIds, Does.Contain(ExceptionLoggingAnalyzer.DiagnosticId));
    }

    [Test]
    public void Fixer_FixableDiagnosticIds_ContainsMCP002()
    {
        // Arrange
        var fixer = new ExceptionLoggingFixer();

        // Act
        var ids = fixer.FixableDiagnosticIds;

        // Assert
        Assert.That(ids, Has.Length.EqualTo(1));
        Assert.That(ids[0], Is.EqualTo("MCP002"));
    }

    [Test]
    public void Provider_CanBeCreated()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ExceptionLoggingProvider>();

        // Act
        var provider = new ExceptionLoggingProvider(logger);

        // Assert
        Assert.That(provider, Is.Not.Null);
        Assert.That(provider.Id, Is.EqualTo("ExceptionLogging"));
        Assert.That(provider.Profile, Is.EqualTo(FixProfile.Balanced));
        Assert.That(provider.IsFullyAutomated, Is.True);
        Assert.That(provider.GetAnalyzer(), Is.Not.Null);
        Assert.That(provider.GetCodeFixProvider(), Is.Not.Null);
    }

    [Test]
    public void Provider_Properties_AreCorrect()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ExceptionLoggingProvider>();
        var provider = new ExceptionLoggingProvider(logger);

        // Assert
        Assert.That(provider.Id, Is.EqualTo("ExceptionLogging"));
        Assert.That(provider.Name, Is.EqualTo("Exception Logging Fixer"));
        Assert.That(provider.Description.ToLowerInvariant(), Does.Contain("exception"));
        Assert.That(provider.Profile, Is.EqualTo(FixProfile.Balanced));
        Assert.That(provider.IsFullyAutomated, Is.True);
    }

    [Test]
    public void Provider_FixableDiagnosticIds_ContainsMCP002()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ExceptionLoggingProvider>();
        var provider = new ExceptionLoggingProvider(logger);

        // Act
        var ids = provider.FixableDiagnosticIds;

        // Assert
        Assert.That(ids, Has.Length.EqualTo(1));
        Assert.That(ids[0], Is.EqualTo("MCP002"));
    }

    [Test]
    public void Provider_GetAnalyzer_ReturnsExceptionLoggingAnalyzer()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ExceptionLoggingProvider>();
        var provider = new ExceptionLoggingProvider(logger);

        // Act
        var analyzer = provider.GetAnalyzer();

        // Assert
        Assert.That(analyzer, Is.Not.Null);
        Assert.That(analyzer, Is.TypeOf<ExceptionLoggingAnalyzer>());
    }

    [Test]
    public void Provider_GetCodeFixProvider_ReturnsExceptionLoggingFixer()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ExceptionLoggingProvider>();
        var provider = new ExceptionLoggingProvider(logger);

        // Act
        var fixer = provider.GetCodeFixProvider();

        // Assert
        Assert.That(fixer, Is.Not.Null);
        Assert.That(fixer, Is.TypeOf<ExceptionLoggingFixer>());
    }

    [Test]
    public void Registry_RegistersExceptionLoggingProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();

        // Act
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Assert
        var providers = registry.GetAllProviders();
        Assert.That(providers, Does.Contain(providers.First(p => p.Id == "ExceptionLogging")));
    }

    [Test]
    public void Registry_GetProviderById_ReturnsExceptionLoggingProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var provider = registry.GetProvider("ExceptionLogging");

        // Assert
        Assert.That(provider, Is.Not.Null);
        Assert.That(provider!.Id, Is.EqualTo("ExceptionLogging"));
        Assert.That(provider, Is.TypeOf<ExceptionLoggingProvider>());
    }

    [Test]
    public void Registry_GetProvidersByProfile_IncludesExceptionLogging()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersByProfile(FixProfile.Balanced);

        // Assert
        Assert.That(providers, Is.Not.Empty);
        Assert.That(providers, Does.Contain(providers.First(p => p.Id == "ExceptionLogging")));
    }

    [Test]
    public void Registry_GetFullyAutomatedProviders_IncludesExceptionLogging()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetFullyAutomatedProviders();

        // Assert
        Assert.That(providers, Is.Not.Empty);
        Assert.That(providers, Does.Contain(providers.First(p => p.Id == "ExceptionLogging")));
    }

    [Test]
    public void Registry_GetProvidersForDiagnostic_ReturnsMCP002()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersForDiagnostic("MCP002");

        // Assert
        Assert.That(providers, Has.Length.EqualTo(1));
        Assert.That(providers[0].Id, Is.EqualTo("ExceptionLogging"));
    }

    [Test]
    public void Registry_Statistics_IncludesExceptionLoggingProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var stats = registry.GetStatistics();

        // Assert
        Assert.That(stats.TotalProviders, Is.GreaterThanOrEqualTo(2)); // At least AsyncAwait and ExceptionLogging
        Assert.That(stats.FullyAutomatedCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(stats.ProvidersByProfile.Keys, Does.Contain(FixProfile.Balanced));
    }
}
