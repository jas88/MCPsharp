using Microsoft.CodeAnalysis;
using MCPsharp.Models;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Duplicate hotspot analysis
/// </summary>
public partial class DuplicateCodeDetectorService
{
    private static DuplicateHotspotsResult AnalyzeHotspots(
        List<DuplicateGroup> duplicateGroups,
        List<CodeBlock> allCodeBlocks)
    {
        // File hotspots
        var fileDuplicationScores = new Dictionary<string, double>();
        var fileDuplicateCounts = new Dictionary<string, int>();
        var fileLineCounts = new Dictionary<string, int>();

        foreach (var group in duplicateGroups)
        {
            foreach (var block in group.CodeBlocks)
            {
                if (!fileDuplicationScores.ContainsKey(block.FilePath))
                {
                    fileDuplicationScores[block.FilePath] = 0;
                    fileDuplicateCounts[block.FilePath] = 0;
                    fileLineCounts[block.FilePath] = 0;
                }

                fileDuplicationScores[block.FilePath] += group.SimilarityScore * group.Complexity;
                fileDuplicateCounts[block.FilePath]++;
                fileLineCounts[block.FilePath] += block.EndLine - block.StartLine + 1;
            }
        }

        var fileHotspots = fileDuplicationScores
            .Select(kvp => new FileHotspot
            {
                FilePath = kvp.Key,
                DuplicationScore = kvp.Value,
                DuplicateGroupCount = fileDuplicateCounts[kvp.Key],
                DuplicationPercentage = allCodeBlocks.Any(cb => cb.FilePath == kvp.Key)
                    ? (double)fileLineCounts[kvp.Key] / allCodeBlocks.Count(cb => cb.FilePath == kvp.Key) * 100
                    : 0.0,
                RiskLevel = CalculateHotspotRiskLevel(kvp.Value, fileDuplicateCounts[kvp.Key])
            })
            .OrderByDescending(f => f.DuplicationScore)
            .Take(10)
            .ToList();

        // Class hotspots
        var classDuplicationScores = new Dictionary<string, (double Score, int Count, string FilePath)>();

        foreach (var group in duplicateGroups)
        {
            foreach (var block in group.CodeBlocks.Where(b => !string.IsNullOrEmpty(b.ContainingType)))
            {
                var key = $"{block.ContainingType}|{block.FilePath}";
                if (!classDuplicationScores.TryGetValue(key, out var current))
                {
                    current = (0, 0, block.FilePath);
                    classDuplicationScores[key] = current;
                }

                classDuplicationScores[key] = (
                    current.Score + group.SimilarityScore * group.Complexity,
                    current.Count + 1,
                    block.FilePath
                );
            }
        }

        var classHotspots = classDuplicationScores
            .Select(kvp => new ClassHotspot
            {
                ClassName = kvp.Key.Split('|')[0],
                FilePath = kvp.Value.FilePath,
                DuplicationScore = kvp.Value.Score,
                DuplicateGroupCount = kvp.Value.Count,
                RiskLevel = CalculateHotspotRiskLevel(kvp.Value.Score, kvp.Value.Count)
            })
            .OrderByDescending(c => c.DuplicationScore)
            .Take(10)
            .ToList();

        // Method hotspots
        var methodDuplicationScores = new Dictionary<string, (double Score, int Count, string ClassName, string FilePath)>();

        foreach (var group in duplicateGroups)
        {
            foreach (var block in group.CodeBlocks)
            {
                var key = $"{block.ElementName}|{block.ContainingType}|{block.FilePath}";
                if (!methodDuplicationScores.TryGetValue(key, out var current))
                {
                    current = (0, 0, block.ContainingType ?? "", block.FilePath);
                    methodDuplicationScores[key] = current;
                }

                methodDuplicationScores[key] = (
                    current.Score + group.SimilarityScore * group.Complexity,
                    current.Count + 1,
                    block.ContainingType ?? "",
                    block.FilePath
                );
            }
        }

        var methodHotspots = methodDuplicationScores
            .Select(kvp => new MethodHotspot
            {
                MethodName = kvp.Key.Split('|')[0],
                ClassName = kvp.Value.ClassName,
                FilePath = kvp.Value.FilePath,
                DuplicationScore = kvp.Value.Score,
                DuplicateGroupCount = kvp.Value.Count,
                RiskLevel = CalculateHotspotRiskLevel(kvp.Value.Score, kvp.Value.Count)
            })
            .OrderByDescending(m => m.DuplicationScore)
            .Take(10)
            .ToList();

        // Directory hotspots
        var directoryDuplicationScores = new Dictionary<string, (int Files, int Lines)>();

        foreach (var group in duplicateGroups)
        {
            foreach (var block in group.CodeBlocks)
            {
                var directory = Path.GetDirectoryName(block.FilePath) ?? "";
                if (!directoryDuplicationScores.TryGetValue(directory, out var current))
                {
                    current = (0, 0);
                    directoryDuplicationScores[directory] = current;
                }

                var lines = block.EndLine - block.StartLine + 1;
                directoryDuplicationScores[directory] = (
                    current.Files + 1,
                    current.Lines + lines
                );
            }
        }

        var directoryHotspots = directoryDuplicationScores
            .Select(kvp => new DirectoryHotspot
            {
                DirectoryPath = kvp.Key,
                DuplicationScore = kvp.Value.Lines * 0.1, // Simplified score
                FilesWithDuplicates = kvp.Value.Files,
                TotalDuplicateLines = kvp.Value.Lines,
                RiskLevel = CalculateHotspotRiskLevel(kvp.Value.Lines * 0.1, kvp.Value.Files)
            })
            .OrderByDescending(d => d.DuplicationScore)
            .Take(10)
            .ToList();

        return new DuplicateHotspotsResult
        {
            FileHotspots = fileHotspots,
            ClassHotspots = classHotspots,
            MethodHotspots = methodHotspots,
            DirectoryHotspots = directoryHotspots
        };
    }

    private static HotspotRiskLevel CalculateHotspotRiskLevel(double score, int count)
    {
        if (score >= 100 || count >= 10)
            return HotspotRiskLevel.Critical;

        if (score >= 50 || count >= 5)
            return HotspotRiskLevel.High;

        if (score >= 20 || count >= 3)
            return HotspotRiskLevel.Medium;

        return HotspotRiskLevel.Low;
    }
}
