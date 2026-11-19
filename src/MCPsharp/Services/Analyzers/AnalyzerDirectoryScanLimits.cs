using System.Globalization;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Limits on a directory searched for analyzer assemblies (load_roslyn_analyzers on a directory, and the
/// auto-load's customAnalyzerPaths). Every DLL found is loaded to look for analyzers, so a directory such as the
/// NuGet cache, with thousands of DLLs, would exhaust memory and file handles.
/// </summary>
public static class AnalyzerDirectoryScanLimits
{
    /// <summary>
    /// The most DLLs a directory searched for analyzers may hold; an analyzer package has a handful
    /// </summary>
    public const int MaxAssemblyCount = 100;

    /// <summary>
    /// The most bytes of DLLs a directory searched for analyzers may hold (100 MB)
    /// </summary>
    public const long MaxTotalBytes = 100L * 1024 * 1024;

    private const string Suggestion =
        "Point at a specific package's analyzer folder (e.g. ~/.nuget/packages/<package>/<version>/analyzers/dotnet/cs), " +
        "a project's analyzer output folder, or a single analyzer DLL; the analyzers the open project references are loaded automatically.";

    /// <summary>
    /// Check that <paramref name="directory"/> holds few and small enough DLLs to be searched for analyzers. Counting
    /// stops at the first DLL over a limit, so even a huge directory is rejected quickly.
    /// </summary>
    /// <returns>Null when the directory can be searched (or can't be read, which the search reports), otherwise an
    /// error naming the limit it exceeds</returns>
    public static string? Check(
        string directory,
        bool recursive = true,
        int maxAssemblyCount = MaxAssemblyCount,
        long maxTotalBytes = MaxTotalBytes)
    {
        var count = 0;
        long totalBytes = 0;
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.dll", searchOption))
            {
                count++;
                if (count > maxAssemblyCount)
                {
                    return $"'{directory}' holds more than {maxAssemblyCount} DLLs, the most a directory searched for analyzers " +
                           $"may hold, since every DLL found is loaded to look for analyzers. {Suggestion}";
                }

                totalBytes += new FileInfo(file).Length;
                if (totalBytes > maxTotalBytes)
                {
                    return $"'{directory}' holds more than {FormatSize(maxTotalBytes)} of DLLs, the most a directory searched for " +
                           $"analyzers may hold, since every DLL found is loaded to look for analyzers. {Suggestion}";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable directory is reported by the search itself
        }

        return null;
    }

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB");
}
