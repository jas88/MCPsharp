# MCPsharp Tests

This directory contains comprehensive unit tests for the MCPsharp project, covering all Phase 1 features with >90% code coverage target.

## Test Structure

### Core Test Files
- **TestBase.cs** - Base test classes providing common functionality
- **TestDataFixtures.cs** - Test data and fixtures for various scenarios
- **SampleCSharpFiles.cs** - Sample C# code files for testing

### Service Tests
- **Services/Roslyn/** - Tests for reverse search services
  - `AdvancedReferenceFinderServiceTests.cs` - Comprehensive tests for advanced reference finding
- **Services/BulkEdit/** - Tests for bulk edit operations
  - `BulkEditServiceTests.cs` - Tests for bulk replace, conditional edit, and refactor operations
- **Services/Streaming/** - Tests for streaming file processing
  - `StreamingFileProcessorTests.cs` - Tests for large file handling and streaming operations
- **Services/Analyzers/** - Tests for analyzer integration
  - `AnalyzerHostTests.cs` - Tests for analyzer hosting and security

### Integration Tests
- **Integration/** - End-to-end tests for MCP tools
  - `McpToolsIntegrationTests.cs` - Comprehensive integration tests across services

### Performance Tests
- **Performance/** - Performance and stress testing
  - `PerformanceTests.cs` - Performance validation and benchmarking

## Test Categories

Tests are organized using NUnit categories:
- **Unit** - Individual service unit tests
- **Integration** - Cross-service integration tests
- **Performance** - Performance and stress tests
- **ReverseSearch** - Tests for reverse search functionality
- **BulkEdit** - Tests for bulk edit operations
- **Streaming** - Tests for streaming processors
- **Analyzers** - Tests for analyzer integration
- **Memory** - Memory usage and efficiency tests
- **Concurrency** - Parallel processing tests
- **Stress** - Stress testing scenarios
- **Regression** - Performance regression tests
- **Scaling** - Scaling behavior tests

## Running Tests

### Run All Tests
```bash
dotnet test
```

### Run Specific Categories
```bash
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Performance"
dotnet test --filter "Category=Integration"
```

### Run with Coverage
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Run Performance Tests Only
```bash
dotnet test --filter "Category=Performance"
```

## Test Data

### Sample Files
The tests use various sample C# files:
- Simple class with basic methods
- Inheritance hierarchies and interfaces
- Complex call chains and workflows
- Large files for performance testing
- Generic type usage examples
- Error patterns and edge cases

### Test Scenarios
- **Happy Path** - Normal operation scenarios
- **Edge Cases** - Boundary conditions and unusual inputs
- **Error Handling** - Exception scenarios and recovery
- **Performance** - Timing, memory usage, and scaling
- **Concurrency** - Parallel processing and thread safety
- **Integration** - End-to-end workflows

## Performance Targets

The tests validate the following performance targets:
- **Reverse Search** - <2 seconds for <1000 references
- **Bulk Edit** - Process 1000 files in <30 seconds
- **Streaming** - Handle large files with <100MB memory usage
- **Analyzers** - Performance targets for analyzer operations

## Coverage Goals

Target coverage:
- **Line Coverage**: >90%
- **Branch Coverage**: >85%
- **Method Coverage**: >95%

## Test Best Practices

1. **AAA Pattern** - Arrange, Act, Assert structure
2. **Descriptive Names** - Clear test names explaining scenarios
3. **Test Isolation** - Independent tests with proper setup/teardown
4. **Mocking** - Use NSubstitute for external dependencies
5. **Assertions** - Use FluentAssertions for readable test output
6. **Categories** - Organize tests with appropriate categories
7. **Cleanup** - Proper resource cleanup in tear-down methods

## Test Data Management

- Use `CreateTempFile()` and `CreateTempDirectory()` for test resources
- Automatic cleanup happens in `TearDown()` methods
- Test data is generated deterministically for consistent results
- Large test files are generated programmatically to avoid file bloat

## Running Specific Tests

### Individual Test Files
```bash
dotnet test --filter "ClassName=AdvancedReferenceFinderServiceTests"
```

### Specific Test Methods
```bash
dotnet test --filter "TestMethodName=FindCallersAsync_WithValidParameters_ShouldReturnCallerResult"
```

### Multiple Categories
```bash
dotnet test --filter "Category=Unit|Category=Integration"
```

## CI/CD Integration

The tests are designed to run in CI/CD pipelines:
- Fast execution for quick feedback
- Categorized for selective test runs
- Performance tests for regression detection
- Coverage reporting for quality metrics

## Debugging Tests

To debug failing tests:
1. Run with `--logger "console;verbosity=detailed"`
2. Use `Debugger.Break()` in test code
3. Run tests individually to isolate issues
4. Check test output and console logs

## Contributing

When adding new tests:
1. Follow existing naming conventions
2. Add appropriate categories
3. Include performance testing where relevant
4. Update this README if adding new test categories
5. Ensure proper cleanup of test resources