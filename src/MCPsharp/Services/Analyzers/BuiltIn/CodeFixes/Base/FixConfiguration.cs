using System.Collections.Immutable;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;

/// <summary>
/// Configuration for automated code fix operations
/// </summary>
public record FixConfiguration
{
    /// <summary>
    /// Fix profile to use (conservative, balanced, aggressive)
    /// </summary>
    public FixProfile Profile { get; init; } = FixProfile.Balanced;

    /// <summary>
    /// Whether to require user approval before applying fixes
    /// </summary>
    public bool RequireUserApproval { get; init; } = true;

    /// <summary>
    /// Whether to create backups before applying fixes
    /// </summary>
    public bool CreateBackup { get; init; } = true;

    /// <summary>
    /// Whether to validate files after applying fixes
    /// </summary>
    public bool ValidateAfterApply { get; init; } = true;

    /// <summary>
    /// Whether to stop on first error
    /// </summary>
    public bool StopOnFirstError { get; init; } = false;

    /// <summary>
    /// Specific fix providers to include (empty = all for profile)
    /// </summary>
    public ImmutableArray<string> IncludedProviders { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Specific fix providers to exclude
    /// </summary>
    public ImmutableArray<string> ExcludedProviders { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Additional properties for configuration
    /// </summary>
    public ImmutableDictionary<string, object> Properties { get; init; } = ImmutableDictionary<string, object>.Empty;

    /// <summary>
    /// Default conservative configuration
    /// </summary>
    public static readonly FixConfiguration ConservativeDefault = new()
    {
        Profile = FixProfile.Conservative,
        RequireUserApproval = false,
        CreateBackup = true,
        ValidateAfterApply = true
    };

    /// <summary>
    /// Default balanced configuration
    /// </summary>
    public static readonly FixConfiguration BalancedDefault = new()
    {
        Profile = FixProfile.Balanced,
        RequireUserApproval = true,
        CreateBackup = true,
        ValidateAfterApply = true
    };

    /// <summary>
    /// Default aggressive configuration
    /// </summary>
    public static readonly FixConfiguration AggressiveDefault = new()
    {
        Profile = FixProfile.Aggressive,
        RequireUserApproval = true,
        CreateBackup = true,
        ValidateAfterApply = true,
        StopOnFirstError = false
    };
}
