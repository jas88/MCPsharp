using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using System.Reflection;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Implementation of analyzer host for loading and managing analyzers
/// </summary>
public class AnalyzerHost : IAnalyzerHost
{
    private readonly ILogger<AnalyzerHost> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IAnalyzerRegistry _registry;
    private readonly ISecurityManager _securityManager;
    private readonly IFixEngine _fixEngine;
    private readonly Dictionary<string, IAnalyzerSandbox> _sandboxes;
    private readonly object _lock = new();

    public event EventHandler<AnalyzerLoadedEventArgs>? AnalyzerLoaded;
    public event EventHandler<AnalyzerUnloadedEventArgs>? AnalyzerUnloaded;
    public event EventHandler<AnalyzerUnregisteredEventArgs>? AnalyzerUnregistered;
    public event EventHandler<AnalysisCompletedEventArgs>? AnalysisCompleted;

    public AnalyzerHost(
        ILogger<AnalyzerHost> logger,
        ILoggerFactory loggerFactory,
        IAnalyzerRegistry registry,
        ISecurityManager securityManager,
        IFixEngine fixEngine)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _registry = registry;
        _securityManager = securityManager;
        _fixEngine = fixEngine;
        _sandboxes = new Dictionary<string, IAnalyzerSandbox>();

        // Subscribe to registry events
        _registry.AnalyzerRegistered += (sender, args) => AnalyzerLoaded?.Invoke(this, new AnalyzerLoadedEventArgs(args.AnalyzerId, args.AnalyzerInfo));
        _registry.AnalyzerUnregistered += (sender, args) => AnalyzerUnregistered?.Invoke(this, new AnalyzerUnregisteredEventArgs(args.AnalyzerId));
    }

    public async Task<AnalyzerLoadResult> LoadAnalyzerAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading analyzer from assembly: {AssemblyPath}", assemblyPath);

            if (!File.Exists(assemblyPath))
            {
                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = $"Assembly not found: {assemblyPath}"
                };
            }

            // Security validation
            var securityValidation = await _securityManager.ValidateAssemblyAsync(assemblyPath);
            if (!securityValidation.IsValid)
            {
                await _securityManager.LogSecurityEventAsync(new SecurityEvent
                {
                    AnalyzerId = Path.GetFileNameWithoutExtension(assemblyPath),
                    EventType = SecurityEventType.AssemblyValidation,
                    Operation = "LoadAnalyzer",
                    TargetPath = assemblyPath,
                    Success = false,
                    Details = $"Security validation failed: {securityValidation.ErrorMessage}"
                });

                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = $"Security validation failed: {securityValidation.ErrorMessage}",
                    SecurityValidation = securityValidation
                };
            }

            // Compatibility check
            var compatibilityResult = await _registry.ValidateCompatibilityAsync(assemblyPath, cancellationToken);
            if (!compatibilityResult.IsCompatible)
            {
                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = $"Compatibility check failed: {compatibilityResult.ErrorMessage}",
                    SecurityValidation = securityValidation,
                    Warnings = compatibilityResult.Warnings
                };
            }

            // Load analyzers from assembly
            var analyzerInfos = await _registry.DiscoverAnalyzersAsync(Path.GetDirectoryName(assemblyPath)!, cancellationToken);
            IAnalyzer? loadedAnalyzer = null;
            var loadedAnalyzers = new List<string>();

            foreach (var info in analyzerInfos.Where(i => i.AssemblyPath == assemblyPath))
            {
                try
                {
                    var analyzer = await CreateAnalyzerInstanceAsync(info, assemblyPath, cancellationToken);
                    if (analyzer != null)
                    {
                        var registered = await _registry.RegisterAnalyzerAsync(analyzer, cancellationToken);
                        if (registered)
                        {
                            loadedAnalyzers.Add(analyzer.Id);
                            loadedAnalyzer ??= analyzer; // Keep the first loaded analyzer

                            // Create sandbox for the analyzer
                            var sandbox = new AnalyzerSandbox(
                                _loggerFactory.CreateLogger<AnalyzerSandbox>(),
                                _securityManager);

                            lock (_lock)
                            {
                                _sandboxes[analyzer.Id] = sandbox;
                            }

                            // Set default permissions
                            await _securityManager.SetPermissionsAsync(analyzer.Id, new AnalyzerPermissions
                            {
                                CanReadFiles = true,
                                CanWriteFiles = false,
                                CanExecuteCommands = false,
                                CanAccessNetwork = false,
                                AllowedPaths = ImmutableArray.Create(Directory.GetCurrentDirectory())
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading analyzer: {AnalyzerId}", info.Id);
                }
            }

            if (!loadedAnalyzers.Any())
            {
                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = "No valid analyzers found in assembly",
                    SecurityValidation = securityValidation,
                    Warnings = compatibilityResult.Warnings
                };
            }

            return new AnalyzerLoadResult
            {
                Success = true,
                AnalyzerId = loadedAnalyzers.First(), // Return the first loaded analyzer ID
                Analyzer = loadedAnalyzer,
                SecurityValidation = securityValidation,
                Warnings = compatibilityResult.Warnings.Concat(securityValidation.Warnings).ToImmutableArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading analyzer assembly: {AssemblyPath}", assemblyPath);
            return new AnalyzerLoadResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> UnloadAnalyzerAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Unloading analyzer: {AnalyzerId}", analyzerId);

            // Remove sandbox
            IAnalyzerSandbox? sandbox = null;
            lock (_lock)
            {
                if (_sandboxes.TryGetValue(analyzerId, out sandbox))
                {
                    _sandboxes.Remove(analyzerId);
                }
            }

            sandbox?.Dispose();

            // Unregister from registry
            var unregistered = await _registry.UnregisterAnalyzerAsync(analyzerId, cancellationToken);

            if (unregistered)
            {
                await _securityManager.LogSecurityEventAsync(new SecurityEvent
                {
                    AnalyzerId = analyzerId,
                    EventType = SecurityEventType.AssemblyValidation,
                    Operation = "UnloadAnalyzer",
                    Success = true,
                    Details = $"Analyzer {analyzerId} unloaded successfully"
                });
            }

            return unregistered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unloading analyzer: {AnalyzerId}", analyzerId);
            return false;
        }
    }

    public ImmutableArray<AnalyzerInfo> GetLoadedAnalyzers()
    {
        return _registry.GetRegisteredAnalyzers();
    }

    public IAnalyzer? GetAnalyzer(string analyzerId)
    {
        return _registry.GetAnalyzer(analyzerId);
    }

    public AnalyzerInfo? GetAnalyzerInfo(string analyzerId)
    {
        return _registry.GetAnalyzerInfo(analyzerId);
    }

    public async Task<bool> SetAnalyzerEnabledAsync(string analyzerId, bool enabled, CancellationToken cancellationToken = default)
    {
        try
        {
            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return false;
            }

            analyzer.IsEnabled = enabled;

            await _securityManager.LogSecurityEventAsync(new SecurityEvent
            {
                AnalyzerId = analyzerId,
                EventType = SecurityEventType.PermissionCheck,
                Operation = "SetEnabled",
                Success = true,
                Details = $"Analyzer {analyzerId} {(enabled ? "enabled" : "disabled")}"
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting analyzer enabled state: {AnalyzerId}", analyzerId);
            return false;
        }
    }

    public async Task<bool> ConfigureAnalyzerAsync(string analyzerId, AnalyzerConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return false;
            }

            analyzer.Configuration = configuration;

            await _securityManager.LogSecurityEventAsync(new SecurityEvent
            {
                AnalyzerId = analyzerId,
                EventType = SecurityEventType.PermissionCheck,
                Operation = "Configure",
                Success = true,
                Details = $"Analyzer {analyzerId} configuration updated"
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring analyzer: {AnalyzerId}", analyzerId);
            return false;
        }
    }

    public async Task<AnalysisSessionResult> RunAnalysisAsync(AnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("Starting analysis session {SessionId} with analyzer {AnalyzerId}", sessionId, request.AnalyzerId);

            var analyzer = _registry.GetAnalyzer(request.AnalyzerId);
            if (analyzer == null)
            {
                return new AnalysisSessionResult
                {
                    SessionId = sessionId,
                    AnalyzerId = request.AnalyzerId,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    Success = false,
                    ErrorMessage = $"Analyzer not found: {request.AnalyzerId}"
                };
            }

            if (!analyzer.IsEnabled)
            {
                return new AnalysisSessionResult
                {
                    SessionId = sessionId,
                    AnalyzerId = request.AnalyzerId,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    Success = false,
                    ErrorMessage = $"Analyzer is disabled: {request.AnalyzerId}"
                };
            }

            // Apply configuration if provided
            if (request.Configuration != null)
            {
                analyzer.Configuration = request.Configuration;
            }

            // Get sandbox
            IAnalyzerSandbox sandbox;
            lock (_lock)
            {
                if (!_sandboxes.TryGetValue(request.AnalyzerId, out sandbox!))
                {
                    sandbox = new AnalyzerSandbox(
                        _loggerFactory.CreateLogger<AnalyzerSandbox>(),
                        _securityManager);
                    _sandboxes[request.AnalyzerId] = sandbox;
                }
            }

            // Analyze files
            var results = new List<AnalysisResult>();
            var allIssues = new List<AnalyzerIssue>();

            foreach (var filePath in request.Files)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        _logger.LogWarning("File not found for analysis: {FilePath}", filePath);
                        continue;
                    }

                    // Check read permission
                    var canRead = await _securityManager.IsOperationAllowedAsync(request.AnalyzerId, "ReadFile", filePath);
                    if (!canRead)
                    {
                        _logger.LogWarning("Analyzer {AnalyzerId} not allowed to read file: {FilePath}", request.AnalyzerId, filePath);
                        continue;
                    }

                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var result = await sandbox.ExecuteAnalyzerAsync(analyzer, filePath, content, cancellationToken);

                    results.Add(result);
                    allIssues.AddRange(result.Issues);

                    // Filter issues if specific rules requested
                    if (request.Rules.Any())
                    {
                        allIssues = allIssues.Where(i => request.Rules.Contains(i.RuleId)).ToList();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error analyzing file: {FilePath}", filePath);
                    results.Add(new AnalysisResult
                    {
                        FilePath = filePath,
                        AnalyzerId = request.AnalyzerId,
                        StartTime = DateTime.UtcNow,
                        EndTime = DateTime.UtcNow,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }

            var sessionResult = new AnalysisSessionResult
            {
                SessionId = sessionId,
                AnalyzerId = request.AnalyzerId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = true,
                Results = results.ToImmutableArray(),
                AllIssues = allIssues.ToImmutableArray(),
                Statistics = new Dictionary<string, object>
                {
                    ["FilesAnalyzed"] = results.Count,
                    ["IssuesFound"] = allIssues.Count,
                    ["FilesWithIssues"] = results.Count(r => r.Issues.Any()),
                    ["AnalysisDuration"] = DateTime.UtcNow - startTime
                }.ToImmutableDictionary()
            };

            // Generate fixes if requested
            if (request.GenerateFixes && allIssues.Any())
            {
                await GenerateFixesAsync(sessionResult, cancellationToken);
            }

            AnalysisCompleted?.Invoke(this, new AnalysisCompletedEventArgs(sessionId, sessionResult));

            return sessionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in analysis session {SessionId}", sessionId);
            return new AnalysisSessionResult
            {
                SessionId = sessionId,
                AnalyzerId = request.AnalyzerId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<ImmutableArray<AnalyzerFix>> GetFixesAsync(string analyzerId, ImmutableArray<string> issueIds, CancellationToken cancellationToken = default)
    {
        try
        {
            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return ImmutableArray<AnalyzerFix>.Empty;
            }

            var fixes = new List<AnalyzerFix>();

            foreach (var issueId in issueIds)
            {
                // This is a simplified approach - in practice, we'd need to map issue IDs to rule IDs
                // For now, return all available fixes
                var analyzerFixes = analyzer.GetFixes("ALL");
                fixes.AddRange(analyzerFixes);
            }

            return fixes.DistinctBy(f => f.Id).ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting fixes for analyzer: {AnalyzerId}", analyzerId);
            return ImmutableArray<AnalyzerFix>.Empty;
        }
    }

    public async Task<FixSessionResult> ApplyFixesAsync(ApplyFixRequest request, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("Starting fix session {SessionId} with analyzer {AnalyzerId}", sessionId, request.AnalyzerId);

            var analyzer = _registry.GetAnalyzer(request.AnalyzerId);
            if (analyzer == null)
            {
                return new FixSessionResult
                {
                    SessionId = sessionId,
                    AnalyzerId = request.AnalyzerId,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    Success = false,
                    ErrorMessage = $"Analyzer not found: {request.AnalyzerId}"
                };
            }

            // Get fixes for issues
            var fixes = await GetFixesAsync(request.AnalyzerId, request.IssueIds, cancellationToken);
            var selectedFixes = fixes.Where(f => request.FixIds.Contains(f.Id) || request.FixIds.IsEmpty).ToImmutableArray();

            if (!selectedFixes.Any())
            {
                return new FixSessionResult
                {
                    SessionId = sessionId,
                    AnalyzerId = request.AnalyzerId,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    Success = false,
                    ErrorMessage = "No fixes found for the specified issues"
                };
            }

            // Apply fixes using the fix engine
            var fixOptions = new FixApplicationOptions
            {
                CreateBackup = true,
                ResolveConflicts = request.ResolveConflicts,
                PreviewOnly = request.PreviewOnly
            };

            var issues = request.IssueIds.Select(id => new AnalyzerIssue
            {
                Id = id,
                RuleId = "UNKNOWN", // Would need to resolve this properly
                AnalyzerId = request.AnalyzerId,
                FilePath = "UNKNOWN" // Would need to resolve this properly
            }).ToImmutableArray();

            var applicationResult = await _fixEngine.ApplyFixesAsync(issues, selectedFixes, fixOptions, cancellationToken);

            return new FixSessionResult
            {
                SessionId = sessionId,
                AnalyzerId = request.AnalyzerId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = applicationResult.Success,
                Results = applicationResult.AppliedFixes.Select(f => new FixResult
                {
                    Id = f.Id,
                    FixId = f.FixId,
                    AnalyzerId = f.AnalyzerId,
                    Success = f.Success,
                    ErrorMessage = f.ErrorMessage,
                    ModifiedFiles = f.ModifiedFiles,
                    Conflicts = f.Conflicts,
                    AppliedAt = f.AppliedAt,
                    Metrics = f.Metrics
                }).ToImmutableArray(),
                ModifiedFiles = applicationResult.ModifiedFiles,
                Conflicts = applicationResult.ResolvedConflicts.SelectMany(c => c.ConflictingFixIds).ToImmutableArray(),
                Statistics = new Dictionary<string, object>
                {
                    ["FixesApplied"] = applicationResult.AppliedFixes.Count(f => f.Success),
                    ["FilesModified"] = applicationResult.ModifiedFiles.Length,
                    ["ConflictsResolved"] = applicationResult.ResolvedConflicts.Length
                }.ToImmutableDictionary()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in fix session {SessionId}", sessionId);
            return new FixSessionResult
            {
                SessionId = sessionId,
                AnalyzerId = request.AnalyzerId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<ImmutableArray<AnalyzerHealthStatus>> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var analyzers = _registry.GetRegisteredAnalyzers();
            var healthStatuses = new List<AnalyzerHealthStatus>();

            foreach (var analyzerInfo in analyzers)
            {
                var analyzer = _registry.GetAnalyzer(analyzerInfo.Id);
                var isHealthy = analyzer != null && analyzer.IsEnabled;

                IAnalyzerSandbox? sandbox = null;
                lock (_lock)
                {
                    _sandboxes.TryGetValue(analyzerInfo.Id, out sandbox);
                }

                var sandboxHealthy = sandbox?.IsHealthy() ?? true;

                healthStatuses.Add(new AnalyzerHealthStatus
                {
                    AnalyzerId = analyzerInfo.Id,
                    IsHealthy = isHealthy && sandboxHealthy,
                    IsLoaded = analyzer != null,
                    IsEnabled = analyzer?.IsEnabled ?? false,
                    LastActivity = analyzerInfo.LoadedAt,
                    Uptime = DateTime.UtcNow - analyzerInfo.LoadedAt,
                    AnalysesRun = 0, // Would need to track this
                    ErrorsEncountered = 0, // Would need to track this
                    Metrics = new Dictionary<string, object>
                    {
                        ["SandboxHealthy"] = sandboxHealthy,
                        ["Version"] = analyzerInfo.Version.ToString(),
                        ["Author"] = analyzerInfo.Author
                    }.ToImmutableDictionary()
                });
            }

            return healthStatuses.ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting analyzer health status");
            return ImmutableArray<AnalyzerHealthStatus>.Empty;
        }
    }

    private async Task<IAnalyzer?> CreateAnalyzerInstanceAsync(AnalyzerInfo info, string assemblyPath, CancellationToken cancellationToken)
    {
        try
        {
            var assemblyContext = new AssemblyLoadContext(assemblyPath, isCollectible: true);
            var assembly = assemblyContext.LoadFromAssemblyPath(assemblyPath);

            var analyzerTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IAnalyzer).IsAssignableFrom(t))
                .ToList();

            foreach (var analyzerType in analyzerTypes)
            {
                try
                {
                    var constructor = analyzerType.GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                    {
                        var analyzer = (IAnalyzer)constructor.Invoke(Array.Empty<object>());
                        if (analyzer.Id == info.Id)
                        {
                            return analyzer;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error instantiating analyzer type: {AnalyzerType}", analyzerType.Name);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating analyzer instance: {AnalyzerId}", info.Id);
            return null;
        }
    }

    private async Task GenerateFixesAsync(AnalysisSessionResult sessionResult, CancellationToken cancellationToken)
    {
        try
        {
            // This would integrate with the fix engine to pre-generate fixes
            // For now, it's a placeholder
            _logger.LogInformation("Generating fixes for session {SessionId}", sessionResult.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating fixes for session {SessionId}", sessionResult.SessionId);
        }
    }

    public async Task<AnalyzerResult> RunAnalyzerAsync(string analyzerId, string targetPath, AnalyzerOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Running analyzer {AnalyzerId} on target {TargetPath}", analyzerId, targetPath);

            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return new AnalyzerResult
                {
                    Success = false,
                    AnalyzerId = analyzerId,
                    ErrorMessage = $"Analyzer not found: {analyzerId}"
                };
            }

            if (!analyzer.IsEnabled)
            {
                return new AnalyzerResult
                {
                    Success = false,
                    AnalyzerId = analyzerId,
                    ErrorMessage = $"Analyzer is disabled: {analyzerId}"
                };
            }

            if (!analyzer.CanAnalyze(targetPath))
            {
                return new AnalyzerResult
                {
                    Success = false,
                    AnalyzerId = analyzerId,
                    ErrorMessage = $"Analyzer cannot analyze target: {targetPath}"
                };
            }

            // Initialize analyzer if needed
            await analyzer.InitializeAsync(cancellationToken);

            // Determine if target is file or directory
            if (File.Exists(targetPath))
            {
                var content = await File.ReadAllTextAsync(targetPath, cancellationToken);
                var analysisResult = await analyzer.AnalyzeAsync(targetPath, content, cancellationToken);

                return new AnalyzerResult
                {
                    Success = true,
                    AnalyzerId = analyzerId,
                    Findings = analysisResult.Issues.Select(i => new Finding
                    {
                        Message = i.Description,
                        Severity = MapSeverity(i.Severity),
                        FilePath = i.FilePath,
                        LineNumber = i.LineNumber,
                        ColumnNumber = i.ColumnNumber
                    }).ToList()
                };
            }
            else if (Directory.Exists(targetPath))
            {
                // For directory analysis, find all supported files
                var supportedExtensions = analyzer.SupportedExtensions;
                var files = Directory.GetFiles(targetPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
                    .Take(100); // Limit to 100 files for now

                var allFindings = new List<Finding>();
                foreach (var file in files)
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(file, cancellationToken);
                        var analysisResult = await analyzer.AnalyzeAsync(file, content, cancellationToken);

                        allFindings.AddRange(analysisResult.Issues.Select(i => new Finding
                        {
                            Message = i.Description,
                            Severity = MapSeverity(i.Severity),
                            FilePath = i.FilePath,
                            LineNumber = i.LineNumber,
                            ColumnNumber = i.ColumnNumber
                        }));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error analyzing file: {FilePath}", file);
                    }
                }

                return new AnalyzerResult
                {
                    Success = true,
                    AnalyzerId = analyzerId,
                    Findings = allFindings
                };
            }
            else
            {
                return new AnalyzerResult
                {
                    Success = false,
                    AnalyzerId = analyzerId,
                    ErrorMessage = $"Target path does not exist: {targetPath}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running analyzer {AnalyzerId} on target {TargetPath}", analyzerId, targetPath);
            return new AnalyzerResult
            {
                Success = false,
                AnalyzerId = analyzerId,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<List<IAnalyzer>> GetLoadedAnalyzersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.FromResult(_registry.GetLoadedAnalyzers());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting loaded analyzers");
            return new List<IAnalyzer>();
        }
    }

    public async Task<AnalyzerCapabilities?> GetAnalyzerCapabilitiesAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return null;
            }

            return analyzer.GetCapabilities();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting capabilities for analyzer: {AnalyzerId}", analyzerId);
            return null;
        }
    }

    public async Task<AnalyzerUnloadResult> UnloadAnalyzerAsync(IAnalyzer analyzer, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Unloading analyzer: {AnalyzerId}", analyzer.Id);

            // Remove sandbox
            IAnalyzerSandbox? sandbox = null;
            lock (_lock)
            {
                if (_sandboxes.TryGetValue(analyzer.Id, out sandbox))
                {
                    _sandboxes.Remove(analyzer.Id);
                }
            }

            sandbox?.Dispose();

            // Unregister from registry
            var unregistered = await _registry.UnregisterAnalyzerAsync(analyzer.Id, cancellationToken);

            if (unregistered)
            {
                await _securityManager.LogSecurityEventAsync(new SecurityEvent
                {
                    AnalyzerId = analyzer.Id,
                    EventType = SecurityEventType.AssemblyValidation,
                    Operation = "UnloadAnalyzer",
                    Success = true,
                    Details = $"Analyzer {analyzer.Id} unloaded successfully"
                });

                return new AnalyzerUnloadResult
                {
                    Success = true,
                    AnalyzerId = analyzer.Id
                };
            }
            else
            {
                return new AnalyzerUnloadResult
                {
                    Success = false,
                    AnalyzerId = analyzer.Id,
                    ErrorMessage = "Failed to unregister analyzer"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unloading analyzer: {AnalyzerId}", analyzer.Id);
            return new AnalyzerUnloadResult
            {
                Success = false,
                AnalyzerId = analyzer.Id,
                ErrorMessage = ex.Message
            };
        }
    }

    private FindingSeverity MapSeverity(IssueSeverity analyzerSeverity)
    {
        return analyzerSeverity switch
        {
            IssueSeverity.Info => FindingSeverity.Info,
            IssueSeverity.Warning => FindingSeverity.Warning,
            IssueSeverity.Error => FindingSeverity.Error,
            IssueSeverity.Critical => FindingSeverity.Critical,
            _ => FindingSeverity.Info
        };
    }
}