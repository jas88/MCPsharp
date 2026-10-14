namespace MCPsharp.Services.Streaming;

/// <summary>
/// Line handling shared by the text stream processors. Chunks use text-file semantics: "\r\n", "\n"
/// and "\r" each end a line, and a trailing line break terminates the last line rather than starting
/// an empty one. Processed output keeps that trailing line break, so the outputs of consecutive
/// chunks concatenate into whole lines.
/// </summary>
internal static class TextChunkLines
{
    private static readonly string[] LineSeparators = { "\r\n", "\n", "\r" };

    /// <summary>
    /// Splits chunk text into lines; empty lines are kept.
    /// </summary>
    public static List<string> Split(string text, out bool endsWithLineBreak)
    {
        endsWithLineBreak = text.Length > 0 && text[^1] is '\n' or '\r';
        if (text.Length == 0)
            return new List<string>();

        var lines = text.Split(LineSeparators, StringSplitOptions.None).ToList();
        if (endsWithLineBreak)
            lines.RemoveAt(lines.Count - 1);

        return lines;
    }

    /// <summary>
    /// Joins processed lines with <see cref="Environment.NewLine"/>, ending with a line break when the
    /// input chunk did and at least one line remains.
    /// </summary>
    public static string Join(IReadOnlyCollection<string> lines, bool endWithLineBreak)
    {
        var text = string.Join(Environment.NewLine, lines);
        return endWithLineBreak && lines.Count > 0 ? text + Environment.NewLine : text;
    }

    /// <summary>
    /// Counts the line breaks in encoded text the way <see cref="Split"/> separates lines ("\r\n"
    /// counts once). Works on the raw bytes, so it assumes an ASCII-compatible encoding such as UTF-8.
    /// </summary>
    public static long CountLineBreaks(ReadOnlySpan<byte> data)
    {
        long count = 0;
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == '\n' || (data[i] == '\r' && (i + 1 == data.Length || data[i + 1] != '\n')))
                count++;
        }

        return count;
    }
}
