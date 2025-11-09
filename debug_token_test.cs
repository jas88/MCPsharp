using System;
using System.Text.Json;
using MCPsharp.Services;
using MCPsharp.Models;

// Simple test to debug token estimation
var config = new ResponseConfiguration
{
    MaxTokens = 100,
    EnableTruncation = true,
    TruncateLength = 200
};

var processor = new ResponseProcessor(config);

// Test data from the failing test
var largeResponse = new
{
    message = "This is a large response",
    data = new string('x', 2000) // This should exceed our 100 token limit
};
var toolName = "test_tool";

// Serialize to see actual JSON length
var jsonString = JsonSerializer.Serialize(largeResponse, new JsonSerializerOptions
{
    WriteIndented = false
});

Console.WriteLine($"JSON string length: {jsonString.Length}");
Console.WriteLine($"Estimated tokens: {jsonString.Length * 0.25}");

var result = processor.ProcessResponse(largeResponse, toolName);

Console.WriteLine($"WasTruncated: {result.WasTruncated}");
Console.WriteLine($"EstimatedTokenCount: {result.EstimatedTokenCount}");
Console.WriteLine($"OriginalTokenCount: {result.OriginalTokenCount}");
Console.WriteLine($"MaxTokens: {config.MaxTokens}");
Console.WriteLine($"Estimated <= MaxTokens: {result.EstimatedTokenCount <= config.MaxTokens}");

// Show the JSON
Console.WriteLine($"JSON: {jsonString}");