using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MCPsharp.Models.Analyzers;

/// <summary>
/// Security manager for analyzer validation and access control
/// </summary>
public interface ISecurityManager
{
    /// <summary>
    /// Validate an analyzer assembly
    /// </summary>
    Task<SecurityValidationResult> ValidateAssemblyAsync(string assemblyPath);

    /// <summary>
    /// Check if an operation is allowed
    /// </summary>
    Task<bool> IsOperationAllowedAsync(string analyzerId, string operation, string? targetPath = null);

    /// <summary>
    /// Log a security event
    /// </summary>
    Task LogSecurityEventAsync(SecurityEvent securityEvent);

    /// <summary>
    /// Get permissions for an analyzer
    /// </summary>
    Task<AnalyzerPermissions> GetPermissionsAsync(string analyzerId);

    /// <summary>
    /// Set permissions for an analyzer
    /// </summary>
    Task SetPermissionsAsync(string analyzerId, AnalyzerPermissions permissions);

    /// <summary>
    /// Check if a file path is allowed for access
    /// </summary>
    bool IsPathAllowed(string analyzerId, string path);

    /// <summary>
    /// Get trusted certificates
    /// </summary>
    ImmutableArray<X509Certificate2> GetTrustedCertificates();

    /// <summary>
    /// Add a trusted certificate
    /// </summary>
    Task AddTrustedCertificateAsync(X509Certificate2 certificate);

    /// <summary>
    /// Remove a trusted certificate
    /// </summary>
    Task RemoveTrustedCertificateAsync(string thumbprint);
}

/// <summary>
/// Result of security validation
/// </summary>
public record SecurityValidationResult
{
    public bool IsValid { get; init; }
    public bool IsSigned { get; init; }
    public bool IsTrusted { get; init; }
    public bool HasMaliciousPatterns { get; init; }
    public string? Signer { get; init; }
    public string? ErrorMessage { get; init; }
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> Violations { get; init; } = ImmutableArray<string>.Empty;
    public string? Checksum { get; init; }
    public DateTime ValidatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Security event for auditing
/// </summary>
public record SecurityEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string AnalyzerId { get; init; } = string.Empty;
    public SecurityEventType EventType { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string? TargetPath { get; init; }
    public string? Details { get; init; }
    public bool Success { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? UserContext { get; init; }
    public ImmutableDictionary<string, object> Metadata { get; init; } = ImmutableDictionary<string, object>.Empty;
}

/// <summary>
/// Type of security event
/// </summary>
public enum SecurityEventType
{
    AssemblyValidation,
    PermissionCheck,
    FileAccess,
    OperationAttempt,
    SignatureVerification,
    MaliciousPatternDetected,
    UnauthorizedAccess
}

/// <summary>
/// Permissions for an analyzer
/// </summary>
public record AnalyzerPermissions
{
    public bool CanReadFiles { get; init; } = true;
    public bool CanWriteFiles { get; init; } = false;
    public bool CanExecuteCommands { get; init; } = false;
    public bool CanAccessNetwork { get; init; } = false;
    public bool CanAccessFileSystem { get; init; } = true;
    public ImmutableArray<string> AllowedPaths { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> DeniedPaths { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> AllowedOperations { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> DeniedOperations { get; init; } = ImmutableArray<string>.Empty;
    public DateTime GrantedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; init; }
    public string? GrantedBy { get; init; }
}