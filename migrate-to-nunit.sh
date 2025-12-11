#!/bin/bash

# Script to migrate test files from xUnit + FluentAssertions to NUnit 4

TEST_DIR="/Users/jas88/Developer/Github/MCPsharp/tests/MCPsharp.Tests"

# Find all .cs test files (excluding bin, obj, and non-test files)
find "$TEST_DIR" -name "*Tests.cs" -type f ! -path "*/bin/*" ! -path "*/obj/*" | while read -r file; do
    echo "Processing: $file"

    # Create backup
    cp "$file" "$file.bak"

    # Replace using Xunit with using NUnit.Framework
    sed -i '' 's/using Xunit;/using NUnit.Framework;/g' "$file"

    # Remove FluentAssertions using statements
    sed -i '' '/using FluentAssertions;/d' "$file"

    # Replace [Fact] with [Test]
    sed -i '' 's/\[Fact\]/[Test]/g' "$file"

    # Replace [Theory] with [Test] (will need manual review for TestCase)
    sed -i '' 's/\[Theory\]/[Test]/g' "$file"

    # Replace [InlineData(...)] with [TestCase(...)]
    sed -i '' 's/\[InlineData(/[TestCase(/g' "$file"

    # Replace xUnit assertions with NUnit
    # Note: These are basic replacements; complex cases may need manual review

    # Assert.Equal(expected, actual) -> Assert.That(actual, Is.EqualTo(expected))
    # This is tricky and needs careful handling - will do in code review

    # Assert.True -> Assert.That(x, Is.True)
    sed -i '' 's/Assert\.True(\([^)]*\))/Assert.That(\1, Is.True)/g' "$file"

    # Assert.False -> Assert.That(x, Is.False)
    sed -i '' 's/Assert\.False(\([^)]*\))/Assert.That(\1, Is.False)/g' "$file"

    # Assert.Null -> Assert.That(x, Is.Null)
    sed -i '' 's/Assert\.Null(\([^)]*\))/Assert.That(\1, Is.Null)/g' "$file"

    # Assert.NotNull -> Assert.That(x, Is.Not.Null)
    sed -i '' 's/Assert\.NotNull(\([^)]*\))/Assert.That(\1, Is.Not.Null)/g' "$file"

    # Assert.Empty -> Assert.That(x, Is.Empty)
    sed -i '' 's/Assert\.Empty(\([^)]*\))/Assert.That(\1, Is.Empty)/g' "$file"

    # Assert.NotEmpty -> Assert.That(x, Is.Not.Empty)
    sed -i '' 's/Assert\.NotEmpty(\([^)]*\))/Assert.That(\1, Is.Not.Empty)/g' "$file"

done

echo "Migration complete. Review the changes and delete .bak files if satisfied."
