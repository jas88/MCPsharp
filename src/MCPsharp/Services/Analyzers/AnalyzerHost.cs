using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Implementation of analyzer host for loading and managing analyzers
/// </summary>
public partial class AnalyzerHost : IAnalyzerHost
{
    private readonly ILogger<AnalyzerHost> _logger;
    private readonly IAnalyzerRegistry _registry;
    private readonly ISecurityManager _securityManager;
    private readonly IFixEngine _fixEngine;
    private readonly IAnalyzerSandboxFactory _sandboxFactory;
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
        IFixEngine fixEngine,
        IAnalyzerSandboxFactory sandboxFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _securityManager = securityManager ?? throw new ArgumentNullException(nameof(securityManager));
        _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));
        _sandboxFactory = sandboxFactory ?? throw new ArgumentNullException(nameof(sandboxFactory));
        _sandboxes = new Dictionary<string, IAnalyzerSandbox>();

        // Subscribe to registry events
        _registry.AnalyzerRegistered += (sender, args) => AnalyzerLoaded?.Invoke(this, new AnalyzerLoadedEventArgs(args.AnalyzerId, args.AnalyzerInfo));
        _registry.AnalyzerUnregistered += (sender, args) => AnalyzerUnregistered?.Invoke(this, new AnalyzerUnregisteredEventArgs(args.AnalyzerId));
    }

    public async Task<AnalyzerLoadResult> LoadAnalyzerAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        try
        {
            LogLoadingAnalyzer(_logger, assemblyPath);

            if (string.IsNullOrEmpty(assemblyPath))
            {
                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = "Assembly path cannot be null or empty"
                };
            }

            if (!File.Exists(assemblyPath))
            {
                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = $"Assembly not found: {assemblyPath}"
                };
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Security validation
            var securityValidation = await _securityManager.ValidateAssemblyAsync(assemblyPath, cancellationToken);
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

            // Create sandbox for loading
            var loadSandbox = _sandboxFactory.CreateSandbox();

            // Load analyzer using sandbox
            var loadedAnalyzer = await loadSandbox.LoadAnalyzerAsync(assemblyPath, cancellationToken);
            if (loadedAnalyzer == null)
            {
                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = "No valid analyzers found in assembly",
                    SecurityValidation = securityValidation,
                    Warnings = compatibilityResult.Warnings
                };
            }

            // Register analyzer
            var registered = await _registry.RegisterAnalyzerAsync(loadedAnalyzer, cancellationToken);
            if (!registered)
            {
                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = "Failed to register analyzer",
                    SecurityValidation = securityValidation,
                    Warnings = compatibilityResult.Warnings
                };
            }

            // Store sandbox for the analyzer
            lock (_lock)
            {
                _sandboxes[loadedAnalyzer.Id] = loadSandbox;
            }

            // Set default permissions
            await _securityManager.SetPermissionsAsync(loadedAnalyzer.Id, new AnalyzerPermissions
            {
                CanReadFiles = true,
                CanWriteFiles = false,
                CanExecuteCommands = false,
                CanAccessNetwork = false,
                AllowedPaths = ImmutableArray.Create(Directory.GetCurrentDirectory())
            });

            return new AnalyzerLoadResult
            {
                Success = true,
                AnalyzerId = loadedAnalyzer.Id,
                Analyzer = loadedAnalyzer,
                SecurityValidation = securityValidation,
                Warnings = compatibilityResult.Warnings.Concat(securityValidation.Warnings).ToImmutableArray()
            };
        }
        catch (Exception ex)
        {
            LogErrorLoadingAnalyzer(_logger, ex, assemblyPath);
            return new AnalyzerLoadResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> UnloadAnalyzerAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(analyzerId))
        {
            return false;
        }

        try
        {
            LogUnloadingAnalyzer(_logger, analyzerId);

            // Check if analyzer exists
            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return false;
            }

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

                AnalyzerUnloaded?.Invoke(this, new AnalyzerUnloadedEventArgs(analyzerId));
            }

            return unregistered;
        }
        catch (Exception ex)
        {
            LogErrorUnloadingAnalyzer(_logger, ex, analyzerId);
            return false;
        }
    }

    public async Task<AnalyzerUnloadResult> UnloadAnalyzerAsync(IAnalyzer analyzer, CancellationToken cancellationToken = default)
    {
        try
        {
            LogUnloadingAnalyzer(_logger, analyzer.Id);

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

                AnalyzerUnloaded?.Invoke(this, new AnalyzerUnloadedEventArgs(analyzer.Id));

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
            LogErrorUnloadingAnalyzer(_logger, ex, analyzer.Id);
            return new AnalyzerUnloadResult
            {
                Success = false,
                AnalyzerId = analyzer.Id,
                ErrorMessage = ex.Message
            };
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
            LogErrorSettingEnabledState(_logger, ex, analyzerId);
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
            LogErrorConfiguringAnalyzer(_logger, ex, analyzerId);
            return false;
        }
    }

    public async Task<AnalysisSessionResult> RunAnalysisAsync(AnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            LogStartingAnalysisSession(_logger, sessionId, request.AnalyzerId);

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
                    sandbox = _sandboxFactory.CreateSandbox();
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
                        LogFileNotFoundForAnalysis(_logger, filePath);
                        continue;
                    }

                    // Check read permission
                    var canRead = await _securityManager.IsOperationAllowedAsync(request.AnalyzerId, "ReadFile", filePath);
                    if (!canRead)
                    {
                        LogReadNotAllowed(_logger, request.AnalyzerId, filePath);
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
                    LogErrorAnalyzingFile(_logger, ex, filePath);
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
            if (request.GenerateFixes && allIssues.Count > 0)
            {
                GenerateFixes(sessionResult);
            }

            AnalysisCompleted?.Invoke(this, new AnalysisCompletedEventArgs(sessionId, sessionResult));

            return sessionResult;
        }
        catch (Exception ex)
        {
            LogErrorInAnalysisSession(_logger, ex, sessionId);
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

    public Task<ImmutableArray<AnalyzerFix>> GetFixesAsync(string analyzerId, ImmutableArray<string> issueIds, CancellationToken cancellationToken = default)
    {
        try
        {
            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return Task.FromResult(ImmutableArray<AnalyzerFix>.Empty);
            }

            var fixes = new List<AnalyzerFix>();

            foreach (var issueId in issueIds)
            {
                // This is a simplified approach - in practice, we'd need to map issue IDs to rule IDs
                // For now, return all available fixes
                var analyzerFixes = analyzer.GetFixes("ALL");
                fixes.AddRange(analyzerFixes);
            }

            return Task.FromResult(fixes.DistinctBy(f => f.Id).ToImmutableArray());
        }
        catch (Exception ex)
        {
            LogErrorGettingFixes(_logger, ex, analyzerId);
            return Task.FromResult(ImmutableArray<AnalyzerFix>.Empty);
        }
    }

    public async Task<FixSessionResult> ApplyFixesAsync(ApplyFixRequest request, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            LogStartingFixSession(_logger, sessionId, request.AnalyzerId);

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
            LogErrorInFixSession(_logger, ex, sessionId);
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

    public Task<ImmutableArray<AnalyzerHealthStatus>> GetHealthStatusAsync(CancellationToken cancellationToken = default)
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

            return Task.FromResult(healthStatuses.ToImmutableArray());
        }
        catch (Exception ex)
        {
            LogErrorGettingHealthStatus(_logger, ex);
            return Task.FromResult(ImmutableArray<AnalyzerHealthStatus>.Empty);
        }
    }

    private void GenerateFixes(AnalysisSessionResult sessionResult)
    {
        // This would integrate with the fix engine to pre-generate fixes.
        // Fixes are currently produced on demand via GetFixesAsync/ApplyFixesAsync.
        LogGeneratingFixes(_logger, sessionResult.SessionId);
    }

    public async Task<AnalyzerResult> RunAnalyzerAsync(string analyzerId, string targetPath, AnalyzerOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            LogRunningAnalyzer(_logger, analyzerId, targetPath);

            // Check cancellation early
            cancellationToken.ThrowIfCancellationRequested();

            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return new AnalyzerResult
                {
                    Success = false,
                    AnalyzerId = analyzerId,
                    ErrorMessage = $"Analyzer not loaded: {analyzerId}"
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

            // Check cancellation after async operations
            cancellationToken.ThrowIfCancellationRequested();

            // Determine if target is file or directory
            if (File.Exists(targetPath))
            {
                var content = await File.ReadAllTextAsync(targetPath, cancellationToken);

                // Check cancellation before analysis
                cancellationToken.ThrowIfCancellationRequested();

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
                var hasAnalysisErrors = false;
                string? analysisErrorMessage = null;

                foreach (var file in files)
                {
                    try
                    {
                        // Check cancellation for each file
                        cancellationToken.ThrowIfCancellationRequested();

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
                    catch (OperationCanceledException)
                    {
                        LogAnalysisCancelledDuringFile(_logger, file);
                        throw; // Re-throw cancellation
                    }
                    catch (Exception ex)
                    {
                        LogErrorAnalyzingFile(_logger, ex, file);
                        hasAnalysisErrors = true;
                        analysisErrorMessage = ex.Message;
                        // For tests, we want to fail if analysis fails, not just log and continue
                        // In production, you might want to continue with other files
                        return new AnalyzerResult
                        {
                            Success = false,
                            AnalyzerId = analyzerId,
                            ErrorMessage = $"Analysis failed for file {file}: {ex.Message}",
                            Findings = allFindings
                        };
                    }
                }

                return new AnalyzerResult
                {
                    Success = !hasAnalysisErrors,
                    AnalyzerId = analyzerId,
                    Findings = allFindings,
                    ErrorMessage = hasAnalysisErrors ? analysisErrorMessage : null
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
        catch (OperationCanceledException)
        {
            LogAnalyzerExecutionCancelled(_logger, analyzerId);
            throw; // Re-throw cancellation exceptions
        }
        catch (Exception ex)
        {
            LogErrorRunningAnalyzer(_logger, ex, analyzerId, targetPath);
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
            LogErrorGettingLoadedAnalyzers(_logger, ex);
            return new List<IAnalyzer>();
        }
    }

    public Task<AnalyzerCapabilities?> GetAnalyzerCapabilitiesAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return Task.FromResult<AnalyzerCapabilities?>(null);
            }

            return Task.FromResult<AnalyzerCapabilities?>(analyzer.GetCapabilities());
        }
        catch (Exception ex)
        {
            LogErrorGettingCapabilities(_logger, ex, analyzerId);
            return Task.FromResult<AnalyzerCapabilities?>(null);
        }
    }

    private static FindingSeverity MapSeverity(IssueSeverity analyzerSeverity)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading analyzer from assembly: {AssemblyPath}")]
    private static partial void LogLoadingAnalyzer(ILogger logger, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading analyzer assembly: {AssemblyPath}")]
    private static partial void LogErrorLoadingAnalyzer(ILogger logger, Exception exception, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unloading analyzer: {AnalyzerId}")]
    private static partial void LogUnloadingAnalyzer(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error unloading analyzer: {AnalyzerId}")]
    private static partial void LogErrorUnloadingAnalyzer(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error setting analyzer enabled state: {AnalyzerId}")]
    private static partial void LogErrorSettingEnabledState(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error configuring analyzer: {AnalyzerId}")]
    private static partial void LogErrorConfiguringAnalyzer(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting analysis session {SessionId} with analyzer {AnalyzerId}")]
    private static partial void LogStartingAnalysisSession(ILogger logger, string sessionId, string analyzerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File not found for analysis: {FilePath}")]
    private static partial void LogFileNotFoundForAnalysis(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analyzer {AnalyzerId} not allowed to read file: {FilePath}")]
    private static partial void LogReadNotAllowed(ILogger logger, string analyzerId, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing file: {FilePath}")]
    private static partial void LogErrorAnalyzingFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error in analysis session {SessionId}")]
    private static partial void LogErrorInAnalysisSession(ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting fixes for analyzer: {AnalyzerId}")]
    private static partial void LogErrorGettingFixes(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting fix session {SessionId} with analyzer {AnalyzerId}")]
    private static partial void LogStartingFixSession(ILogger logger, string sessionId, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error in fix session {SessionId}")]
    private static partial void LogErrorInFixSession(ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting analyzer health status")]
    private static partial void LogErrorGettingHealthStatus(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generating fixes for session {SessionId}")]
    private static partial void LogGeneratingFixes(ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running analyzer {AnalyzerId} on target {TargetPath}")]
    private static partial void LogRunningAnalyzer(ILogger logger, string analyzerId, string targetPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis cancelled during file processing: {FilePath}")]
    private static partial void LogAnalysisCancelledDuringFile(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analyzer execution was cancelled: {AnalyzerId}")]
    private static partial void LogAnalyzerExecutionCancelled(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error running analyzer {AnalyzerId} on target {TargetPath}")]
    private static partial void LogErrorRunningAnalyzer(ILogger logger, Exception exception, string analyzerId, string targetPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting loaded analyzers")]
    private static partial void LogErrorGettingLoadedAnalyzers(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting capabilities for analyzer: {AnalyzerId}")]
    private static partial void LogErrorGettingCapabilities(ILogger logger, Exception exception, string analyzerId);
}
