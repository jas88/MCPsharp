using System.Collections.Immutable;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers.Fixes;

public partial class FixEngine
{
    public Task<ConflictDetectionResult> DetectConflictsAsync(ImmutableArray<AnalyzerFix> fixes, CancellationToken cancellationToken = default)
    {
        try
        {
            var conflicts = new List<FixConflict>();

            // Group fixes by file
            var fixesByFile = fixes.GroupBy(f => f.Edits.FirstOrDefault()?.FilePath ?? string.Empty)
                                  .Where(g => !string.IsNullOrEmpty(g.Key))
                                  .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var (filePath, fileFixes) in fixesByFile)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Convert AnalyzerFix objects to FixPreview objects
                var fixPreviews = fileFixes.Select(ConvertToFixPreview).ToList();
                var fileConflicts = DetectFileConflicts(filePath, fixPreviews);
                conflicts.AddRange(fileConflicts);
            }

            return Task.FromResult(new ConflictDetectionResult
            {
                Success = true,
                Conflicts = conflicts.ToImmutableArray(),
                DetectedAt = DateTime.UtcNow
            });
        }
        catch (OperationCanceledException)
        {
            return Task.FromCanceled<ConflictDetectionResult>(cancellationToken);
        }
        catch (Exception ex)
        {
            LogErrorDetectingConflicts(_logger, ex);
            return Task.FromResult(new ConflictDetectionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                DetectedAt = DateTime.UtcNow
            });
        }
    }

    public Task<ConflictResolutionResult> ResolveConflictsAsync(
        ImmutableArray<FixConflict> conflicts,
        ConflictResolutionStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolutions = new List<ConflictResolution>();
            var unresolvedConflicts = new List<FixConflict>();

            foreach (var conflict in conflicts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var resolution = ResolveSingleConflict(conflict, strategy);
                    if (resolution != null)
                    {
                        resolutions.Add(resolution);
                    }
                    else
                    {
                        unresolvedConflicts.Add(conflict);
                    }
                }
                catch (Exception ex)
                {
                    LogErrorResolvingConflict(_logger, ex, conflict.Id);
                    unresolvedConflicts.Add(conflict);
                }
            }

            return Task.FromResult(new ConflictResolutionResult
            {
                Success = unresolvedConflicts.Count == 0,
                Resolutions = resolutions.ToImmutableArray(),
                UnresolvedConflicts = unresolvedConflicts.ToImmutableArray(),
                ResolvedAt = DateTime.UtcNow
            });
        }
        catch (OperationCanceledException)
        {
            return Task.FromCanceled<ConflictResolutionResult>(cancellationToken);
        }
        catch (Exception ex)
        {
            LogErrorResolvingConflicts(_logger, ex);
            return Task.FromResult(new ConflictResolutionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ResolvedAt = DateTime.UtcNow
            });
        }
    }

    private static List<FixConflict> DetectFixConflicts(
        List<FixPreview> previews,
        CancellationToken cancellationToken)
    {
        var conflicts = new List<FixConflict>();

        // Group previews by file
        var previewsByFile = previews.GroupBy(p => p.FilePath).ToList();

        foreach (var fileGroup in previewsByFile)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileConflicts = DetectFileConflicts(fileGroup.Key, fileGroup.ToList());
            conflicts.AddRange(fileConflicts);
        }

        return conflicts;
    }

    private static List<FixConflict> DetectFileConflicts(
        string filePath,
        List<FixPreview> previews)
    {
        var conflicts = new List<FixConflict>();

        // Check for overlapping edits
        var allEdits = previews.SelectMany(p => p.Edits.Select(e => (Edit: e, FixId: p.FixId))).ToList();

        for (int i = 0; i < allEdits.Count; i++)
        {
            for (int j = i + 1; j < allEdits.Count; j++)
            {
                var edit1 = allEdits[i];
                var edit2 = allEdits[j];

                if (EditsOverlap(edit1.Edit, edit2.Edit))
                {
                    conflicts.Add(new FixConflict
                    {
                        FilePath = filePath,
                        ConflictingFixIds = ImmutableArray.Create(edit1.FixId, edit2.FixId),
                        ConflictType = ConflictType.OverlappingEdits,
                        Description = $"Edits from fixes {edit1.FixId} and {edit2.FixId} overlap in {filePath}",
                        ConflictingEdits = ImmutableArray.Create(edit1.Edit, edit2.Edit)
                    });
                }
            }
        }

        return conflicts;
    }

    private static bool EditsOverlap(TextEdit edit1, TextEdit edit2)
    {
        var pos1 = (edit1.StartLine, edit1.StartColumn);
        var pos2 = (edit2.StartLine, edit2.StartColumn);
        var end1 = (edit1.EndLine, edit1.EndColumn);
        var end2 = (edit2.EndLine, edit2.EndColumn);

        return !(end1.Item1 < pos2.Item1 || (end1.Item1 == pos2.Item1 && end1.Item2 < pos2.Item2) ||
                 end2.Item1 < pos1.Item1 || (end2.Item1 == pos1.Item1 && end2.Item2 < pos1.Item2));
    }

    private static ConflictResolution? ResolveSingleConflict(
        FixConflict conflict,
        ConflictResolutionStrategy strategy)
    {
        return strategy switch
        {
            ConflictResolutionStrategy.PreferOlder => new ConflictResolution
            {
                ConflictId = conflict.Id,
                Strategy = strategy,
                PreferredFixId = conflict.ConflictingFixIds.First(),
                Rationale = "Preferred older fix"
            },
            ConflictResolutionStrategy.PreferNewer => new ConflictResolution
            {
                ConflictId = conflict.Id,
                Strategy = strategy,
                PreferredFixId = conflict.ConflictingFixIds.Last(),
                Rationale = "Preferred newer fix"
            },
            ConflictResolutionStrategy.SkipAll => new ConflictResolution
            {
                ConflictId = conflict.Id,
                Strategy = strategy,
                Rationale = "Skipping all conflicting fixes"
            },
            _ => null
        };
    }
}
