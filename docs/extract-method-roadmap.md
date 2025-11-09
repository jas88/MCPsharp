# Extract Method Refactoring - Implementation Roadmap

## Executive Summary

Extract Method is a critical refactoring operation for MCPsharp that enables safe, semantic-aware code extraction. This roadmap provides a phased implementation plan to deliver a production-ready solution handling 33+ edge cases.

## Quick Reference

- **Priority**: HIGH (P3)
- **Timeline**: 5 weeks
- **Complexity**: High (33+ edge cases, complex data flow)
- **Dependencies**: RoslynWorkspace, SemanticModel, DataFlowAnalysis
- **Risk Level**: Medium (can break code if done incorrectly)

## Architecture Overview

```
ExtractMethodService
├── SelectionAnalyzer      (Validates extraction feasibility)
├── DataFlowAnalyzer       (Infers parameters and returns)
├── MethodGenerator        (Creates new method)
├── CallSiteRewriter       (Replaces original code)
└── EdgeCaseHandlers       (33+ specialized handlers)
```

## Phase 1: Foundation (Week 1)

### Goals
- Basic extraction for simple statements
- Single parameter/return inference
- Core infrastructure setup

### Deliverables
1. **ExtractMethodService.cs** - Core orchestration
2. **SelectionAnalyzer.cs** - Basic validation
3. **Simple test suite** - 10 basic scenarios

### Implementation Files
```
/src/MCPsharp/Services/Refactoring/
├── ExtractMethodService.cs
├── SelectionAnalyzer.cs
├── Models/
│   ├── ExtractMethodRequest.cs
│   ├── ExtractMethodResult.cs
│   └── MethodSignature.cs
└── Tests/
    └── BasicExtractionTests.cs
```

### Success Criteria
- Extract 5-10 line selections
- Handle simple parameters (value types)
- Single return value support
- 90% test coverage

## Phase 2: Data Flow Analysis (Week 2)

### Goals
- Complete parameter inference
- Multiple return strategies
- Ref/out parameter detection

### Deliverables
1. **DataFlowAnalyzer.cs** - Roslyn data flow integration
2. **ParameterInferencer.cs** - Smart parameter detection
3. **ReturnValueAnalyzer.cs** - Return strategy selection

### Key Algorithms
```csharp
// Parameter inference logic
DataFlowsIn → Filter(NotThis, NotConstant) → Parameters
WrittenInside ∩ ReadOutside → Return/Ref/Out
CapturedVariables → Closure analysis
```

### Edge Cases Covered
- Ref parameters (variable modified and read after)
- Out parameters (variable only written)
- Multiple returns (tuple vs out parameters)
- No parameters/returns scenarios

## Phase 3: Control Flow Handling (Week 3)

### Goals
- Async/await support
- Exception handling
- Complex control flow patterns

### Deliverables
1. **AsyncAwaitHandler.cs** - Async method extraction
2. **ExceptionHandler.cs** - Try/catch/finally
3. **ControlFlowTransformer.cs** - Multiple exit transformation

### Edge Cases Covered
- Async/await propagation
- Try-catch extraction rules
- Using/lock statements
- Multiple return paths
- Early returns
- Yield return (iterators)

### Complex Scenarios
```csharp
// Multi-exit transformation
if (x < 0) return -1;     →    ExtractedMethod(x, out result);
if (x > 100) return 100;  →    return result;
return x * 2;
```

## Phase 4: Advanced Type Handling (Week 4)

### Goals
- Generic method extraction
- Modern C# features
- Complex type scenarios

### Deliverables
1. **GenericMethodHandler.cs** - Type parameter inference
2. **PatternMatchingHandler.cs** - C# 7+ patterns
3. **ModernCSharpHandler.cs** - Records, init-only, ranges

### Edge Cases Covered
- Generic type parameters and constraints
- Pattern matching variables
- Anonymous types
- Tuple deconstruction
- Record types (C# 9+)
- Nullable reference types
- Dynamic types

### Test Matrix
| Feature | C# Version | Complexity | Test Count |
|---------|------------|------------|------------|
| Patterns | 7.0+ | High | 5 |
| Tuples | 7.0+ | Medium | 3 |
| Nullable | 8.0+ | Medium | 4 |
| Records | 9.0+ | Low | 2 |
| Ranges | 8.0+ | Low | 2 |

## Phase 5: Production Ready (Week 5)

### Goals
- Performance optimization
- Error recovery
- User experience polish

### Deliverables
1. **Performance optimizations** - Caching, incremental updates
2. **Error messages** - Actionable, user-friendly
3. **Preview mode** - Show before/after
4. **Integration tests** - Real-world scenarios

### Quality Metrics
- **Performance**: < 500ms for 95% of extractions
- **Accuracy**: 100% semantic preservation
- **Coverage**: Handle 95% of real scenarios
- **Usability**: Clear error messages with fixes

## Implementation Checklist

### Core Components
- [ ] ExtractMethodService
- [ ] SelectionAnalyzer
- [ ] DataFlowAnalyzer
- [ ] MethodGenerator
- [ ] CallSiteRewriter

### Edge Case Handlers (33+)
- [ ] AsyncAwaitHandler
- [ ] YieldReturnHandler
- [ ] ExceptionHandler
- [ ] UsingStatementHandler
- [ ] LockStatementHandler
- [ ] GenericTypeHandler
- [ ] PatternMatchingHandler
- [ ] TupleHandler
- [ ] RefOutHandler
- [ ] ClosureHandler
- [ ] (23 more...)

### Testing
- [ ] Unit tests (100+ test cases)
- [ ] Integration tests (20+ scenarios)
- [ ] Performance benchmarks
- [ ] Edge case matrix validation
- [ ] Round-trip tests (extract → inline)

### Documentation
- [ ] API specification
- [ ] Edge case matrix
- [ ] Implementation guide
- [ ] User documentation

## Risk Mitigation

### High Risk Areas
1. **Data flow analysis accuracy** → Extensive test coverage
2. **Multiple return handling** → Multiple strategies (tuple/out)
3. **Async propagation** → Careful signature transformation
4. **Generic constraints** → Full constraint propagation

### Fallback Strategies
1. **Unsupported constructs** → Clear error messages
2. **Complex scenarios** → Suggest manual refactoring
3. **Performance issues** → Cancellation support
4. **Compilation errors** → Rollback mechanism

## Integration Points

### MCP Tool Registration
```csharp
yield return new McpTool
{
    Name = "extract_method",
    Description = "Extract selected code into a new method",
    InputSchema = JsonSchema.FromType<ExtractMethodRequest>(),
    Handler = ExtractMethodHandler
};
```

### Service Dependencies
```csharp
services.AddScoped<ExtractMethodService>();
services.AddScoped<SelectionAnalyzer>();
services.AddScoped<DataFlowAnalyzer>();
services.AddScoped<MethodGenerator>();
services.AddScoped<CallSiteRewriter>();
// Edge case handlers...
```

## Success Metrics

### Week 1
- Basic extraction working
- 10 test cases passing
- Core infrastructure complete

### Week 2
- Parameter inference accurate
- 25 test cases passing
- Data flow analysis complete

### Week 3
- Async/control flow working
- 50 test cases passing
- Major edge cases handled

### Week 4
- Generic/modern C# support
- 80 test cases passing
- Complex scenarios handled

### Week 5
- Performance optimized
- 100+ test cases passing
- Production ready

## Future Enhancements (Post-MVP)

1. **Extract to Interface** - Create interface method
2. **Extract to Base Class** - Move to parent class
3. **Extract to Extension** - Create extension method
4. **Cross-file Extraction** - Extract to different file
5. **Batch Extraction** - Multiple extractions
6. **AI-powered Naming** - Smart method names
7. **Test Generation** - Create unit tests

## Key Files Created

1. `/docs/extract-method-design.md` - Complete architecture design
2. `/docs/extract-method-api.md` - MCP tool API specification
3. `/docs/extract-method-implementation.md` - Implementation details
4. `/docs/extract-method-edge-cases.md` - 33+ edge case matrix
5. `/docs/extract-method-roadmap.md` - This roadmap

## Next Steps

1. **Review** design documents with team
2. **Create** feature branch `feature/extract-method`
3. **Implement** Phase 1 foundation
4. **Test** with real-world C# projects
5. **Iterate** based on feedback

## Contact Points

- **Feature Lead**: ExtractMethodService owner
- **Code Review**: Roslyn team members
- **Testing**: QA team for edge cases
- **Documentation**: Technical writers

---

*This roadmap provides a structured path to implementing robust extract method refactoring in MCPsharp. The phased approach ensures we deliver value incrementally while building toward comprehensive edge case handling.*