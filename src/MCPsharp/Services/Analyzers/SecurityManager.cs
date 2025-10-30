using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Implementation of security manager for analyzer validation and access control
/// </summary>
public class SecurityManager : ISecurityManager
{
    private readonly ILogger<SecurityManager> _logger;
    private readonly string _basePath;
    private readonly Dictionary<string, AnalyzerPermissions> _permissions;
    private readonly List<X509Certificate2> _trustedCertificates;
    private readonly List<SecurityEvent> _securityLog;
    private readonly object _logLock = new();

    public SecurityManager(ILogger<SecurityManager> logger, string basePath)
    {
        _logger = logger;
        _basePath = basePath;
        _permissions = new Dictionary<string, AnalyzerPermissions>();
        _trustedCertificates = new List<X509Certificate2>();
        _securityLog = new List<SecurityEvent>();
    }

    public async Task<SecurityValidationResult> ValidateAssemblyAsync(string assemblyPath)
    {
        try
        {
            _logger.LogInformation("Validating assembly: {AssemblyPath}", assemblyPath);

            if (!File.Exists(assemblyPath))
            {
                return new SecurityValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Assembly not found: {assemblyPath}",
                    ValidatedAt = DateTime.UtcNow
                };
            }

            var checksum = await ComputeChecksumAsync(assemblyPath);
            var maliciousPatterns = await ScanForMaliciousPatternsAsync(assemblyPath);

            // Check signature
            var signatureResult = await CheckSignatureAsync(assemblyPath);
            var isSigned = signatureResult.IsValid;
            var isTrusted = isSigned && await IsSignerTrustedAsync(signatureResult.Signer);

            var result = new SecurityValidationResult
            {
                IsValid = !maliciousPatterns.Any() && isTrusted,
                IsSigned = isSigned,
                IsTrusted = isTrusted,
                HasMaliciousPatterns = maliciousPatterns.Any(),
                Signer = signatureResult.Signer,
                Checksum = checksum,
                Warnings = maliciousPatterns.Any() ? ImmutableArray.Create("Potential malicious patterns detected") : ImmutableArray<string>.Empty,
                ValidatedAt = DateTime.UtcNow
            };

            await LogSecurityEventAsync(new SecurityEvent
            {
                AnalyzerId = Path.GetFileNameWithoutExtension(assemblyPath),
                EventType = SecurityEventType.AssemblyValidation,
                Operation = "ValidateAssembly",
                TargetPath = assemblyPath,
                Success = result.IsValid,
                Details = $"Valid: {result.IsValid}, Signed: {isSigned}, Trusted: {isTrusted}",
                Metadata = new Dictionary<string, object>
                {
                    ["Checksum"] = checksum ?? string.Empty,
                    ["Signer"] = signatureResult.Signer ?? string.Empty,
                    ["MaliciousPatterns"] = maliciousPatterns.Count
                }.ToImmutableDictionary()
            });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating assembly: {AssemblyPath}", assemblyPath);

            await LogSecurityEventAsync(new SecurityEvent
            {
                AnalyzerId = Path.GetFileNameWithoutExtension(assemblyPath),
                EventType = SecurityEventType.AssemblyValidation,
                Operation = "ValidateAssembly",
                TargetPath = assemblyPath,
                Success = false,
                Details = ex.Message
            });

            return new SecurityValidationResult
            {
                IsValid = false,
                ErrorMessage = ex.Message,
                ValidatedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<bool> IsOperationAllowedAsync(string analyzerId, string operation, string? targetPath = null)
    {
        try
        {
            if (!_permissions.TryGetValue(analyzerId, out var permissions))
            {
                // Default permissions for unknown analyzers
                permissions = new AnalyzerPermissions();
            }

            // Check basic permissions
            var allowed = operation switch
            {
                "ReadFile" => permissions.CanReadFiles,
                "WriteFile" => permissions.CanWriteFiles,
                "ExecuteCommand" => permissions.CanExecuteCommands,
                "NetworkAccess" => permissions.CanAccessNetwork,
                "FileSystemAccess" => permissions.CanAccessFileSystem,
                _ => permissions.AllowedOperations.Contains(operation)
            };

            // Check path-specific permissions
            if (allowed && !string.IsNullOrEmpty(targetPath))
            {
                allowed = IsPathAllowed(analyzerId, targetPath);
            }

            await LogSecurityEventAsync(new SecurityEvent
            {
                AnalyzerId = analyzerId,
                EventType = SecurityEventType.PermissionCheck,
                Operation = operation,
                TargetPath = targetPath,
                Success = allowed,
                Details = $"Operation {operation} {(allowed ? "allowed" : "denied")} for analyzer {analyzerId}"
            });

            return allowed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking operation permission for analyzer: {AnalyzerId}, operation: {Operation}", analyzerId, operation);
            return false;
        }
    }

    public async Task LogSecurityEventAsync(SecurityEvent securityEvent)
    {
        try
        {
            lock (_logLock)
            {
                _securityLog.Add(securityEvent);

                // Keep only last 10000 events
                if (_securityLog.Count > 10000)
                {
                    _securityLog.RemoveAt(0);
                }
            }

            _logger.LogInformation(
                "Security Event: {EventType} - Analyzer: {AnalyzerId}, Operation: {Operation}, Success: {Success}, Details: {Details}",
                securityEvent.EventType,
                securityEvent.AnalyzerId,
                securityEvent.Operation,
                securityEvent.Success,
                securityEvent.Details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging security event");
        }
    }

    public async Task<AnalyzerPermissions> GetPermissionsAsync(string analyzerId)
    {
        return _permissions.TryGetValue(analyzerId, out var permissions)
            ? permissions
            : new AnalyzerPermissions();
    }

    public async Task SetPermissionsAsync(string analyzerId, AnalyzerPermissions permissions)
    {
        _permissions[analyzerId] = permissions;

        await LogSecurityEventAsync(new SecurityEvent
        {
            AnalyzerId = analyzerId,
            EventType = SecurityEventType.PermissionCheck,
            Operation = "SetPermissions",
            Success = true,
            Details = $"Permissions updated for analyzer {analyzerId}",
            Metadata = new Dictionary<string, object>
            {
                ["CanReadFiles"] = permissions.CanReadFiles,
                ["CanWriteFiles"] = permissions.CanWriteFiles,
                ["CanExecuteCommands"] = permissions.CanExecuteCommands,
                ["CanAccessNetwork"] = permissions.CanAccessNetwork
            }.ToImmutableDictionary()
        });
    }

    public bool IsPathAllowed(string analyzerId, string path)
    {
        try
        {
            if (!_permissions.TryGetValue(analyzerId, out var permissions))
            {
                // Default: allow access to workspace directory
                return Path.GetFullPath(path).StartsWith(Path.GetFullPath(_basePath), StringComparison.OrdinalIgnoreCase);
            }

            var fullPath = Path.GetFullPath(path);

            // Check denied paths first
            foreach (var deniedPath in permissions.DeniedPaths)
            {
                if (fullPath.StartsWith(Path.GetFullPath(deniedPath), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Check allowed paths
            if (permissions.AllowedPaths.Any())
            {
                return permissions.AllowedPaths.Any(allowedPath =>
                    fullPath.StartsWith(Path.GetFullPath(allowedPath), StringComparison.OrdinalIgnoreCase));
            }

            // Default: allow if not explicitly denied
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking path permission for analyzer: {AnalyzerId}, path: {Path}", analyzerId, path);
            return false;
        }
    }

    public ImmutableArray<X509Certificate2> GetTrustedCertificates()
    {
        return _trustedCertificates.ToImmutableArray();
    }

    public async Task AddTrustedCertificateAsync(X509Certificate2 certificate)
    {
        _trustedCertificates.Add(certificate);
        _logger.LogInformation("Added trusted certificate: {Thumbprint}", certificate.Thumbprint);
    }

    public async Task RemoveTrustedCertificateAsync(string thumbprint)
    {
        var cert = _trustedCertificates.FirstOrDefault(c => c.Thumbprint == thumbprint);
        if (cert != null)
        {
            _trustedCertificates.Remove(cert);
            _logger.LogInformation("Removed trusted certificate: {Thumbprint}", thumbprint);
        }
    }

    private async Task<string?> ComputeChecksumAsync(string filePath)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToBase64String(hash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing checksum for file: {FilePath}", filePath);
            return null;
        }
    }

    private async Task<(bool IsValid, string? Signer)> CheckSignatureAsync(string assemblyPath)
    {
        string? signer = null;
        try
        {
            var certificate = X509Certificate2.CreateFromSignedFile(assemblyPath);
            if (certificate != null)
            {
                signer = certificate.Subject;
                return (true, signer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Assembly is not signed: {AssemblyPath}", assemblyPath);
        }
        return (false, signer);
    }

    private async Task<bool> IsSignerTrustedAsync(string? signer)
    {
        if (string.IsNullOrEmpty(signer))
            return false;

        return _trustedCertificates.Any(cert =>
            cert.Subject.Contains(signer, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<string>> ScanForMaliciousPatternsAsync(string assemblyPath)
    {
        var patterns = new List<string>();

        try
        {
            // Read assembly bytes for pattern matching
            var bytes = await File.ReadAllBytesAsync(assemblyPath);
            var content = Encoding.UTF8.GetString(bytes);

            // Check for suspicious patterns
            var suspiciousPatterns = new[]
            {
                "eval(",
                "exec(",
                "System.Diagnostics.Process.Start",
                "Reflection.Emit",
                "LoadFrom",
                "CreateInstance",
                "InvokeMember"
            };

            foreach (var pattern in suspiciousPatterns)
            {
                if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    patterns.Add($"Suspicious pattern detected: {pattern}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning for malicious patterns: {AssemblyPath}", assemblyPath);
        }

        return patterns;
    }
}