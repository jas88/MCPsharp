using System.Text;
using MCPsharp.Models.Consolidated;
using MCPsharp.Models.Streaming;
using ChunkProcessor = MCPsharp.Services.Streaming.IStreamProcessor;
using StreamingLineProcessor = MCPsharp.Services.Streaming.LineStreamProcessor;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Drives a chunk-based <see cref="ChunkProcessor"/> from <c>MCPsharp.Services.Streaming</c> over a
/// consolidated input/output stream pair: reads the input in chunks, feeds each chunk through the
/// processor and writes the processed chunks to the output.
/// </summary>
internal static class StreamingProcessorAdapter
{
    private const int DefaultChunkSize = 65536;

    /// <summary>
    /// The encoding of the text chunks exchanged with the processor in <see cref="ProcessLinesAsync"/>
    /// </summary>
    public static readonly Encoding ChunkEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly byte[] ChunkNewLine = ChunkEncoding.GetBytes(Environment.NewLine);

    /// <summary>
    /// Processes text input line by line. Chunks always end on a line boundary so no line is split
    /// between two chunks; each line in a chunk is terminated with <see cref="Environment.NewLine"/>,
    /// and the chunk's metadata carries the number of its first line
    /// (<see cref="StreamingLineProcessor.StartLineNumberKey"/>) so line numbering continues across
    /// chunks. Chunks are handed to the processor as UTF-8 (<see cref="ChunkEncoding"/>); processed
    /// chunks are written in the input's encoding (see <see cref="TextStreamEncoding"/>), terminated
    /// with a newline if they are not already, after the input's byte order mark if it had one.
    /// </summary>
    public static async Task<ProcessingResult> ProcessLinesAsync(
        ChunkProcessor processor,
        Stream input,
        Stream output,
        ProcessingContext context,
        Dictionary<string, object> options,
        Encoding inputEncoding,
        CancellationToken ct)
    {
        if (!await processor.ValidateOptionsAsync(options).ConfigureAwait(false))
            return InvalidOptions(processor);

        var chunkSize = ResolveChunkSize(context);
        var state = new RunState();
        var batch = new List<string>();
        long batchBytes = 0;
        long linesRead = 0;

        var text = await TextStreamEncoding.DetectAsync(input, inputEncoding, ct).ConfigureAwait(false);
        state.BytesWritten += await text.WriteByteOrderMarkAsync(output, ct).ConfigureAwait(false);

        using var reader = text.CreateReader();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            linesRead++;
            batch.Add(line);
            batchBytes += ChunkEncoding.GetByteCount(line) + ChunkNewLine.Length;

            if (batchBytes >= chunkSize)
            {
                await ProcessTextBatchAsync(processor, output, options, text, batch, linesRead - batch.Count + 1, state, isLast: false, ct).ConfigureAwait(false);
                batch.Clear();
                batchBytes = 0;
            }
        }

        if (batch.Count > 0)
            await ProcessTextBatchAsync(processor, output, options, text, batch, linesRead - batch.Count + 1, state, isLast: true, ct).ConfigureAwait(false);

        await output.FlushAsync(ct).ConfigureAwait(false);

        return new ProcessingResult
        {
            Success = true,
            RecordsProcessed = linesRead,
            BytesProcessed = input.CanSeek ? input.Length : state.BytesRead,
            Message = $"Processed {linesRead} line(s) in {state.Chunks} chunk(s) with the {processor.ProcessorType}",
            Metrics = state.ToMetrics(("LinesRead", linesRead))
        };
    }

    /// <summary>
    /// Processes binary input in fixed-size chunks. When <paramref name="boundaryPattern"/> is set,
    /// chunk boundaries are moved so that no occurrence of the pattern straddles two chunks.
    /// </summary>
    public static async Task<ProcessingResult> ProcessBytesAsync(
        ChunkProcessor processor,
        Stream input,
        Stream output,
        ProcessingContext context,
        Dictionary<string, object> options,
        byte[]? boundaryPattern,
        CancellationToken ct)
    {
        if (!await processor.ValidateOptionsAsync(options).ConfigureAwait(false))
            return InvalidOptions(processor);

        var chunkSize = ResolveChunkSize(context);
        var readBuffer = new byte[chunkSize];
        var pending = Array.Empty<byte>();
        var state = new RunState();
        var checksums = new List<string>();

        while (true)
        {
            var read = await input.ReadAsync(readBuffer.AsMemory(0, chunkSize), ct).ConfigureAwait(false);
            var data = new byte[pending.Length + read];
            pending.CopyTo(data, 0);
            Array.Copy(readBuffer, 0, data, pending.Length, read);

            if (read == 0)
            {
                if (data.Length > 0)
                    await ProcessByteChunkAsync(processor, output, options, data, state, checksums, isLast: true, ct).ConfigureAwait(false);
                break;
            }

            var cut = FindSafeCut(data, boundaryPattern);
            pending = data[cut..];
            if (cut > 0)
                await ProcessByteChunkAsync(processor, output, options, data[..cut], state, checksums, isLast: false, ct).ConfigureAwait(false);
        }

        await output.FlushAsync(ct).ConfigureAwait(false);

        var metrics = state.ToMetrics();
        if (checksums.Count > 0)
            metrics["ChunkChecksums"] = checksums;

        return new ProcessingResult
        {
            Success = true,
            RecordsProcessed = state.Chunks,
            BytesProcessed = state.BytesRead,
            Message = $"Processed {state.BytesRead} byte(s) in {state.Chunks} chunk(s) with the {processor.ProcessorType}",
            Metrics = metrics
        };
    }

    /// <summary>
    /// Returns the largest prefix length of <paramref name="data"/> that can be processed without
    /// splitting a (greedy, left-to-right) occurrence of <paramref name="pattern"/>.
    /// </summary>
    internal static int FindSafeCut(byte[] data, byte[]? pattern)
    {
        if (pattern is not { Length: > 1 })
            return data.Length;

        var position = 0;
        while (position <= data.Length - pattern.Length)
        {
            var index = data.AsSpan(position).IndexOf(pattern);
            if (index < 0)
                break;
            position += index + pattern.Length;
        }

        // A match that is not complete within the buffer can only start in the last (pattern.Length - 1) bytes
        return Math.Max(position, data.Length - pattern.Length + 1);
    }

    private static async Task ProcessTextBatchAsync(
        ChunkProcessor processor,
        Stream output,
        Dictionary<string, object> options,
        TextStreamEncoding outputEncoding,
        List<string> batch,
        long firstLineNumber,
        RunState state,
        bool isLast,
        CancellationToken ct)
    {
        var text = new StringBuilder();
        foreach (var line in batch)
            text.Append(line).Append(Environment.NewLine);

        var metadata = new Dictionary<string, object> { [StreamingLineProcessor.StartLineNumberKey] = firstLineNumber };
        var processed = await RunChunkAsync(processor, options, ChunkEncoding.GetBytes(text.ToString()), state, isLast, metadata).ConfigureAwait(false);

        if (processed.Data.Length == 0)
            return;

        state.BytesWritten += await outputEncoding.WriteUtf8Async(output, processed.Data, ct).ConfigureAwait(false);

        if (!processed.Data.AsSpan().EndsWith(ChunkNewLine))
            state.BytesWritten += await outputEncoding.WriteUtf8Async(output, ChunkNewLine, ct).ConfigureAwait(false);
    }

    private static async Task ProcessByteChunkAsync(
        ChunkProcessor processor,
        Stream output,
        Dictionary<string, object> options,
        byte[] data,
        RunState state,
        List<string> checksums,
        bool isLast,
        CancellationToken ct)
    {
        var processed = await RunChunkAsync(processor, options, data, state, isLast).ConfigureAwait(false);
        if (processed.Checksum != null)
            checksums.Add(processed.Checksum);

        await output.WriteAsync(processed.Data, ct).ConfigureAwait(false);
        state.BytesWritten += processed.Data.Length;
    }

    private static async Task<StreamChunk> RunChunkAsync(
        ChunkProcessor processor,
        Dictionary<string, object> options,
        byte[] data,
        RunState state,
        bool isLast,
        Dictionary<string, object>? metadata = null)
    {
        var chunk = new StreamChunk
        {
            Data = data,
            Position = state.BytesRead,
            Length = data.Length,
            ChunkIndex = state.Chunks,
            IsLastChunk = isLast,
            Metadata = metadata ?? new Dictionary<string, object>()
        };

        var processed = await processor.ProcessChunkAsync(chunk, options).ConfigureAwait(false);
        state.Chunks++;
        state.BytesRead += data.Length;
        return processed;
    }

    private static int ResolveChunkSize(ProcessingContext context)
        => context.ChunkSize > 0 ? context.ChunkSize : DefaultChunkSize;

    private static ProcessingResult InvalidOptions(ChunkProcessor processor)
        => StreamProcessorExecution.Failure($"Invalid processor options for the {processor.ProcessorType}");

    private sealed class RunState
    {
        public long Chunks { get; set; }
        public long BytesRead { get; set; }
        public long BytesWritten { get; set; }

        public Dictionary<string, object> ToMetrics(params (string Key, object Value)[] extra)
        {
            var metrics = new Dictionary<string, object>
            {
                ["ChunksProcessed"] = Chunks,
                ["BytesRead"] = BytesRead,
                ["BytesWritten"] = BytesWritten
            };

            foreach (var (key, value) in extra)
                metrics[key] = value;

            return metrics;
        }
    }
}
