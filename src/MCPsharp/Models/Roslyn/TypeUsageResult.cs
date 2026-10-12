namespace MCPsharp.Models.Roslyn;

/// <summary>
/// Result of analyzing type usage across the codebase
/// </summary>
public class TypeUsageResult
{
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public List<TypeUsageInfo> Usages { get; init; } = new();
    public int TotalUsages => Usages.Count;
    public DateTime AnalysisTime { get; init; } = DateTime.UtcNow;
    public TypeUsageMetadata Metadata { get; init; } = new();

    /// <summary>
    /// Get usages by usage type
    /// </summary>
    public Dictionary<TypeUsageKind, List<TypeUsageInfo>> UsagesByKind =>
        Usages.GroupBy(u => u.UsageKind)
              .ToDictionary(g => g.Key, g => g.ToList());

    /// <summary>
    /// Get declarations of this type
    /// </summary>
    public IEnumerable<TypeUsageInfo> Declarations =>
        Usages.Where(u => u.UsageKind == TypeUsageKind.TypeDeclaration);

    /// <summary>
    /// Get instantiations of this type
    /// </summary>
    public IEnumerable<TypeUsageInfo> Instantiations =>
        Usages.Where(u => u.UsageKind == TypeUsageKind.Instantiation);

    /// <summary>
    /// Get method parameter usages
    /// </summary>
    public IEnumerable<TypeUsageInfo> ParameterUsages =>
        Usages.Where(u => u.UsageKind == TypeUsageKind.Parameter);

    /// <summary>
    /// Get return type usages
    /// </summary>
    public IEnumerable<TypeUsageInfo> ReturnUsages =>
        Usages.Where(u => u.UsageKind == TypeUsageKind.ReturnType);

    /// <summary>
    /// Get property usages
    /// </summary>
    public IEnumerable<TypeUsageInfo> PropertyUsages =>
        Usages.Where(u => u.UsageKind == TypeUsageKind.Property);

    /// <summary>
    /// Get field usages
    /// </summary>
    public IEnumerable<TypeUsageInfo> FieldUsages =>
        Usages.Where(u => u.UsageKind == TypeUsageKind.Field);

    /// <summary>
    /// Get generic constraint usages
    /// </summary>
    public IEnumerable<TypeUsageInfo> GenericConstraintUsages =>
        Usages.Where(u => u.UsageKind == TypeUsageKind.GenericConstraint);

    /// <summary>
    /// Get inheritance usages (base class, interface implementation)
    /// </summary>
    public IEnumerable<TypeUsageInfo> InheritanceUsages =>
        Usages.Where(u => u.UsageKind == TypeUsageKind.BaseClass || u.UsageKind == TypeUsageKind.InterfaceImplementation);

    /// <summary>
    /// Get files where this type is used
    /// </summary>
    public IEnumerable<string> UsedInFiles =>
        Usages.Select(u => u.File).Distinct();
}

/// <summary>
/// Information about a single type usage
/// </summary>
public class TypeUsageInfo
{
    public required string File { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public required TypeUsageKind UsageKind { get; init; }
    public required string Context { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public string? MemberName { get; init; }
    public string? ContainerName { get; init; }
    public string? AccessModifier { get; init; }
    public bool IsGeneric { get; init; }
    public List<string> GenericArguments { get; init; } = new();

    /// <summary>
    /// Location for display
    /// </summary>
    public string Location => $"{Path.GetFileName(File)}:{Line + 1}:{Column + 1}";

    /// <summary>
    /// Get detailed description of the usage
    /// </summary>
    public string GetDescription()
    {
        var parts = new List<string> { UsageKind.ToString().ToLowerInvariant() };

        if (!string.IsNullOrEmpty(MemberName))
            parts.Add($"in {MemberName}");

        if (!string.IsNullOrEmpty(ContainerName))
            parts.Add($"in {ContainerName}");

        if (IsGeneric && GenericArguments.Count > 0)
            parts.Add($"with generic arguments [{string.Join(", ", GenericArguments)}]");

        return string.Join(" ", parts);
    }
}

/// <summary>
/// Metadata about the type usage analysis
/// </summary>
public class TypeUsageMetadata
{
    public TimeSpan AnalysisDuration { get; init; }
    public int FilesAnalyzed { get; init; }
    public int TypesAnalyzed { get; init; }
    public List<string> AnalysisWarnings { get; init; } = new();
    public bool IncompleteAnalysis { get; init; }
    public string? AnalysisMethod { get; init; }
    public Dictionary<string, object> AdditionalData { get; init; } = new();
}

/// <summary>
/// Different ways a type can be used
/// </summary>
public enum TypeUsageKind
{
    /// <summary>
    /// Type declaration (class, interface, struct, enum)
    /// </summary>
    TypeDeclaration,

    /// <summary>
    /// Object instantiation (new keyword)
    /// </summary>
    Instantiation,

    /// <summary>
    /// Method parameter
    /// </summary>
    Parameter,

    /// <summary>
    /// Method return type
    /// </summary>
    ReturnType,

    /// <summary>
    /// Property type
    /// </summary>
    Property,

    /// <summary>
    /// Field type
    /// </summary>
    Field,

    /// <summary>
    /// Variable declaration
    /// </summary>
    Variable,

    /// <summary>
    /// Generic type argument
    /// </summary>
    GenericArgument,

    /// <summary>
    /// Generic constraint
    /// </summary>
    GenericConstraint,

    /// <summary>
    /// Base class inheritance
    /// </summary>
    BaseClass,

    /// <summary>
    /// Interface implementation
    /// </summary>
    InterfaceImplementation,

    /// <summary>
    /// Type cast
    /// </summary>
    TypeCast,

    /// <summary>
    /// TypeOf expression
    /// </summary>
    TypeOf,

    /// <summary>
    /// Is operator
    /// </summary>
    IsOperator,

    /// <summary>
    /// As operator
    /// </summary>
    AsOperator,

    /// <summary>
    /// Using directive
    /// </summary>
    UsingDirective,

    /// <summary>
    /// Namespace qualification
    /// </summary>
    NamespaceQualification,

    /// <summary>
    /// Unknown usage
    /// </summary>
    Unknown
}