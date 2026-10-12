using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Isolated load context for third-party analyzer and code fix assemblies, one per directory.
/// </summary>
/// <remarks>
/// Analyzer assemblies must never be loaded into <see cref="AssemblyLoadContext.Default"/>: Roslyn resolves
/// analyzer and source generator assemblies by name from the host's context first, so a copy loaded there
/// replaces the one a project actually references. NuGet packages ship several builds of the same generator
/// (e.g. roslyn3.11/roslyn4.4) with one assembly identity, and an old build leaking into the default context
/// breaks every later compilation in the process. Assemblies the host already provides (Roslyn, the BCL) are
/// shared from the default context so analyzer types stay compatible with the host's Roslyn types.
/// </remarks>
internal sealed class AnalyzerAssemblyLoadContext : AssemblyLoadContext
{
    private static readonly ConcurrentDictionary<string, AnalyzerAssemblyLoadContext> s_contexts =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string _directory;
    private readonly object _loadLock = new();

    private AnalyzerAssemblyLoadContext(string directory)
        : base($"Analyzers: {directory}")
    {
        _directory = directory;
    }

    /// <summary>
    /// Load an analyzer assembly in the isolated context for its directory, reusing an existing load.
    /// </summary>
    public static Assembly LoadAnalyzerAssembly(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"Assembly path has no directory: {assemblyPath}", nameof(assemblyPath));

        var context = s_contexts.GetOrAdd(directory, d => new AnalyzerAssemblyLoadContext(d));
        return context.LoadFromPath(fullPath);
    }

    private Assembly LoadFromPath(string fullPath)
    {
        lock (_loadLock)
        {
            var name = AssemblyName.GetAssemblyName(fullPath);

            // A copy of a host assembly (e.g. Microsoft.CodeAnalysis.dll next to an analyzer) resolves to the host's copy
            var hostAssembly = Default.Assemblies.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));

            return hostAssembly
                ?? Assemblies.FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(name, a.GetName()))
                ?? LoadFromAssemblyPath(fullPath);
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Share whatever the host can provide (Roslyn, BCL) so analyzer types are assignable to the host's types
        try
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }
        catch (FileNotFoundException)
        {
            // Not a host assembly: fall through to the analyzer's own directory
        }

        var candidate = Path.Combine(_directory, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }
}
