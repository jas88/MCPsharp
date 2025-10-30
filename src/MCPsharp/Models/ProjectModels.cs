namespace MCPsharp.Models;

/// <summary>
/// Represents information about a symbol (class, method, property, etc.) in the code.
/// </summary>
public class SymbolInfo
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public string? ContainingNamespace { get; init; }
    public string? ContainingType { get; init; }
    public required string FilePath { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
}

/// <summary>
/// Represents information about the current compilation state.
/// </summary>
public class CompilationInfo
{
    public required int FileCount { get; init; }
    public required int TypeCount { get; init; }
    public required bool HasErrors { get; init; }
    public IReadOnlyList<string>? Errors { get; init; }
}

/// <summary>
/// Information about a .NET project file (.csproj)
/// </summary>
public class CsprojInfo
{
    /// <summary>
    /// Project name (typically from file name)
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Absolute path to the .csproj file
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Output type: "Exe", "Library", "WinExe", etc.
    /// </summary>
    public required string OutputType { get; init; }

    /// <summary>
    /// Target frameworks (e.g., ["net9.0"], ["net6.0", "net8.0"])
    /// </summary>
    public required IReadOnlyList<string> TargetFrameworks { get; init; }

    /// <summary>
    /// Assembly name (defaults to project name if not specified)
    /// </summary>
    public string? AssemblyName { get; init; }

    /// <summary>
    /// Root namespace (defaults to project name if not specified)
    /// </summary>
    public string? RootNamespace { get; init; }

    /// <summary>
    /// Nullable reference types enabled
    /// </summary>
    public bool Nullable { get; init; }

    /// <summary>
    /// C# language version (e.g., "latest", "12", "preview")
    /// </summary>
    public string? LangVersion { get; init; }

    /// <summary>
    /// NuGet package references
    /// </summary>
    public IReadOnlyList<PackageReference>? PackageReferences { get; init; }

    /// <summary>
    /// Project-to-project references
    /// </summary>
    public IReadOnlyList<ProjectReference>? ProjectReferences { get; init; }
}

/// <summary>
/// NuGet package reference
/// </summary>
public class PackageReference
{
    /// <summary>
    /// Package identifier (e.g., "Newtonsoft.Json")
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Package version (e.g., "13.0.3")
    /// </summary>
    public required string Version { get; init; }
}

/// <summary>
/// Project-to-project reference
/// </summary>
public class ProjectReference
{
    /// <summary>
    /// Absolute path to the referenced .csproj file
    /// </summary>
    public required string ProjectPath { get; init; }

    /// <summary>
    /// Referenced project name (derived from file name)
    /// </summary>
    public string? ProjectName { get; init; }
}

/// <summary>
/// Information about a Visual Studio solution file (.sln)
/// </summary>
public class SolutionInfo
{
    /// <summary>
    /// Absolute path to the .sln file
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Projects contained in the solution
    /// </summary>
    public required IReadOnlyList<SolutionProject> Projects { get; init; }

    /// <summary>
    /// Build configurations (e.g., ["Debug|Any CPU", "Release|Any CPU"])
    /// </summary>
    public IReadOnlyList<string>? Configurations { get; init; }
}

/// <summary>
/// Project entry in a Visual Studio solution
/// </summary>
public class SolutionProject
{
    /// <summary>
    /// Project name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Relative or absolute path to the .csproj file
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Unique project identifier GUID
    /// </summary>
    public required string ProjectGuid { get; init; }

    /// <summary>
    /// Project type GUID (e.g., FAE04EC0-301F-11D3-BF4B-00C04F79EFBC for C#)
    /// </summary>
    public string? TypeGuid { get; init; }
}
