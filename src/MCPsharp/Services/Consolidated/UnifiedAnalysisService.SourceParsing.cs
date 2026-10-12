namespace MCPsharp.Services.Consolidated;

public partial class UnifiedAnalysisService
{
    #region Helper Methods

    private static int CountMethodLines(string content, int startIndex)
    {
        var lines = content.Split('\n');
        var braceCount = 0;
        var methodLines = 0;
        var inMethod = false;

        for (int i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i];
            methodLines++;

            foreach (var c in line)
            {
                if (c == '{')
                {
                    braceCount++;
                    inMethod = true;
                }
                else if (c == '}')
                {
                    braceCount--;
                    if (braceCount == 0 && inMethod)
                    {
                        return methodLines;
                    }
                }
            }
        }

        return methodLines;
    }

    private static int CountClassLines(string content, int startIndex)
    {
        var lines = content.Split('\n');
        var braceCount = 0;
        var classLines = 0;
        var inClass = false;

        for (int i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i];
            classLines++;

            foreach (var c in line)
            {
                if (c == '{')
                {
                    braceCount++;
                    inClass = true;
                }
                else if (c == '}')
                {
                    braceCount--;
                    if (braceCount == 0 && inClass)
                    {
                        return classLines;
                    }
                }
            }
        }

        return classLines;
    }

    #endregion

    #region Additional Helper Methods

    private static string ExtractMethodContent(string content, int startIndex)
    {
        var braceCount = 0;
        var inMethod = false;
        var startIdx = startIndex;
        var endIdx = startIndex;

        for (int i = startIndex; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                if (!inMethod)
                {
                    inMethod = true;
                    startIdx = i;
                }
                braceCount++;
            }
            else if (content[i] == '}')
            {
                braceCount--;
                if (braceCount == 0 && inMethod)
                {
                    endIdx = i + 1;
                    break;
                }
            }
        }

        return content.Substring(startIdx, endIdx - startIdx);
    }

    private static int CalculateCyclomaticComplexity(string methodContent)
    {
        var complexity = 1; // Base complexity

        // Count decision points
        var decisionKeywords = new[] { "if", "else", "while", "for", "foreach", "switch", "case", "&&", "||", "?:" };
        foreach (var keyword in decisionKeywords)
        {
            complexity += System.Text.RegularExpressions.Regex.Count(methodContent, $@"\b{keyword}\b", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        }

        return complexity;
    }

    #endregion
}
