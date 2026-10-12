using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Services;

public partial class StreamingFileProcessor
{
    public async Task<StreamResult> ProcessFileWithTransformAsync(string filePath, StreamProcessRequest config, Func<string, string> transform)
    {
        try
        {
            // Read the original content
            var originalContent = await File.ReadAllTextAsync(filePath);

            // Apply the transform with exception handling
            string transformedContent;
            try
            {
                transformedContent = transform(originalContent);
            }
            catch (Exception ex)
            {
                LogTransformFailed(_logger, ex, filePath);
                return new StreamResult
                {
                    Success = false,
                    WasProcessed = false,
                    Error = $"Transform failed: {ex.Message}",
                    OutputPath = filePath,
                    ProcessingTime = TimeSpan.Zero
                };
            }

        // Create output path if not provided
        var outputPath = string.IsNullOrEmpty(config.OutputPath)
            ? filePath + ".processed"
            : config.OutputPath;

        // Create output directory if needed
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Write transformed content
        await File.WriteAllTextAsync(outputPath, transformedContent);

        var fileInfo = new FileInfo(filePath);

        var result = new StreamResult
        {
            Success = true,
            WasProcessed = true,
            OperationId = Guid.NewGuid().ToString(),
            OutputPath = outputPath,
            OriginalSize = fileInfo.Length,
            BytesProcessed = transformedContent.Length,
            ProcessedSize = transformedContent.Length,
            ChunksProcessed = 1,
            ProcessingTime = TimeSpan.FromMilliseconds(1), // Simplified timing
            OutputFiles = new List<string> { outputPath }
        };

        // Update statistics
        lock (_statisticsLock)
        {
            _statistics.TotalFilesProcessed++;
            _statistics.TotalBytesProcessed += result.BytesProcessed;
            _statistics.LastUpdated = DateTime.UtcNow;
        }

        return result;
        }
        catch (Exception ex)
        {
            LogFailedToProcessFileWith(_logger, ex, filePath);
            return new StreamResult
            {
                Success = false,
                WasProcessed = false,
                Error = $"Processing failed: {ex.Message}",
                OutputPath = filePath,
                ProcessingTime = TimeSpan.Zero
            };
        }
    }

    public async Task<StreamResult> ProcessFileWithFilterAsync(string filePath, StreamProcessRequest config, Func<string, bool> filter)
    {
        try
        {
            // Check if file exists first
            if (!File.Exists(filePath))
            {
                return new StreamResult
                {
                    Success = false,
                    WasProcessed = false,
                    OperationId = Guid.NewGuid().ToString(),
                    OutputPath = filePath,
                    OriginalSize = 0,
                    BytesProcessed = 0,
                    ProcessedSize = 0,
                    ChunksProcessed = 0,
                    ProcessingTime = TimeSpan.Zero,
                    ErrorMessage = $"File not found: {filePath}",
                    ProcessingSpeedBytesPerSecond = 0
                };
            }

            // Read the original content
            var originalContent = await File.ReadAllTextAsync(filePath);

            // Apply the filter
            var shouldProcess = filter(originalContent);

            var fileInfo = new FileInfo(filePath);

            if (shouldProcess)
            {
                // Process the file normally
                var result = await ProcessFileAsync(filePath, config);
                result.WasProcessed = true;
                result.ProcessedSize = result.BytesProcessed;
                result.OriginalSize = fileInfo.Length;

                // Update statistics
                lock (_statisticsLock)
                {
                    _statistics.TotalFilesProcessed++;
                    _statistics.TotalBytesProcessed += result.BytesProcessed;
                    _statistics.LastUpdated = DateTime.UtcNow;
                }

                return result;
            }
            else
            {
                // File was filtered out - don't process
                var result = new StreamResult
                {
                    Success = true,
                    WasProcessed = false,
                    OperationId = Guid.NewGuid().ToString(),
                    OutputPath = filePath,
                    OriginalSize = fileInfo.Length,
                    BytesProcessed = 0,
                    ProcessedSize = 0,
                    ChunksProcessed = 0,
                    ProcessingTime = TimeSpan.Zero,
                    ProcessingSpeedBytesPerSecond = 0
                };

                return result;
            }
        }
        catch (Exception ex)
        {
            return new StreamResult
            {
                Success = false,
                WasProcessed = false,
                OperationId = Guid.NewGuid().ToString(),
                OutputPath = filePath,
                OriginalSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0,
                BytesProcessed = 0,
                ProcessedSize = 0,
                ChunksProcessed = 0,
                ProcessingTime = TimeSpan.Zero,
                ErrorMessage = ex.Message,
                Error = ex.Message,
                Exception = ex,
                ProcessingSpeedBytesPerSecond = 0
            };
        }
    }
}
