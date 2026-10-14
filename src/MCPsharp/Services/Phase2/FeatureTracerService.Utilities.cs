using System.Text.RegularExpressions;
using MCPsharp.Models.Roslyn;
using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Phase2;

public partial class FeatureTracerService
{
    /// <summary>
    /// Normalize feature name for searching
    /// </summary>
    private static string NormalizeFeatureName(string featureName)
    {
        return featureName.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
    }

    /// <summary>
    /// Convert string to PascalCase
    /// </summary>
    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var words = input.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Select(word =>
            char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant()));
    }

    /// <summary>
    /// Parse entry point string into file path and symbol name
    /// </summary>
    private static (string? filePath, string? symbolName) ParseEntryPoint(string entryPoint)
    {
        if (entryPoint.Contains("::"))
        {
            var parts = entryPoint.Split("::", StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 ? (parts[0], parts[1]) : (null, entryPoint);
        }

        return (null, entryPoint);
    }

    /// <summary>
    /// Extract feature name from symbol and file path
    /// </summary>
    private static string ExtractFeatureNameFromSymbol(SymbolResult symbol, string? filePath)
    {
        // Try to extract from symbol name first
        var symbolName = symbol.Name;
        if (symbolName.EndsWith("Controller", StringComparison.Ordinal))
        {
            return symbolName.Replace("Controller", "").ToLowerInvariant();
        }
        if (symbolName.EndsWith("Service", StringComparison.Ordinal))
        {
            return symbolName.Replace("Service", "").ToLowerInvariant();
        }

        // Fall back to file path analysis
        if (!string.IsNullOrEmpty(filePath))
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            return fileName.ToLowerInvariant();
        }

        return symbolName.ToLowerInvariant();
    }

    /// <summary>
    /// Extract class name from C# content
    /// </summary>
    private static string ExtractClassName(string content)
    {
        var match = ClassNameRegex().Match(content);
        return match.Success ? match.Groups[1].Value : "";
    }

    /// <summary>
    /// Categorize component by class name
    /// </summary>
    private static void CategorizeByClassName(string className, string filePath, MutableFeatureComponents components)
    {
        if (className.EndsWith("Controller", StringComparison.Ordinal))
        {
            components.Controller = $"{filePath}::{className}";
        }
        else if (className.EndsWith("Service", StringComparison.Ordinal))
        {
            components.Service = $"{filePath}::{className}";
        }
        else if (className.EndsWith("Repository", StringComparison.Ordinal))
        {
            components.Repository = $"{filePath}::{className}";
        }
        else
        {
            components.Models.Add(filePath);
        }
    }

    /// <summary>
    /// Categorize component by symbol name
    /// </summary>
    private static void CategorizeBySymbolName(string symbolName, string filePath, MutableFeatureComponents components, List<string> dataFlow)
    {
        if (symbolName.EndsWith("Controller", StringComparison.Ordinal))
        {
            components.Controller = $"{filePath}::{symbolName}";
            if (!dataFlow.Contains("Controller"))
                dataFlow.Add("Controller");
        }
        else if (symbolName.EndsWith("Service", StringComparison.Ordinal))
        {
            components.Service = $"{filePath}::{symbolName}";
            if (!dataFlow.Contains("Service"))
                dataFlow.Add("Service");
        }
        else if (symbolName.EndsWith("Repository", StringComparison.Ordinal))
        {
            components.Repository = $"{filePath}::{symbolName}";
            if (!dataFlow.Contains("Repository"))
                dataFlow.Add("Repository");
        }
    }

    /// <summary>
    /// Count total components in feature map
    /// </summary>
    private static int GetComponentCount(MutableFeatureComponents components)
    {
        var count = 0;
        if (components.Controller != null)
            count++;
        if (components.Service != null)
            count++;
        if (components.Repository != null)
            count++;
        if (components.Models != null)
            count += components.Models.Count;
        if (components.Migrations != null)
            count += components.Migrations.Count;
        if (components.Config != null)
            count += components.Config.Count;
        if (components.Tests != null)
            count += components.Tests.Count;
        if (components.Documentation != null)
            count++;
        if (components.Workflows != null)
            count += components.Workflows.Count;

        return count;
    }

    [GeneratedRegex(@"class\s+(\w+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ClassNameRegex();
}
