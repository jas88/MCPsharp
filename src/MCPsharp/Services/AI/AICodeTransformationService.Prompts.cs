namespace MCPsharp.Services.AI;

public partial class AICodeTransformationService
{
    /// <summary>
    /// Build prompt for AI to suggest bug fix.
    /// </summary>
    private static string BuildFixPrompt(string code, string description, int? lineNumber)
    {
        var lineContext = lineNumber.HasValue ? $"Focus on line {lineNumber}" : "";

        return $@"You are a C# code fixing expert. Fix the following bug using Roslyn AST transformations.

## Current Code

```csharp
{code}
```

## Bug Description

{description}
{lineContext}

## Instructions

Return a JSON response with this structure:

```json
{{
  ""explanation"": ""Brief explanation of the fix"",
  ""changes"": [
    {{
      ""type"": ""replace"" | ""insert"" | ""delete"",
      ""target"": ""MethodDeclaration:MethodName"" | ""PropertyDeclaration:PropertyName"" | ""ClassDeclaration:ClassName"",
      ""newCode"": ""C# code for replacement/insertion (if applicable)""
    }}
  ]
}}
```

IMPORTANT:
- Only return valid C# syntax
- Use Roslyn node types (MethodDeclaration, PropertyDeclaration, etc.)
- Ensure the result compiles
- Preserve formatting and indentation style
- Return ONLY the JSON, no markdown code blocks
";
    }

    /// <summary>
    /// Build prompt for AI refactoring.
    /// </summary>
    private static string BuildRefactorPrompt(string code, string goal)
    {
        return $@"You are a C# refactoring expert. Refactor the following code using Roslyn AST transformations.

## Current Code

```csharp
{code}
```

## Refactoring Goal

{goal}

## Instructions

Return a JSON response with this structure:

```json
{{
  ""explanation"": ""Brief explanation of the refactoring"",
  ""changes"": [
    {{
      ""type"": ""replace"" | ""insert"" | ""delete"",
      ""target"": ""MethodDeclaration:MethodName"" | ""PropertyDeclaration:PropertyName"" | ""ClassDeclaration:ClassName"",
      ""newCode"": ""C# code for replacement/insertion (if applicable)""
    }}
  ]
}}
```

IMPORTANT:
- Preserve behavior while improving code quality
- Use modern C# idioms
- Ensure the result compiles
- Maintain existing API contracts
- Return ONLY the JSON, no markdown code blocks
";
    }

    /// <summary>
    /// Build prompt for AI feature implementation.
    /// </summary>
    private static string BuildImplementationPrompt(string code, string featureDescription)
    {
        return $@"You are a C# implementation expert. Add the requested feature to the following code using Roslyn AST transformations.

## Current Code

```csharp
{code}
```

## Feature to Implement

{featureDescription}

## Instructions

Return a JSON response with this structure:

```json
{{
  ""explanation"": ""Brief explanation of the implementation"",
  ""changes"": [
    {{
      ""type"": ""replace"" | ""insert"",
      ""target"": ""MethodDeclaration:MethodName"" | ""PropertyDeclaration:PropertyName"" | ""ClassDeclaration:ClassName"",
      ""newCode"": ""C# code for replacement/insertion""
    }}
  ]
}}
```

IMPORTANT:
- Generate clean, idiomatic C# code
- Follow existing code style and conventions
- Ensure the result compiles
- Add necessary using statements
- Return ONLY the JSON, no markdown code blocks
";
    }
}
