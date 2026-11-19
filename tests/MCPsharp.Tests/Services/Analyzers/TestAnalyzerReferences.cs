using System.Reflection;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// Analyzer references to assemblies the build produces (MCPsharp's built-in analyzers and code fixes), as a
/// project would reference them, so analyzer loading tests are fast and don't depend on the NuGet cache.
/// </summary>
internal static class TestAnalyzerReferences
{
    /// <summary>
    /// IDs of the adapters for the C# analyzers in MCPsharp.Analyzers.dll
    /// </summary>
    public static readonly string[] BuiltInAnalyzerIds =
    [
        "Roslyn_AsyncAwaitPatternAnalyzer",
        "Roslyn_ExceptionLoggingAnalyzer",
        "Roslyn_StaticMethodAnalyzer",
        "Roslyn_UnusedCodeAnalyzer"
    ];

    /// <summary>
    /// MCPsharp.Analyzers.dll, holding the analyzers listed in <see cref="BuiltInAnalyzerIds"/>
    /// </summary>
    public static AnalyzerFileReference BuiltInAnalyzers() => Create(typeof(StaticMethodAnalyzer).Assembly.Location);

    /// <summary>
    /// MCPsharp.Analyzers.CodeFixes.dll, holding code fix providers but no analyzers (NuGet adds every assembly
    /// of a package's analyzers folder, code fix assemblies included, as an analyzer reference)
    /// </summary>
    public static AnalyzerFileReference BuiltInCodeFixes() => Create(typeof(StaticMethodFixer).Assembly.Location);

    public static AnalyzerFileReference Create(string path) => new(path, IsolatedAssemblyLoader.Instance);

    /// <summary>
    /// Loads analyzer assemblies in MCPsharp's per-directory analyzer load contexts (Roslyn's own assembly loaders
    /// are internal). Sharing one instance makes references to the same path equal, as within one workspace.
    /// </summary>
    private sealed class IsolatedAssemblyLoader : IAnalyzerAssemblyLoader
    {
        public static readonly IsolatedAssemblyLoader Instance = new();

        public void AddDependencyLocation(string fullPath)
        {
            // Dependencies are resolved from the analyzer assembly's own directory
        }

        public Assembly LoadFromPath(string fullPath) => AnalyzerAssemblyLoadContext.LoadAnalyzerAssembly(fullPath);
    }
}
