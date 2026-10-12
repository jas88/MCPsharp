namespace MCPsharp.Models.Consolidated;

/// <summary>
/// Architecture definition
/// </summary>
public class ArchitectureDefinition
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<ArchitectureLayer> Layers { get; set; } = new();
    public List<DependencyRule> Rules { get; set; } = new();

    // Additional properties for compatibility with Models.Architecture.ArchitectureDefinition
    public List<DependencyRule> DependencyRules { get; set; } = new();
    public List<object> NamingPatterns { get; set; } = new();
    public List<object> ForbiddenPatterns { get; set; } = new();
    public string Type { get; set; } = "Custom";
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Architecture layer
/// </summary>
public class ArchitectureLayer
{
    public required string Name { get; set; }
    public required int Level { get; set; }
    public List<string> NamespacePatterns { get; set; } = new();
    public List<string> Types { get; set; } = new();

    // Additional properties for compatibility with Models.Architecture.ArchitecturalLayer
    public string? Description { get; set; }
    public int Order { get; set; }
    public List<string> AllowedDependencies { get; set; } = new();
    public List<string> ForbiddenDependencies { get; set; } = new();
    public string Type { get; set; } = "Standard";
    public bool IsCore { get; set; }
    public List<string> TypicalClasses { get; set; } = new();
}

/// <summary>
/// Dependency rule
/// </summary>
public class DependencyRule
{
    public required string FromLayer { get; set; }
    public required string ToLayer { get; set; }
    public required bool Allowed { get; set; }
    public string? Reason { get; set; }

    // Additional properties for compatibility with Models.Architecture.DependencyRule
    public MCPsharp.Models.Architecture.DependencyDirection Direction { get; set; } = MCPsharp.Models.Architecture.DependencyDirection.Downward;
    public MCPsharp.Models.Architecture.RuleType Type { get; set; } = MCPsharp.Models.Architecture.RuleType.Forbidden;
    public string? Description { get; set; }
    public bool IsStrict { get; set; } = true;
}

/// <summary>
/// Architecture validation result
/// </summary>
public class ArchitectureValidation
{
    public bool IsValid { get; set; }
    public List<ArchitectureViolation> Violations { get; set; } = new();
    public List<ArchitectureWarning> Warnings { get; set; } = new();
}

/// <summary>
/// Architecture violation
/// </summary>
public class ArchitectureViolation
{
    public required string From { get; set; }
    public required string To { get; set; }
    public required string Rule { get; set; }
    public required Location Location { get; set; }
    public required string Description { get; set; }

    // Additional properties for compatibility with Models.Architecture.LayerViolation
    public string? SourceComponent { get; set; }
    public string? TargetComponent { get; set; }
    public string? RuleName { get; set; }
    public int Line { get; set; }
}

/// <summary>
/// Architecture warning
/// </summary>
public class ArchitectureWarning
{
    public required string Type { get; set; }
    public required string Message { get; set; }
    public required Location Location { get; set; }
}
