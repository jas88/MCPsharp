using System.Threading;
using System.Threading.Tasks;

namespace MCPsharp.Models.Analyzers;

/// <summary>
/// Factory for creating analyzer sandboxes
/// </summary>
public interface IAnalyzerSandboxFactory
{
    /// <summary>
    /// Create a new analyzer sandbox
    /// </summary>
    IAnalyzerSandbox CreateSandbox();
}