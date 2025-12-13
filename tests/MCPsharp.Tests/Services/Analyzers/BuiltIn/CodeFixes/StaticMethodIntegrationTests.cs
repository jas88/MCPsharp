using NUnit.Framework;
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
        var analyzer = new StaticMethodAnalyzer();

        // Assert
        Assert.That(analyzer, Is.Not.Null);
        Assert.That(StaticMethodAnalyzer.DiagnosticId, Is.EqualTo("MCP005"));
        Assert.That(analyzer.SupportedDiagnostics, Has.Length.EqualTo(1));
    }

    [Test]
    public void Analyzer_DiagnosticDescriptor_HasCorrectProperties()
    {
        // Arrange
        var analyzer = new StaticMethodAnalyzer();

        // Act
        var descriptor = analyzer.SupportedDiagnostics[0];

        // Assert
        Assert.That(descriptor.Id, Is.EqualTo("MCP005"));
        Assert.That(descriptor.Category, Is.EqualTo("Performance"));
        Assert.That(descriptor.IsEnabledByDefault, Is.True);
    }

    [Test]
    public void Fixer_CanBeCreated()
    {
        // Arrange & Act
        var fixer = new StaticMethodFixer();

        // Assert
        Assert.That(fixer, Is.Not.Null);
        Assert.That(fixer.FixableDiagnosticIds, Does.Contain(StaticMethodAnalyzer.DiagnosticId));
    }

    [Test]
    public void Fixer_FixableDiagnosticIds_ContainsMCP005()
    {
        // Arrange
        var fixer = new StaticMethodFixer();

        // Act
        var ids = fixer.FixableDiagnosticIds;

        // Assert
        Assert.That(ids, Has.Length.EqualTo(1));
        Assert.That(ids[0], Is.EqualTo("MCP005"));
    }

    [Test]
    public void Provider_CanBeCreated()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<StaticMethodProvider>();

        // Act
        var provider = new StaticMethodProvider(logger);

        // Assert
        Assert.That(provider, Is.Not.Null);
        Assert.That(provider.Id, Is.EqualTo("StaticMethod"));
        Assert.That(provider.Profile, Is.EqualTo(FixProfile.Balanced));
        Assert.That(provider.IsFullyAutomated, Is.True);
        Assert.That(provider.GetAnalyzer(), Is.Not.Null);
        Assert.That(provider.GetCodeFixProvider(), Is.Not.Null);
    }

    [Test]
    public void Provider_Properties_AreCorrect()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<StaticMethodProvider>();
        var provider = new StaticMethodProvider(logger);

        // Assert
        Assert.That(provider.Id, Is.EqualTo("StaticMethod"));
        Assert.That(provider.Name, Is.EqualTo("Static Method Converter"));
        Assert.That(provider.Description.ToLowerInvariant(), Does.Contain("static"));
        Assert.That(provider.Profile, Is.EqualTo(FixProfile.Balanced));
        Assert.That(provider.IsFullyAutomated, Is.True);
    }

    [Test]
    public void Provider_FixableDiagnosticIds_ContainsMCP005()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<StaticMethodProvider>();
        var provider = new StaticMethodProvider(logger);

        // Act
        var ids = provider.FixableDiagnosticIds;

        // Assert
        Assert.That(ids, Has.Length.EqualTo(1));
        Assert.That(ids[0], Is.EqualTo("MCP005"));
    }

    [Test]
    public void Provider_GetAnalyzer_ReturnsStaticMethodAnalyzer()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<StaticMethodProvider>();
        var provider = new StaticMethodProvider(logger);

        // Act
        var analyzer = provider.GetAnalyzer();

        // Assert
        Assert.That(analyzer, Is.Not.Null);
        Assert.That(analyzer, Is.TypeOf<StaticMethodAnalyzer>());
    }

    [Test]
    public void Provider_GetCodeFixProvider_ReturnsStaticMethodFixer()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<StaticMethodProvider>();
        var provider = new StaticMethodProvider(logger);

        // Act
        var fixer = provider.GetCodeFixProvider();

        // Assert
        Assert.That(fixer, Is.Not.Null);
        Assert.That(fixer, Is.TypeOf<StaticMethodFixer>());
    }

    [Test]
    public void Registry_RegistersStaticMethodProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();

        // Act
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Assert
        var providers = registry.GetAllProviders();
        Assert.That(providers, Does.Contain(providers.First(p => p.Id == "StaticMethod")));
    }

    [Test]
    public void Registry_GetProviderById_ReturnsStaticMethodProvider()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var provider = registry.GetProvider("StaticMethod");

        // Assert
        Assert.That(provider, Is.Not.Null);
        Assert.That(provider!.Id, Is.EqualTo("StaticMethod"));
        Assert.That(provider, Is.TypeOf<StaticMethodProvider>());
    }

    [Test]
    public void Registry_GetProvidersByProfile_IncludesStaticMethod()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersByProfile(FixProfile.Balanced);

        // Assert
        Assert.That(providers, Is.Not.Empty);
        Assert.That(providers, Does.Contain(providers.First(p => p.Id == "StaticMethod")));
    }

    [Test]
    public void Registry_GetFullyAutomatedProviders_IncludesStaticMethod()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetFullyAutomatedProviders();

        // Assert
        Assert.That(providers, Is.Not.Empty);
        Assert.That(providers, Does.Contain(providers.First(p => p.Id == "StaticMethod")));
    }

    [Test]
    public void Registry_GetProvidersForDiagnostic_ReturnsMCP005()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var providers = registry.GetProvidersForDiagnostic("MCP005");

        // Assert
        Assert.That(providers, Has.Length.EqualTo(1));
        Assert.That(providers[0].Id, Is.EqualTo("StaticMethod"));
    }

    [Test]
    public void Registry_Statistics_IncludesAllFourProviders()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var stats = registry.GetStatistics();

        // Assert
        Assert.That(stats.TotalProviders, Is.EqualTo(4)); // AsyncAwait, ExceptionLogging, UnusedCode, StaticMethod
        Assert.That(stats.FullyAutomatedCount, Is.EqualTo(4));
        Assert.That(stats.TotalFixableDiagnostics, Is.GreaterThanOrEqualTo(5)); // MCP001, MCP002, MCP003, MCP004, MCP005
        Assert.That(stats.ProvidersByProfile.Keys, Does.Contain(FixProfile.Conservative));
        Assert.That(stats.ProvidersByProfile.Keys, Does.Contain(FixProfile.Balanced));
    }

    [Test]
    public void Registry_AllProviders_AreRegistered()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var allProviders = registry.GetAllProviders();

        // Assert
        Assert.That(allProviders.Length, Is.EqualTo(4));
        Assert.That(allProviders, Does.Contain(allProviders.First(p => p.Id == "AsyncAwaitPattern")));
        Assert.That(allProviders, Does.Contain(allProviders.First(p => p.Id == "ExceptionLogging")));
        Assert.That(allProviders, Does.Contain(allProviders.First(p => p.Id == "UnusedCode")));
        Assert.That(allProviders, Does.Contain(allProviders.First(p => p.Id == "StaticMethod")));
    }

    [Test]
    public void Registry_GetAnalyzers_ReturnsAllAnalyzers()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var analyzers = registry.GetAllAnalyzers();

        // Assert
        Assert.That(analyzers.Length, Is.EqualTo(4));
        Assert.That(analyzers, Does.Contain(analyzers.OfType<AsyncAwaitPatternAnalyzer>().First()));
        Assert.That(analyzers, Does.Contain(analyzers.OfType<ExceptionLoggingAnalyzer>().First()));
        Assert.That(analyzers, Does.Contain(analyzers.OfType<UnusedCodeAnalyzer>().First()));
        Assert.That(analyzers, Does.Contain(analyzers.OfType<StaticMethodAnalyzer>().First()));
    }

    [Test]
    public void Registry_GetCodeFixProviders_ReturnsAllFixers()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<BuiltInCodeFixRegistry>();
        var registry = new BuiltInCodeFixRegistry(logger, _loggerFactory);

        // Act
        var fixers = registry.GetAllCodeFixProviders();

        // Assert
        Assert.That(fixers.Length, Is.EqualTo(4));
        Assert.That(fixers, Does.Contain(fixers.OfType<AsyncAwaitPatternFixer>().First()));
        Assert.That(fixers, Does.Contain(fixers.OfType<ExceptionLoggingFixer>().First()));
        Assert.That(fixers, Does.Contain(fixers.OfType<UnusedCodeFixer>().First()));
        Assert.That(fixers, Does.Contain(fixers.OfType<StaticMethodFixer>().First()));
    }
}
