namespace MCPsharp.Models.Architecture;

/// <summary>
/// Types of architectural patterns
/// </summary>
public enum ArchitectureType
{
    Unknown,
    CleanArchitecture,
    OnionArchitecture,
    NTier,
    Hexagonal,
    Layered,
    Microservices,
    EventDriven,
    Custom
}

/// <summary>
/// Types of architectural layers
/// </summary>
public enum LayerType
{
    Unknown,
    Presentation, // UI, Controllers, API
    Application, // Use cases, Application services
    Domain, // Business logic, Entities
    Infrastructure, // Data access, External services
    Core, // Domain core, Business rules
    Service, // Service layer
    Repository, // Data repository layer
    Utility, // Helper utilities
    Configuration,
    Testing
}

/// <summary>
/// Direction of dependency flow
/// </summary>
public enum DependencyDirection
{
    Unknown,
    Upward, // Lower level depends on higher level
    Downward, // Higher level depends on lower level (preferred)
    Bidirectional, // Both ways (usually bad)
    None // No dependency allowed
}

/// <summary>
/// Types of dependency rules
/// </summary>
public enum RuleType
{
    Unknown,
    Required, // Dependency must exist
    Forbidden, // Dependency must not exist
    Recommended, // Dependency is recommended
    Discouraged // Dependency should be avoided
}

/// <summary>
/// Pattern matching types
/// </summary>
public enum PatternType
{
    Unknown,
    Namespace, // Namespace pattern matching
    ClassName, // Class name pattern matching
    Suffix, // Suffix pattern matching
    Prefix, // Prefix pattern matching
    Regex, // Regular expression pattern
    Attribute // Attribute-based pattern
}

/// <summary>
/// Severity levels for violations
/// </summary>
public enum ViolationSeverity
{
    Unknown,
    Info,
    Minor,
    Major,
    Critical,
    Blocker
}

/// <summary>
/// Types of nodes in dependency graph
/// </summary>
public enum NodeType
{
    Unknown,
    Class,
    Interface,
    AbstractClass,
    Enum,
    Struct,
    Record,
    Delegate,
    Module,
    Assembly,
    Namespace
}

/// <summary>
/// Types of dependencies
/// </summary>
public enum DependencyType
{
    Unknown,
    Inheritance, // Class inheritance
    Implementation, // Interface implementation
    Composition, // Field/property dependency
    Parameter, // Method parameter dependency
    Return, // Method return type dependency
    LocalVariable, // Local variable usage
    MethodCall, // Method invocation
    PropertyAccess, // Property/field access
    EventSubscription, // Event handler subscription
    GenericParameter, // Generic type parameter
    NamespaceImport, // Using statement
    AssemblyReference // Assembly reference
}

/// <summary>
/// Types of edges in architecture diagram
/// </summary>
public enum EdgeType
{
    Unknown,
    Solid, // Regular dependency
    Dashed, // Weak/optional dependency
    Dotted, // Indirect dependency
    Arrow, // Directed dependency
    Bidirectional, // Two-way dependency
    Curved, // Curved edge (for aesthetics)
    Violating // Violating dependency (different color)
}

/// <summary>
/// Layout types for architecture diagrams
/// </summary>
public enum LayoutType
{
    Unknown,
    Hierarchical, // Top-down hierarchy
    Layered, // Layer-by-layer layout
    Circular, // Circular layout
    ForceDirected, // Force-directed graph layout
    Grid, // Grid-based layout
    Tree, // Tree structure layout
    Custom // Custom layout algorithm
}

/// <summary>
/// Priority levels for recommendations
/// </summary>
public enum RecommendationPriority
{
    Unknown,
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Analysis confidence levels
/// </summary>
public enum AnalysisConfidence
{
    Unknown,
    VeryLow,
    Low,
    Medium,
    High,
    VeryHigh
}

/// <summary>
/// Status of architecture analysis
/// </summary>
public enum AnalysisStatus
{
    Unknown,
    NotStarted,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Types of architectural metrics
/// </summary>
public enum MetricType
{
    Unknown,
    Complexity, // Cyclomatic complexity
    Coupling, // Coupling between modules
    Cohesion, // Cohesion within modules
    Instability, // Instability metric (I = Ce / (Ca + Ce))
    Abstraction, // Abstraction metric
    Distance, // Distance from main sequence
    Dependencies, // Number of dependencies
    Violations, // Number of rule violations
    Compliance, // Compliance percentage
    Coverage // Test coverage
}