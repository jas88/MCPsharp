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
public partial class SecurityManager : ISecurityManager
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

    public async Task<SecurityValidationResult> ValidateAssemblyAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        try
        {
            LogValidatingAssembly(_logger, assemblyPath);

            if (!File.Exists(assemblyPath))
            {
                return new SecurityValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Assembly not found: {assemblyPath}",
                    ValidatedAt = DateTime.UtcNow
                };
            }

            var checksum = await ComputeChecksumAsync(assemblyPath, cancellationToken);
            var maliciousPatterns = await ScanForMaliciousPatternsAsync(assemblyPath, cancellationToken);

            // Check signature
            var signatureResult = CheckSignature(assemblyPath);
            var isSigned = signatureResult.IsValid;
            var isTrusted = isSigned && IsSignerTrusted(signatureResult.Signer);
            var hasMaliciousPatterns = maliciousPatterns.Count > 0;

            var result = new SecurityValidationResult
            {
                IsValid = !hasMaliciousPatterns && isTrusted,
                IsSigned = isSigned,
                IsTrusted = isTrusted,
                HasMaliciousPatterns = hasMaliciousPatterns,
                Signer = signatureResult.Signer,
                Checksum = checksum,
                Warnings = hasMaliciousPatterns ? ImmutableArray.Create("Potential malicious patterns detected") : ImmutableArray<string>.Empty,
                ValidatedAt = DateTime.UtcNow
            };

            _ = LogSecurityEventAsync(new SecurityEvent
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
            LogValidateAssemblyError(_logger, ex, assemblyPath);

            _ = LogSecurityEventAsync(new SecurityEvent
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

    public Task<bool> IsOperationAllowedAsync(string analyzerId, string operation, string? targetPath = null)
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

            _ = LogSecurityEventAsync(new SecurityEvent
            {
                AnalyzerId = analyzerId,
                EventType = SecurityEventType.PermissionCheck,
                Operation = operation,
                TargetPath = targetPath,
                Success = allowed,
                Details = $"Operation {operation} {(allowed ? "allowed" : "denied")} for analyzer {analyzerId}"
            });

            return Task.FromResult(allowed);
        }
        catch (Exception ex)
        {
            LogOperationPermissionError(_logger, ex, analyzerId, operation);
            return Task.FromResult(false);
        }
    }

    public Task LogSecurityEventAsync(SecurityEvent securityEvent)
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

            LogSecurityEvent(
                _logger,
                securityEvent.EventType,
                securityEvent.AnalyzerId,
                securityEvent.Operation,
                securityEvent.Success,
                securityEvent.Details);
        }
        catch (Exception ex)
        {
            LogSecurityEventError(_logger, ex);
        }

        return Task.CompletedTask;
    }

    public Task<AnalyzerPermissions> GetPermissionsAsync(string analyzerId)
    {
        var result = _permissions.TryGetValue(analyzerId, out var permissions)
            ? permissions
            : new AnalyzerPermissions();

        return Task.FromResult(result);
    }

    public Task SetPermissionsAsync(string analyzerId, AnalyzerPermissions permissions)
    {
        _permissions[analyzerId] = permissions;

        _ = LogSecurityEventAsync(new SecurityEvent
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

        return Task.CompletedTask;
    }

    public bool IsPathAllowed(string analyzerId, string path)
    {
        try
        {
            if (!_permissions.TryGetValue(analyzerId, out var permissions))
            {
                // Default: allow access to workspace directory
                return PathContainment.IsWithin(_basePath, path);
            }

            var fullPath = Path.GetFullPath(path);

            // Check denied paths first
            if (permissions.DeniedPaths.Any(deniedPath =>
                    PathContainment.IsWithin(deniedPath, fullPath)))
            {
                return false;
            }

            // Check allowed paths
            if (permissions.AllowedPaths.Any())
            {
                return permissions.AllowedPaths.Any(allowedPath =>
                    PathContainment.IsWithin(allowedPath, fullPath));
            }

            // Default: allow if not explicitly denied
            return true;
        }
        catch (Exception ex)
        {
            LogPathPermissionError(_logger, ex, analyzerId, path);
            return false;
        }
    }

    public ImmutableArray<X509Certificate2> GetTrustedCertificates()
    {
        return _trustedCertificates.ToImmutableArray();
    }

    public Task AddTrustedCertificateAsync(X509Certificate2 certificate)
    {
        _trustedCertificates.Add(certificate);
        LogTrustedCertificateAdded(_logger, certificate.Thumbprint);

        return Task.CompletedTask;
    }

    public Task RemoveTrustedCertificateAsync(string thumbprint)
    {
        var cert = _trustedCertificates.FirstOrDefault(c => c.Thumbprint == thumbprint);
        if (cert != null)
        {
            _trustedCertificates.Remove(cert);
            LogTrustedCertificateRemoved(_logger, thumbprint);
        }

        return Task.CompletedTask;
    }

    private async Task<string?> ComputeChecksumAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToBase64String(hash);
        }
        catch (Exception ex)
        {
            LogChecksumError(_logger, ex, filePath);
            return null;
        }
    }

    private (bool IsValid, string? Signer) CheckSignature(string assemblyPath)
    {
        try
        {
            var certificate = X509CertificateLoader.LoadCertificateFromFile(assemblyPath);
            if (certificate != null)
            {
                return (true, certificate.Subject);
            }
        }
        catch (Exception ex)
        {
            LogAssemblyNotSigned(_logger, ex, assemblyPath);
        }
        return (false, null);
    }

    private bool IsSignerTrusted(string? signer)
    {
        if (string.IsNullOrEmpty(signer))
            return false;

        return _trustedCertificates.Any(cert =>
            cert.Subject.Contains(signer, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<string>> ScanForMaliciousPatternsAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        var patterns = new List<string>();

        try
        {
            // Read assembly bytes for pattern matching
            var bytes = await File.ReadAllBytesAsync(assemblyPath, cancellationToken);
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

            patterns.AddRange(suspiciousPatterns
                .Where(pattern => content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .Select(pattern => $"Suspicious pattern detected: {pattern}"));
        }
        catch (Exception ex)
        {
            LogMaliciousPatternScanError(_logger, ex, assemblyPath);
        }

        return patterns;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Validating assembly: {AssemblyPath}")]
    private static partial void LogValidatingAssembly(ILogger logger, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error validating assembly: {AssemblyPath}")]
    private static partial void LogValidateAssemblyError(ILogger logger, Exception exception, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error checking operation permission for analyzer: {AnalyzerId}, operation: {Operation}")]
    private static partial void LogOperationPermissionError(ILogger logger, Exception exception, string analyzerId, string operation);

    [LoggerMessage(Level = LogLevel.Information, Message = "Security Event: {EventType} - Analyzer: {AnalyzerId}, Operation: {Operation}, Success: {Success}, Details: {Details}")]
    private static partial void LogSecurityEvent(ILogger logger, SecurityEventType eventType, string analyzerId, string operation, bool success, string? details);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error logging security event")]
    private static partial void LogSecurityEventError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error checking path permission for analyzer: {AnalyzerId}, path: {Path}")]
    private static partial void LogPathPermissionError(ILogger logger, Exception exception, string analyzerId, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Added trusted certificate: {Thumbprint}")]
    private static partial void LogTrustedCertificateAdded(ILogger logger, string thumbprint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed trusted certificate: {Thumbprint}")]
    private static partial void LogTrustedCertificateRemoved(ILogger logger, string thumbprint);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error computing checksum for file: {FilePath}")]
    private static partial void LogChecksumError(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Assembly is not signed: {AssemblyPath}")]
    private static partial void LogAssemblyNotSigned(ILogger logger, Exception exception, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error scanning for malicious patterns: {AssemblyPath}")]
    private static partial void LogMaliciousPatternScanError(ILogger logger, Exception exception, string assemblyPath);
}
