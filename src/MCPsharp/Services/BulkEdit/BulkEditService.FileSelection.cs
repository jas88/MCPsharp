using MCPsharp.Models.BulkEdit;
using Microsoft.Extensions.FileSystemGlobbing;

namespace MCPsharp.Services;

/// <summary>
/// Selection of the files a bulk replace operates on: glob expansion and exclusion patterns.
/// </summary>
public sealed partial class BulkEditService
{
    private static readonly char[] FileGlobCharacters = { '*', '?', '[', '{' };
    private static readonly char[] FilePathSeparators = { '/', '\\' };

    /// <summary>
    /// Expand the glob entries of <paramref name="files"/> (explicit paths are kept as given, so missing
    /// files are still reported) and then drop every file matched by <see cref="BulkEditOptions.ExcludedFiles"/>.
    /// </summary>
    private async Task<List<string>> SelectBulkReplaceFilesAsync(
        IReadOnlyList<string> files,
        BulkEditOptions options,
        CancellationToken cancellationToken)
    {
        // Exclusions are applied afterwards by CreateExclusionFilter, not by the expansion's matcher
        var expansionOptions = new BulkEditOptions
        {
            IncludeHiddenFiles = options.IncludeHiddenFiles,
            MaxFileSize = options.MaxFileSize
        };
        var selected = new List<string>();
        var seen = new HashSet<string>(StringComparer.FromComparison(PathContainment.Comparison));

        foreach (var entry in files)
        {
            var matches = entry.IndexOfAny(FileGlobCharacters) < 0
                ? new[] { entry }
                : (await ResolveFilePatterns(new[] { entry }, expansionOptions, cancellationToken))
                    .Order(StringComparer.Ordinal)
                    .ToArray();

            selected.AddRange(matches.Where(seen.Add));
        }

        if (options.ExcludedFiles.Count == 0)
        {
            return selected;
        }

        var exclusions = options.ExcludedFiles
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(CreateExclusionFilter)
            .ToList();
        return selected.Where(file => !exclusions.Exists(isExcluded => isExcluded(Path.GetFullPath(file)))).ToList();
    }

    /// <summary>
    /// Build a predicate for one exclusion: a plain path excludes that file or everything beneath that
    /// directory; a glob is matched relative to its fixed leading directory. Relative exclusions are
    /// resolved against the current directory, like relative file paths.
    /// </summary>
    internal static Func<string, bool> CreateExclusionFilter(string pattern)
    {
        var globIndex = pattern.IndexOfAny(FileGlobCharacters);
        if (globIndex < 0)
        {
            var excludedPath = Path.GetFullPath(pattern);
            return file => PathContainment.IsWithin(excludedPath, file);
        }

        var absolute = Path.IsPathRooted(pattern) ? pattern : Path.Combine(Directory.GetCurrentDirectory(), pattern);
        var separatorIndex = absolute.LastIndexOfAny(FilePathSeparators, absolute.IndexOfAny(FileGlobCharacters));
        var baseDirectory = Path.GetFullPath(separatorIndex <= 0 ? absolute[..(separatorIndex + 1)] : absolute[..separatorIndex]);
        var relativeGlob = absolute[(separatorIndex + 1)..];

        var matcher = new Matcher(PathContainment.Comparison);
        matcher.AddInclude(relativeGlob);

        return file =>
        {
            var relative = Path.GetRelativePath(baseDirectory, file);
            return !Path.IsPathRooted(relative) &&
                   !relative.Split(FilePathSeparators).Contains("..") &&
                   matcher.Match(relative).HasMatches;
        };
    }
}
