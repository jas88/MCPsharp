# Extract Method MCP Tool API Specification

## Tool Definition

```yaml
name: extract_method
description: |
  Extract selected code into a new method with automatic parameter inference,
  return value detection, and proper handling of async/await, generics, and
  complex control flow patterns
```

## Request Schema

```typescript
interface ExtractMethodRequest {
  // Required: File containing the code to extract
  filePath: string;

  // Required: Selection range
  selection: {
    startLine: number;      // 1-based line number
    endLine: number;        // 1-based line number
    startColumn?: number;   // Optional: 1-based column for precise selection
    endColumn?: number;     // Optional: 1-based column for precise selection
  };

  // Optional: Method configuration
  methodName?: string;        // Custom name (auto-generated if not provided)
  accessibility?: "public" | "private" | "protected" | "internal"; // Default: "private"
  makeStatic?: boolean;       // Force static if possible (default: auto-detect)

  // Optional: Extraction options
  extractMode?: "statements" | "expression" | "partial"; // Default: "statements"
  returnStrategy?: "single" | "tuple" | "out"; // For multiple returns (default: auto)
  parameterOrder?: string[];  // Custom parameter ordering

  // Optional: Preview mode
  preview?: boolean;          // Return preview without applying (default: false)
}
```

## Response Schema

```typescript
interface ExtractMethodResponse {
  success: boolean;

  // Success response
  extraction?: {
    // Generated method details
    method: {
      name: string;
      signature: string;
      body: string;
      location: {
        filePath: string;
        line: number;
        column: number;
      };
    };

    // Call site replacement
    callSite: {
      code: string;
      location: {
        startLine: number;
        endLine: number;
        startColumn: number;
        endColumn: number;
      };
    };

    // Inferred information
    parameters: Array<{
      name: string;
      type: string;
      modifier?: "ref" | "out" | "in";
      defaultValue?: string;
    }>;

    returnType: string;
    returnVariables?: string[];  // For tuple returns

    // Method characteristics
    characteristics: {
      isAsync: boolean;
      isStatic: boolean;
      isGeneric: boolean;
      hasMultipleReturns: boolean;
      hasEarlyReturns: boolean;
      capturesVariables: boolean;
      containsAwait: boolean;
      containsYield: boolean;
    };

    // Applied transformations
    transformations?: Array<{
      type: "early-return" | "multiple-exit" | "async-wrap" | "tuple-return";
      description: string;
    }>;
  };

  // Preview mode additional info
  preview?: {
    originalCode: string;
    modifiedCode: string;
    diff: string;  // Unified diff format
  };

  // Error response
  error?: {
    code: ExtractMethodErrorCode;
    message: string;
    details?: string;
    suggestions?: string[];
  };

  // Warnings (non-fatal issues)
  warnings?: Array<{
    code: string;
    message: string;
    line?: number;
  }>;
}

enum ExtractMethodErrorCode {
  // Selection errors
  INCOMPLETE_SELECTION = "INCOMPLETE_SELECTION",
  INVALID_RANGE = "INVALID_RANGE",
  EMPTY_SELECTION = "EMPTY_SELECTION",

  // Control flow errors
  MULTIPLE_ENTRY_POINTS = "MULTIPLE_ENTRY_POINTS",
  GOTO_LABEL_PRESENT = "GOTO_LABEL_PRESENT",
  UNREACHABLE_CODE = "UNREACHABLE_CODE",

  // Semantic errors
  UNRESOLVED_SYMBOLS = "UNRESOLVED_SYMBOLS",
  COMPILATION_ERROR = "COMPILATION_ERROR",
  NAME_CONFLICT = "NAME_CONFLICT",

  // Feature limitations
  UNSUPPORTED_CONSTRUCT = "UNSUPPORTED_CONSTRUCT",
  COMPLEX_PATTERN_MATCHING = "COMPLEX_PATTERN_MATCHING",
  LOCAL_FUNCTION_EXTRACTION = "LOCAL_FUNCTION_EXTRACTION"
}
```

## Usage Examples

### Example 1: Basic Extraction

```json
// Request
{
  "filePath": "/src/Calculator.cs",
  "selection": {
    "startLine": 15,
    "endLine": 20
  }
}

// Response
{
  "success": true,
  "extraction": {
    "method": {
      "name": "CalculateTotal",
      "signature": "private decimal CalculateTotal(decimal price, int quantity)",
      "body": "return price * quantity * (1 + TaxRate);",
      "location": {
        "filePath": "/src/Calculator.cs",
        "line": 30,
        "column": 5
      }
    },
    "callSite": {
      "code": "var total = CalculateTotal(price, quantity);",
      "location": {
        "startLine": 15,
        "endLine": 15,
        "startColumn": 9,
        "endColumn": 50
      }
    },
    "parameters": [
      {
        "name": "price",
        "type": "decimal"
      },
      {
        "name": "quantity",
        "type": "int"
      }
    ],
    "returnType": "decimal",
    "characteristics": {
      "isAsync": false,
      "isStatic": false,
      "isGeneric": false,
      "hasMultipleReturns": false,
      "hasEarlyReturns": false,
      "capturesVariables": true,
      "containsAwait": false,
      "containsYield": false
    }
  }
}
```

### Example 2: Async Method Extraction

```json
// Request
{
  "filePath": "/src/DataService.cs",
  "selection": {
    "startLine": 25,
    "endLine": 35
  },
  "methodName": "FetchAndProcessDataAsync"
}

// Response
{
  "success": true,
  "extraction": {
    "method": {
      "name": "FetchAndProcessDataAsync",
      "signature": "private async Task<ProcessedData> FetchAndProcessDataAsync(string url)",
      "body": "var data = await httpClient.GetAsync(url);\nreturn ProcessResponse(data);",
      "location": {
        "filePath": "/src/DataService.cs",
        "line": 50,
        "column": 5
      }
    },
    "callSite": {
      "code": "var result = await FetchAndProcessDataAsync(apiUrl);",
      "location": {
        "startLine": 25,
        "endLine": 25
      }
    },
    "parameters": [
      {
        "name": "url",
        "type": "string"
      }
    ],
    "returnType": "Task<ProcessedData>",
    "characteristics": {
      "isAsync": true,
      "containsAwait": true
    }
  }
}
```

### Example 3: Multiple Return Values

```json
// Request
{
  "filePath": "/src/Parser.cs",
  "selection": {
    "startLine": 45,
    "endLine": 60
  },
  "returnStrategy": "tuple"
}

// Response
{
  "success": true,
  "extraction": {
    "method": {
      "name": "ParseAndValidate",
      "signature": "private (bool success, string message, ParsedData data) ParseAndValidate(string input)",
      "body": "// extraction logic",
      "location": {
        "filePath": "/src/Parser.cs",
        "line": 100,
        "column": 5
      }
    },
    "callSite": {
      "code": "(var success, var message, var data) = ParseAndValidate(input);",
      "location": {
        "startLine": 45,
        "endLine": 45
      }
    },
    "returnType": "(bool success, string message, ParsedData data)",
    "returnVariables": ["success", "message", "data"]
  }
}
```

### Example 4: Preview Mode

```json
// Request
{
  "filePath": "/src/Service.cs",
  "selection": {
    "startLine": 10,
    "endLine": 15
  },
  "preview": true
}

// Response
{
  "success": true,
  "extraction": {
    // ... normal extraction details ...
  },
  "preview": {
    "originalCode": "public void ProcessOrder(Order order)\n{\n    // lines 10-15\n    ValidateOrder(order);\n    CalculateTotals(order);\n    ApplyDiscounts(order);\n    UpdateInventory(order);\n    SendConfirmation(order);\n    LogTransaction(order);\n}",
    "modifiedCode": "public void ProcessOrder(Order order)\n{\n    ProcessOrderCore(order);\n}\n\nprivate void ProcessOrderCore(Order order)\n{\n    ValidateOrder(order);\n    CalculateTotals(order);\n    ApplyDiscounts(order);\n    UpdateInventory(order);\n    SendConfirmation(order);\n    LogTransaction(order);\n}",
    "diff": "@@ -10,6 +10,9 @@\n-    ValidateOrder(order);\n-    CalculateTotals(order);\n+    ProcessOrderCore(order);\n+}\n+\n+private void ProcessOrderCore(Order order)\n+{\n+    ValidateOrder(order);\n+    CalculateTotals(order);"
  }
}
```

### Example 5: Error Response

```json
// Request
{
  "filePath": "/src/ComplexLogic.cs",
  "selection": {
    "startLine": 20,
    "endLine": 40
  }
}

// Response
{
  "success": false,
  "error": {
    "code": "GOTO_LABEL_PRESENT",
    "message": "Cannot extract code containing goto statements or labels",
    "details": "The selection contains a goto statement at line 25 jumping to label 'retry' at line 35",
    "suggestions": [
      "Refactor the goto statement to use a loop or method call",
      "Select only the code before line 25 or after line 35",
      "Extract the entire method and refactor it afterward"
    ]
  },
  "warnings": [
    {
      "code": "DEEP_NESTING",
      "message": "Selected code has deep nesting (level 5)",
      "line": 28
    }
  ]
}
```

## Advanced Features

### Generic Method Extraction

```json
{
  "filePath": "/src/GenericProcessor.cs",
  "selection": {
    "startLine": 15,
    "endLine": 25
  }
}

// Automatically detects and preserves generic type parameters:
// private T ProcessItem<T>(T item) where T : IProcessable
```

### Iterator Method Extraction

```json
{
  "filePath": "/src/DataReader.cs",
  "selection": {
    "startLine": 30,
    "endLine": 45
  }
}

// Detects yield return and creates iterator:
// private IEnumerable<string> ReadLines(StreamReader reader)
```

### Ref/Out Parameter Handling

```json
{
  "filePath": "/src/Calculator.cs",
  "selection": {
    "startLine": 50,
    "endLine": 60
  }
}

// Detects variables that need ref/out:
// private bool TryParse(string input, out int result)
```

## Implementation Notes

1. **Cancellation Support**: All operations support cancellation tokens
2. **Incremental Compilation**: Updates are applied incrementally
3. **Transaction Safety**: All changes are atomic (all-or-nothing)
4. **Performance**: Typical extraction completes in < 500ms
5. **Memory Efficiency**: Streaming for large files
6. **Thread Safety**: Safe for concurrent extractions on different files

## Error Recovery

The tool includes automatic recovery for common issues:

1. **Missing semicolons**: Automatically adds if safe
2. **Incomplete blocks**: Completes blocks if unambiguous
3. **Type inference**: Infers var types when explicit type missing
4. **Namespace imports**: Adds required using statements
5. **Formatting**: Preserves original formatting style

## Limitations

Current limitations (to be addressed in future versions):

1. Cannot extract partial expressions within lambda bodies
2. Cannot extract code spanning multiple methods
3. Cannot extract code with preprocessor directives
4. Cannot extract unsafe code blocks
5. Limited support for code with compiler-generated types
6. No support for extracting to different files (same file only)

## Integration Example

```csharp
// In McpToolRegistry.cs
yield return new McpTool
{
    Name = "extract_method",
    Description = "Extract selected code into a new method with automatic parameter inference",
    InputSchema = JsonSchema.FromType<ExtractMethodRequest>(),
    Handler = async (args, token) =>
    {
        var request = JsonSerializer.Deserialize<ExtractMethodRequest>(args.ToString());
        var service = serviceProvider.GetRequiredService<ExtractMethodService>();

        var result = await service.ExtractMethodAsync(
            request.FilePath,
            request.Selection,
            request.MethodName,
            request.Options,
            token);

        return JsonSerializer.SerializeToElement(result);
    }
};
```