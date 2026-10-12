using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.AI;

public partial class AICodeTransformationService
{
    /// <summary>
    /// Parse AI response to extract code changes.
    /// </summary>
    private List<CodeChange> ParseAICodeResponse(string aiResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(StripMarkdownFences(aiResponse));
            var root = doc.RootElement;

            var changes = new List<CodeChange>();

            if (root.TryGetProperty("changes", out var changesElement))
            {
                foreach (var change in changesElement.EnumerateArray())
                {
                    changes.Add(new CodeChange
                    {
                        Type = change.GetProperty("type").GetString() ?? "replace",
                        Target = change.GetProperty("target").GetString() ?? "",
                        NewCode = change.TryGetProperty("newCode", out var newCode)
                            ? newCode.GetString()
                            : null
                    });
                }
            }

            return changes;
        }
        catch (Exception ex)
        {
            LogParseResponseFailed(_logger, ex, aiResponse);
            return new List<CodeChange>();
        }
    }

    /// <summary>
    /// Extract explanation from AI response.
    /// </summary>
    private static string ExtractExplanation(string aiResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(StripMarkdownFences(aiResponse));
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("explanation", out var explanation)
                && explanation.ValueKind is JsonValueKind.String or JsonValueKind.Null)
            {
                return explanation.GetString() ?? "No explanation provided";
            }
        }
        catch (JsonException)
        {
            // Response was not valid JSON; fall through to the default explanation.
        }

        return "AI transformation applied";
    }

    /// <summary>
    /// Remove markdown code fences (```json ... ```) surrounding an AI JSON response.
    /// </summary>
    private static string StripMarkdownFences(string aiResponse)
    {
        var cleanJson = aiResponse.Trim();
        if (cleanJson.StartsWith("```json", StringComparison.Ordinal))
        {
            cleanJson = cleanJson[7..];
        }
        if (cleanJson.StartsWith("```", StringComparison.Ordinal))
        {
            cleanJson = cleanJson[3..];
        }
        if (cleanJson.EndsWith("```", StringComparison.Ordinal))
        {
            cleanJson = cleanJson[..^3];
        }
        return cleanJson.Trim();
    }

    /// <summary>
    /// Apply code changes using Roslyn syntax tree transformations.
    /// </summary>
    private static SyntaxNode ApplyCodeChanges(SyntaxNode root, List<CodeChange> changes)
    {
        var newRoot = root;

        foreach (var change in changes)
        {
            // Parse target (e.g., "MethodDeclaration:MethodName")
            var parts = change.Target.Split(':');
            if (parts.Length != 2)
                continue;

            var nodeType = parts[0];
            var nodeName = parts[1];

            // Find target node
            SyntaxNode? targetNode = nodeType switch
            {
                "MethodDeclaration" => newRoot.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == nodeName),

                "PropertyDeclaration" => newRoot.DescendantNodes()
                    .OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(p => p.Identifier.Text == nodeName),

                "ClassDeclaration" => newRoot.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault(c => c.Identifier.Text == nodeName),

                _ => null
            };

            if (targetNode == null)
                continue;

            // Apply transformation
            newRoot = change.Type switch
            {
                "replace" when change.NewCode != null =>
                    ReplaceNodeWithParsedCode(newRoot, targetNode, change.NewCode),

                "insert" when change.NewCode != null =>
                    InsertNodeWithParsedCode(newRoot, targetNode, change.NewCode),

                "delete" => newRoot.RemoveNode(targetNode, SyntaxRemoveOptions.KeepNoTrivia)
                    ?? newRoot,

                _ => newRoot
            };
        }

        return newRoot;
    }

    /// <summary>
    /// Replace a node with parsed code, safely handling empty parse results.
    /// </summary>
    private static SyntaxNode ReplaceNodeWithParsedCode(SyntaxNode root, SyntaxNode targetNode, string newCode)
    {
        var parsedTree = SyntaxFactory.ParseSyntaxTree(newCode);
        var parsedRoot = parsedTree.GetRoot();
        var newNode = parsedRoot.DescendantNodes().FirstOrDefault();

        if (newNode == null)
            return root; // No valid node to insert, return unchanged

        return root.ReplaceNode(targetNode, newNode);
    }

    /// <summary>
    /// Insert a new node as a sibling after the target node.
    /// </summary>
    private static SyntaxNode InsertNodeWithParsedCode(SyntaxNode root, SyntaxNode targetNode, string newCode)
    {
        var parsedTree = SyntaxFactory.ParseSyntaxTree(newCode);
        var parsedRoot = parsedTree.GetRoot();
        var newNode = parsedRoot.DescendantNodes().FirstOrDefault();

        if (newNode == null)
            return root; // No valid node to insert, return unchanged

        // Use Roslyn's InsertNodesAfter for proper node insertion
        return root.InsertNodesAfter(targetNode, new[] { newNode });
    }
}
