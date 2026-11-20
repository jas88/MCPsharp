using Microsoft.CodeAnalysis.Diagnostics;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// The projects that compile a file, and their analyzer references: the analyzers an IDE runs on the file
/// (see <see cref="RoslynWorkspace.GetFileAnalyzerScope"/>)
/// </summary>
public sealed class FileAnalyzerScope
{
    private readonly HashSet<string> _referencePaths;

    /// <summary>
    /// The scope of a file that no project compiles
    /// </summary>
    public static FileAnalyzerScope NoProject { get; } = new(0, Array.Empty<AnalyzerReference>());

    public FileAnalyzerScope(int projectCount, IReadOnlyList<AnalyzerReference> analyzerReferences)
    {
        ProjectCount = projectCount;
        AnalyzerReferences = analyzerReferences;
        _referencePaths = analyzerReferences
            .Select(reference => reference.FullPath)
            .OfType<string>()
            .ToHashSet(StringComparer.FromComparison(PathContainment.Comparison));
    }

    /// <summary>
    /// How many projects compile the file: each target framework of a multi-targeted project counts, as does each
    /// project a linked file belongs to
    /// </summary>
    public int ProjectCount { get; }

    /// <summary>
    /// The analyzer references of those projects, each assembly once
    /// </summary>
    public IReadOnlyList<AnalyzerReference> AnalyzerReferences { get; }

    /// <summary>
    /// Whether one of the projects references the analyzer assembly of <paramref name="reference"/>
    /// </summary>
    public bool Includes(AnalyzerReference reference) =>
        reference.FullPath is { } path ? _referencePaths.Contains(path) : AnalyzerReferences.Contains(reference);
}
