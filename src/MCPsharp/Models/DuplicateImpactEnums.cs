namespace MCPsharp.Models;

/// <summary>
/// Hotspot risk levels
/// </summary>
public enum HotspotRiskLevel
{
    /// <summary>
    /// Low risk
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk
    /// </summary>
    Medium,

    /// <summary>
    /// High risk
    /// </summary>
    High,

    /// <summary>
    /// Critical risk
    /// </summary>
    Critical
}


/// <summary>
/// Dependency impact types
/// </summary>
public enum DependencyImpactType
{
    /// <summary>
    /// No impact
    /// </summary>
    None,

    /// <summary>
    /// Minor impact
    /// </summary>
    Minor,

    /// <summary>
    /// Major impact
    /// </summary>
    Major,

    /// <summary>
    /// Breaking change
    /// </summary>
    BreakingChange
}

/// <summary>
/// Impact levels
/// </summary>
public enum ImpactLevel
{
    /// <summary>
    /// Negligible impact
    /// </summary>
    Negligible,

    /// <summary>
    /// Minor impact
    /// </summary>
    Minor,

    /// <summary>
    /// Moderate impact
    /// </summary>
    Moderate,

    /// <summary>
    /// Significant impact
    /// </summary>
    Significant,

    /// <summary>
    /// Critical impact
    /// </summary>
    Critical
}
