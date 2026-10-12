using System.Globalization;
using System.Text.RegularExpressions;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Services;

public sealed partial class BulkEditService
{
    internal static bool CheckCondition(string content, BulkEditCondition condition, string filePath)
    {
        var result = condition.ConditionType switch
        {
            BulkConditionType.FileContains => content.Contains(condition.Pattern, StringComparison.Ordinal),
            BulkConditionType.FileMatches => Regex.IsMatch(content, condition.Pattern, RegexOptions.None, BulkEditRegexTimeout),
            BulkConditionType.FileSize => CheckFileSizeCondition(filePath, condition),
            BulkConditionType.FileModifiedAfter => CheckFileModifiedCondition(filePath, condition),
            BulkConditionType.FileExtension => CheckFileExtensionCondition(filePath, condition),
            BulkConditionType.FileInDirectory => CheckFileInDirectoryCondition(filePath, condition),
            _ => false
        };

        return condition.Negate ? !result : result;
    }

    private static bool CheckFileSizeCondition(string filePath, BulkEditCondition condition)
    {
        try
        {
            var fileInfo = new System.IO.FileInfo(filePath);
            if (condition.Parameters?.TryGetValue("minSize", out var minSizeObj) == true &&
                long.TryParse(minSizeObj.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minSize) &&
                fileInfo.Length < minSize)
            {
                return false;
            }

            if (condition.Parameters?.TryGetValue("maxSize", out var maxSizeObj) == true &&
                long.TryParse(maxSizeObj.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxSize) &&
                fileInfo.Length > maxSize)
            {
                return false;
            }

            return true;
        }
        catch (IOException)
        {
            // File missing or inaccessible: condition cannot be met
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool CheckFileModifiedCondition(string filePath, BulkEditCondition condition)
    {
        try
        {
            var fileInfo = new System.IO.FileInfo(filePath);
            if (condition.Parameters?.TryGetValue("date", out var dateObj) == true &&
                DateTime.TryParse(dateObj.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return fileInfo.LastWriteTime > date;
            }
            return false;
        }
        catch (IOException)
        {
            // File missing or inaccessible: condition cannot be met
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool CheckFileExtensionCondition(string filePath, BulkEditCondition condition)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(condition.Pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CheckFileInDirectoryCondition(string filePath, BulkEditCondition condition)
    {
        var directory = Path.GetDirectoryName(filePath);
        return !string.IsNullOrEmpty(directory) &&
               directory.Contains(condition.Pattern, StringComparison.OrdinalIgnoreCase);
    }
}
