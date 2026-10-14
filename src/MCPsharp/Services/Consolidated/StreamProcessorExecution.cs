using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Runs a consolidated processor body, turning processing failures into
/// <c>Success = false</c> results rather than letting them escape or be reported as success.
/// </summary>
internal static class StreamProcessorExecution
{
    public static async Task<ProcessingResult> RunAsync(string processorName, Func<Task<ProcessingResult>> body)
    {
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsProcessingFailure(ex))
        {
            return Failure($"The '{processorName}' processor failed: {ex.Message}");
        }
    }

    public static ProcessingResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };

    private static bool IsProcessingFailure(Exception ex) => ex is ArgumentException
        or InvalidOperationException
        or InvalidCastException
        or FormatException
        or NotSupportedException
        or IOException
        or RegexMatchTimeoutException
        or DecoderFallbackException
        or EncoderFallbackException
        or JsonException
        or XmlException;
}
