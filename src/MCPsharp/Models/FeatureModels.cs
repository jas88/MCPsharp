using System.Collections.Generic;

namespace MCPsharp.Models;

/// <summary>
/// Maps a feature to all its implementing components across the codebase
/// </summary>
public class FeatureMap
{
    /// <summary>
    /// Name of the feature (e.g., "user-authentication")
    /// </summary>
    public required string FeatureName { get; init; }

    /// <summary>
    /// All components implementing this feature
    /// </summary>
    public required FeatureComponents Components { get; init; }

    /// <summary>
    /// Data flow path through the system (e.g., ["Controller", "Service", "Repository", "Database"])
    /// </summary>
    public IReadOnlyList<string>? DataFlow { get; init; }
}

/// <summary>
/// Components that implement a feature organized by architectural layer
/// </summary>
public class FeatureComponents
{
    /// <summary>
    /// Controller entry point (e.g., "Controllers/UserController.cs::Login")
    /// </summary>
    public string? Controller { get; init; }

    /// <summary>
    /// Business logic service (e.g., "Services/AuthService.cs::Authenticate")
    /// </summary>
    public string? Service { get; init; }

    /// <summary>
    /// Data access repository (e.g., "Data/UserRepository.cs::FindByEmail")
    /// </summary>
    public string? Repository { get; init; }

    /// <summary>
    /// Data models used by the feature
    /// </summary>
    public IReadOnlyList<string>? Models { get; init; }

    /// <summary>
    /// Database migrations related to the feature
    /// </summary>
    public IReadOnlyList<string>? Migrations { get; init; }

    /// <summary>
    /// Configuration files/sections related to the feature
    /// </summary>
    public IReadOnlyList<string>? Config { get; init; }

    /// <summary>
    /// Test files covering the feature
    /// </summary>
    public IReadOnlyList<string>? Tests { get; init; }

    /// <summary>
    /// Documentation describing the feature
    /// </summary>
    public string? Documentation { get; init; }

    /// <summary>
    /// CI/CD workflows related to the feature
    /// </summary>
    public IReadOnlyList<string>? Workflows { get; init; }
}

/// <summary>
/// Dependency graph showing relationships between components and architectural layers
/// </summary>
public class DependencyGraph
{
    /// <summary>
    /// Map of component → list of dependencies
    /// Key format: "FilePath::SymbolName"
    /// </summary>
    public required Dictionary<string, IReadOnlyList<string>> Dependencies { get; init; }

    /// <summary>
    /// Architectural layers in dependency order (lower layers first)
    /// E.g., ["Models", "Data", "Services", "Controllers"]
    /// </summary>
    public required IReadOnlyList<string> Layers { get; init; }

    /// <summary>
    /// Architecture violations detected (e.g., Model → Service is wrong)
    /// </summary>
    public IReadOnlyList<ArchitectureViolation>? Violations { get; init; }
}

/// <summary>
/// Represents a violation of architectural layering rules
/// </summary>
public class ArchitectureViolation
{
    /// <summary>
    /// Layer that should not depend on ToLayer
    /// </summary>
    public required string FromLayer { get; init; }

    /// <summary>
    /// Layer that FromLayer incorrectly depends on
    /// </summary>
    public required string ToLayer { get; init; }

    /// <summary>
    /// File containing the violation
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Line number of the violation
    /// </summary>
    public required int Line { get; init; }

    /// <summary>
    /// Human-readable explanation of why this is a violation
    /// </summary>
    public required string Reason { get; init; }
}
