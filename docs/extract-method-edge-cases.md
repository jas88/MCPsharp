# Extract Method Edge Cases - Comprehensive Matrix

## Control Flow Edge Cases

### 1. Multiple Return Statements
```csharp
// Input
void ProcessData(int value) {
    var result = Calculate(value);
    [|if (result < 0) {
        Log("Negative");
        return;
    }
    if (result > 100) {
        Log("Too large");
        return;
    }
    ProcessResult(result);|]
}

// Challenge: Multiple exit points
// Solution: Transform to single exit with result tracking
private void ExtractedMethod(int result) {
    if (result < 0) {
        Log("Negative");
        return;
    }
    if (result > 100) {
        Log("Too large");
        return;
    }
    ProcessResult(result);
}
```

### 2. Early Returns with Values
```csharp
// Input
int Calculate(int x) {
    [|if (x < 0) return -1;
    if (x == 0) return 0;
    var result = x * 2;
    if (result > 100) return 100;
    return result;|]
}

// Solution: Preserve return pattern
private int ExtractedMethod(int x) {
    if (x < 0) return -1;
    if (x == 0) return 0;
    var result = x * 2;
    if (result > 100) return 100;
    return result;
}
```

### 3. Yield Return (Iterator)
```csharp
// Input
IEnumerable<int> Generate() {
    [|for (int i = 0; i < 10; i++) {
        if (i % 2 == 0)
            yield return i;
    }|]
}

// Solution: Extract as iterator method
private IEnumerable<int> ExtractedMethod() {
    for (int i = 0; i < 10; i++) {
        if (i % 2 == 0)
            yield return i;
    }
}
```

### 4. Async/Await
```csharp
// Input
async Task ProcessAsync() {
    var data = GetData();
    [|var result = await FetchAsync(data);
    await SaveAsync(result);
    NotifyComplete(result);|]
}

// Solution: Extract as async method
private async Task ExtractedMethod(Data data) {
    var result = await FetchAsync(data);
    await SaveAsync(result);
    NotifyComplete(result);
}
```

### 5. Try-Catch-Finally
```csharp
// Input - Partial try block (ERROR)
void Process() {
    try {
        [|var data = LoadData();
        ValidateData(data);|]
        SaveData(data); // Not selected
    } catch(Exception ex) {
        LogError(ex);
    }
}
// ERROR: Cannot extract partial try block

// Input - Complete try-catch (OK)
void Process() {
    [|try {
        var data = LoadData();
        ValidateData(data);
        SaveData(data);
    } catch(ValidationException ex) {
        LogValidationError(ex);
    } catch(Exception ex) {
        LogError(ex);
    }|]
}

// Solution: Extract entire try-catch
private void ExtractedMethod() {
    try {
        var data = LoadData();
        ValidateData(data);
        SaveData(data);
    } catch(ValidationException ex) {
        LogValidationError(ex);
    } catch(Exception ex) {
        LogError(ex);
    }
}
```

### 6. Using Statement
```csharp
// Input
void ProcessFile(string path) {
    [|using (var stream = File.OpenRead(path)) {
        using (var reader = new StreamReader(stream)) {
            var content = reader.ReadToEnd();
            ProcessContent(content);
        }
    }|]
}

// Solution: Extract with using preserved
private void ExtractedMethod(string path) {
    using (var stream = File.OpenRead(path)) {
        using (var reader = new StreamReader(stream)) {
            var content = reader.ReadToEnd();
            ProcessContent(content);
        }
    }
}
```

### 7. Lock Statement
```csharp
// Input
void UpdateCounter() {
    [|lock (_syncRoot) {
        _counter++;
        if (_counter > MAX_VALUE)
            _counter = 0;
    }|]
}

// Solution: Pass lock object as parameter if local
private void ExtractedMethod(object syncRoot) {
    lock (syncRoot) {
        _counter++;
        if (_counter > MAX_VALUE)
            _counter = 0;
    }
}
```

### 8. Goto and Labels (FORBIDDEN)
```csharp
// Input
void Process() {
    [|retry:
    if (!TryProcess()) {
        if (retryCount++ < 3)
            goto retry;
    }|]
}
// ERROR: Cannot extract code with goto/labels
```

### 9. Switch Expression (C# 8.0+)
```csharp
// Input
string GetStatus(int code) {
    [|var status = code switch {
        200 => "OK",
        404 => "Not Found",
        500 => "Server Error",
        _ => "Unknown"
    };
    LogStatus(status);
    return status;|]
}

// Solution: Extract with expression
private string ExtractedMethod(int code) {
    var status = code switch {
        200 => "OK",
        404 => "Not Found",
        500 => "Server Error",
        _ => "Unknown"
    };
    LogStatus(status);
    return status;
}
```

### 10. Break/Continue in Loops
```csharp
// Input - Break/continue to outer loop (ERROR)
void Process() {
    for (int i = 0; i < 10; i++) {
        [|if (ShouldSkip(i))
            continue; // References outer loop
        if (ShouldStop(i))
            break; // References outer loop|]
        DoWork(i);
    }
}
// ERROR: Cannot extract break/continue referencing outer loop

// Input - Complete loop (OK)
void Process() {
    [|for (int i = 0; i < 10; i++) {
        if (ShouldSkip(i))
            continue;
        if (ShouldStop(i))
            break;
        DoWork(i);
    }|]
}
// OK: Can extract entire loop
```

## Variable Scope Edge Cases

### 11. Out Variable Declaration (C# 7.0+)
```csharp
// Input
void Process() {
    var input = GetInput();
    [|if (int.TryParse(input, out var number)) {
        ProcessNumber(number);
    }|]
    // 'number' not accessible here anyway
}

// Solution: Extract with out variable
private void ExtractedMethod(string input) {
    if (int.TryParse(input, out var number)) {
        ProcessNumber(number);
    }
}
```

### 12. Pattern Matching Variables
```csharp
// Input
void Process(object obj) {
    [|if (obj is string text && text.Length > 0) {
        ProcessText(text);
    } else if (obj is int number) {
        ProcessNumber(number);
    }|]
}

// Solution: Pass object, preserve patterns
private void ExtractedMethod(object obj) {
    if (obj is string text && text.Length > 0) {
        ProcessText(text);
    } else if (obj is int number) {
        ProcessNumber(number);
    }
}
```

### 13. Tuple Deconstruction
```csharp
// Input
void Process() {
    [|var (name, age) = GetPerson();
    ValidateName(name);
    ValidateAge(age);|]
    SavePerson(name, age); // Used after
}

// Solution: Return deconstructed values
private (string name, int age) ExtractedMethod() {
    var (name, age) = GetPerson();
    ValidateName(name);
    ValidateAge(age);
    return (name, age);
}
```

### 14. Ref Locals
```csharp
// Input
void Process(int[] array) {
    [|ref var element = ref array[0];
    element *= 2;
    ProcessElement(element);|]
}

// Solution: Pass array and index
private void ExtractedMethod(int[] array, int index) {
    ref var element = ref array[index];
    element *= 2;
    ProcessElement(element);
}
```

### 15. Anonymous Types
```csharp
// Input
void Process() {
    [|var person = new { Name = "John", Age = 30 };
    ProcessPerson(person.Name, person.Age);|]
}

// Solution: Generate named type or use dynamic
private void ExtractedMethod() {
    var person = new { Name = "John", Age = 30 };
    ProcessPerson(person.Name, person.Age);
}
// Warning: Anonymous type scope limited
```

### 16. LINQ Query Variables
```csharp
// Input
void Process(List<Order> orders) {
    [|var query = from o in orders
                 where o.Status == "Active"
                 select new { o.Id, o.Total };
    foreach (var item in query) {
        ProcessOrder(item.Id, item.Total);
    }|]
}

// Solution: Extract entire query
private void ExtractedMethod(List<Order> orders) {
    var query = from o in orders
                 where o.Status == "Active"
                 select new { o.Id, o.Total };
    foreach (var item in query) {
        ProcessOrder(item.Id, item.Total);
    }
}
```

### 17. Captured Variables (Closures)
```csharp
// Input
void Process() {
    int counter = 0;
    [|Action increment = () => counter++;
    Func<int> getCount = () => counter;|]

    increment();
    var count = getCount();
}

// Solution: Pass as ref or restructure
private (Action increment, Func<int> getCount) ExtractedMethod(ref int counter) {
    Action increment = () => counter++;
    Func<int> getCount = () => counter;
    return (increment, getCount);
}
// Warning: Closure semantics may change
```

### 18. Discards
```csharp
// Input
void Process() {
    [|var (result, _) = Calculate();
    if (result > 0) {
        ProcessResult(result);
    }|]
}

// Solution: Preserve discard pattern
private void ExtractedMethod() {
    var (result, _) = Calculate();
    if (result > 0) {
        ProcessResult(result);
    }
}
```

## Type System Edge Cases

### 19. Generic Type Parameters
```csharp
// Input
void Process<T>(T item) where T : IComparable<T> {
    [|if (item.CompareTo(default(T)) > 0) {
        ProcessValid(item);
    } else {
        ProcessInvalid(item);
    }|]
}

// Solution: Propagate generics and constraints
private void ExtractedMethod<T>(T item) where T : IComparable<T> {
    if (item.CompareTo(default(T)) > 0) {
        ProcessValid(item);
    } else {
        ProcessInvalid(item);
    }
}
```

### 20. Dynamic Types
```csharp
// Input
void Process(dynamic data) {
    [|var result = data.Calculate();
    if (result.Success) {
        SaveResult(result.Value);
    }|]
}

// Solution: Preserve dynamic
private void ExtractedMethod(dynamic data) {
    var result = data.Calculate();
    if (result.Success) {
        SaveResult(result.Value);
    }
}
```

### 21. Nullable Reference Types (C# 8.0+)
```csharp
// Input
void Process(string? input) {
    [|if (input != null) {
        var trimmed = input.Trim();
        if (trimmed.Length > 0) {
            ProcessValid(trimmed);
        }
    }|]
}

// Solution: Preserve nullable annotations
private void ExtractedMethod(string? input) {
    if (input != null) {
        var trimmed = input.Trim();
        if (trimmed.Length > 0) {
            ProcessValid(trimmed);
        }
    }
}
```

### 22. ValueTuple with Names
```csharp
// Input
void Process() {
    [|(string name, int age) person = GetPerson();
    ValidatePerson(person.name, person.age);
    var summary = $"{person.name} is {person.age}";|]
    SaveSummary(summary); // Used after
}

// Solution: Return named tuple
private (string summary, (string name, int age) person) ExtractedMethod() {
    (string name, int age) person = GetPerson();
    ValidatePerson(person.name, person.age);
    var summary = $"{person.name} is {person.age}";
    return (summary, person);
}
```

### 23. Extension Method Context
```csharp
// Input
public static class Extensions {
    public static void Process(this string text) {
        [|var upper = text.ToUpper();
        var trimmed = upper.Trim();
        Console.WriteLine(trimmed);|]
    }
}

// Solution: Pass 'this' parameter explicitly
private static void ExtractedMethod(string text) {
    var upper = text.ToUpper();
    var trimmed = upper.Trim();
    Console.WriteLine(trimmed);
}
```

## Special Constructs

### 24. Local Functions
```csharp
// Input
void Process() {
    [|bool IsValid(int x) => x > 0;

    var items = GetItems();
    var valid = items.Where(IsValid);
    ProcessItems(valid);|]
}

// Solution: Move local function to class level
private void ExtractedMethod() {
    var items = GetItems();
    var valid = items.Where(IsValid);
    ProcessItems(valid);
}

private bool IsValid(int x) => x > 0;
```

### 25. Fixed Statement
```csharp
// Input
unsafe void Process(byte[] buffer) {
    [|fixed (byte* ptr = buffer) {
        ProcessPointer(ptr);
    }|]
}

// Solution: Extract with unsafe context
private unsafe void ExtractedMethod(byte[] buffer) {
    fixed (byte* ptr = buffer) {
        ProcessPointer(ptr);
    }
}
```

### 26. Checked/Unchecked
```csharp
// Input
void Calculate(int x, int y) {
    [|checked {
        var result = x * y;
        if (result > MAX_VALUE)
            throw new OverflowException();
    }|]
}

// Solution: Preserve checked context
private void ExtractedMethod(int x, int y) {
    checked {
        var result = x * y;
        if (result > MAX_VALUE)
            throw new OverflowException();
    }
}
```

### 27. Stackalloc
```csharp
// Input
void Process() {
    [|Span<int> buffer = stackalloc int[100];
    FillBuffer(buffer);
    ProcessBuffer(buffer);|]
}

// Solution: Extract with Span parameter
private void ExtractedMethod() {
    Span<int> buffer = stackalloc int[100];
    FillBuffer(buffer);
    ProcessBuffer(buffer);
}
// Note: Cannot return stackalloc'd memory
```

### 28. Conditional Compilation
```csharp
// Input
void Process() {
    [|#if DEBUG
    LogDebug("Starting");
    #endif
    DoWork();
    #if DEBUG
    LogDebug("Complete");
    #endif|]
}

// Solution: Extract with directives
private void ExtractedMethod() {
    #if DEBUG
    LogDebug("Starting");
    #endif
    DoWork();
    #if DEBUG
    LogDebug("Complete");
    #endif
}
```

### 29. String Interpolation with Format
```csharp
// Input
void Process(Person person) {
    [|var message = $"Name: {person.Name,-20} Age: {person.Age:D3}";
    var formatted = string.Format(CultureInfo.InvariantCulture,
        "Date: {0:yyyy-MM-dd}", person.BirthDate);
    LogMessage(message);
    LogMessage(formatted);|]
}

// Solution: Preserve formatting
private void ExtractedMethod(Person person) {
    var message = $"Name: {person.Name,-20} Age: {person.Age:D3}";
    var formatted = string.Format(CultureInfo.InvariantCulture,
        "Date: {0:yyyy-MM-dd}", person.BirthDate);
    LogMessage(message);
    LogMessage(formatted);
}
```

### 30. Record Types (C# 9.0+)
```csharp
// Input
void Process(PersonRecord person) {
    [|var updated = person with { Age = person.Age + 1 };
    if (updated.Age > 18) {
        ProcessAdult(updated);
    }|]
    SavePerson(updated); // Used after
}

// Solution: Return modified record
private PersonRecord ExtractedMethod(PersonRecord person) {
    var updated = person with { Age = person.Age + 1 };
    if (updated.Age > 18) {
        ProcessAdult(updated);
    }
    return updated;
}
```

### 31. Init-Only Properties (C# 9.0+)
```csharp
// Input
void CreatePerson() {
    [|var person = new Person {
        Name = "John",
        Age = 30,
        Id = Guid.NewGuid() // init-only
    };
    ValidatePerson(person);|]
    SavePerson(person); // Used after
}

// Solution: Return created object
private Person ExtractedMethod() {
    var person = new Person {
        Name = "John",
        Age = 30,
        Id = Guid.NewGuid()
    };
    ValidatePerson(person);
    return person;
}
```

### 32. Range and Index (C# 8.0+)
```csharp
// Input
void Process(string[] items) {
    [|var subset = items[1..^1]; // Skip first and last
    foreach (var item in subset) {
        ProcessItem(item);
    }|]
}

// Solution: Pass array, preserve range syntax
private void ExtractedMethod(string[] items) {
    var subset = items[1..^1];
    foreach (var item in subset) {
        ProcessItem(item);
    }
}
```

### 33. Async Streams (C# 8.0+)
```csharp
// Input
async Task ProcessStream() {
    [|await foreach (var item in GetAsyncStream()) {
        if (await ValidateAsync(item)) {
            await ProcessAsync(item);
        }
    }|]
}

// Solution: Extract as async method
private async Task ExtractedMethod() {
    await foreach (var item in GetAsyncStream()) {
        if (await ValidateAsync(item)) {
            await ProcessAsync(item);
        }
    }
}
```

## Validation Matrix

| Scenario | Can Extract | Requirements | Warning |
|----------|------------|--------------|---------|
| Multiple returns | ✅ | Transform to single exit | Performance impact |
| Yield return | ✅ | Extract as iterator | Changes method signature |
| Async/await | ✅ | Extract as async | Propagates async |
| Try-catch partial | ❌ | Select complete try-catch | - |
| Using statement | ✅ | Include complete using | Resource lifetime |
| Lock statement | ✅ | Pass lock object if local | Thread safety |
| Goto/labels | ❌ | Refactor first | - |
| Out variables | ✅ | Declare before or in params | Scope change |
| Pattern matching | ✅ | Include complete pattern | - |
| Ref locals | ✅ | Pass container and index | Performance |
| Anonymous types | ⚠️ | Limited to method scope | Type visibility |
| Closures | ⚠️ | Pass captured as params | Semantic change |
| Generic methods | ✅ | Propagate type params | Constraints |
| Dynamic | ✅ | Preserve dynamic typing | Runtime binding |
| Local functions | ✅ | Move to class level | Visibility change |
| Stackalloc | ⚠️ | Cannot return stack memory | Lifetime |
| Preprocessor | ✅ | Include all directives | Conditional compilation |

## Testing Checklist

Each edge case should have:
1. ✅ Positive test (successful extraction)
2. ✅ Negative test (expected failure)
3. ✅ Warning test (extraction with warnings)
4. ✅ Transformation test (verify correctness)
5. ✅ Round-trip test (extract then inline)
6. ✅ Performance test (large code blocks)
7. ✅ Semantic preservation test