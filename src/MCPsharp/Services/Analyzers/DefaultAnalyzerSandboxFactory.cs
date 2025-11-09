using Microsoft.Extensions.Logging;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Default implementation of analyzer sandbox factory
/// </summary>
public class DefaultAnalyzerSandboxFactory : IAnalyzerSandboxFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISecurityManager _securityManager;

    public DefaultAnalyzerSandboxFactory(ILoggerFactory loggerFactory, ISecurityManager securityManager)
    {
        _loggerFactory = loggerFactory;
        _securityManager = securityManager;
    }

    public IAnalyzerSandbox CreateSandbox()
    {
        return new AnalyzerSandbox(
            _loggerFactory.CreateLogger<AnalyzerSandbox>(),
            _securityManager);
    }
}