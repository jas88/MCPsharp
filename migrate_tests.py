#!/usr/bin/env python3
"""
Script to migrate test files from xUnit + FluentAssertions to NUnit 4
"""

import os
import re
from pathlib import Path

TEST_DIR = "/Users/jas88/Developer/Github/MCPsharp/tests/MCPsharp.Tests"

def migrate_file(filepath):
    """Migrate a single test file from xUnit to NUnit"""
    print(f"Processing: {filepath}")

    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    original_content = content

    # Replace using statements
    content = content.replace('using Xunit;', 'using NUnit.Framework;')
    content = re.sub(r'using FluentAssertions;\n?', '', content)

    # Replace attributes
    content = re.sub(r'\[Fact\]', '[Test]', content)
    content = re.sub(r'\[Theory\]', '[Test]', content)
    content = re.sub(r'\[InlineData\(', '[TestCase(', content)

    # Replace xUnit assertions - order matters!
    # Assert.Equal(expected, actual) -> Assert.That(actual, Is.EqualTo(expected))
    # This needs to swap the parameters
    def replace_equal(match):
        expected = match.group(1)
        actual = match.group(2)
        return f'Assert.That({actual}, Is.EqualTo({expected}))'

    content = re.sub(r'Assert\.Equal\(([^,]+),\s*([^)]+)\)', replace_equal, content)

    # Assert.True/False
    content = re.sub(r'Assert\.True\(([^)]+)\)', r'Assert.That(\1, Is.True)', content)
    content = re.sub(r'Assert\.False\(([^)]+)\)', r'Assert.That(\1, Is.False)', content)

    # Assert.Null/NotNull
    content = re.sub(r'Assert\.Null\(([^)]+)\)', r'Assert.That(\1, Is.Null)', content)
    content = re.sub(r'Assert\.NotNull\(([^)]+)\)', r'Assert.That(\1, Is.Not.Null)', content)

    # Assert.Empty/NotEmpty
    content = re.sub(r'Assert\.Empty\(([^)]+)\)', r'Assert.That(\1, Is.Empty)', content)
    content = re.sub(r'Assert\.NotEmpty\(([^)]+)\)', r'Assert.That(\1, Is.Not.Empty)', content)

    # Assert.Contains(substring, string) -> Assert.That(string, Does.Contain(substring))
    def replace_contains(match):
        item = match.group(1)
        collection = match.group(2)
        return f'Assert.That({collection}, Does.Contain({item}))'

    content = re.sub(r'Assert\.Contains\(([^,]+),\s*([^)]+)\)', replace_contains, content)

    # FluentAssertions patterns
    content = re.sub(r'\.Should\(\)\.Be\(([^)]+)\)', r', Is.EqualTo(\1))', content)
    content = re.sub(r'\.Should\(\)\.NotBe\(([^)]+)\)', r', Is.Not.EqualTo(\1))', content)
    content = re.sub(r'\.Should\(\)\.BeTrue\(\)', r', Is.True)', content)
    content = re.sub(r'\.Should\(\)\.BeFalse\(\)', r', Is.False)', content)
    content = re.sub(r'\.Should\(\)\.BeNull\(\)', r', Is.Null)', content)
    content = re.sub(r'\.Should\(\)\.NotBeNull\(\)', r', Is.Not.Null)', content)
    content = re.sub(r'\.Should\(\)\.BeEmpty\(\)', r', Is.Empty)', content)
    content = re.sub(r'\.Should\(\)\.NotBeEmpty\(\)', r', Is.Not.Empty)', content)
    content = re.sub(r'\.Should\(\)\.HaveCount\(([^)]+)\)', r', Has.Count.EqualTo(\1))', content)
    content = re.sub(r'\.Should\(\)\.Contain\(([^)]+)\)', r', Does.Contain(\1))', content)
    content = re.sub(r'\.Should\(\)\.StartWith\(([^)]+)\)', r', Does.StartWith(\1))', content)
    content = re.sub(r'\.Should\(\)\.EndWith\(([^)]+)\)', r', Does.EndWith(\1))', content)
    content = re.sub(r'\.Should\(\)\.BeGreaterThan\(([^)]+)\)', r', Is.GreaterThan(\1))', content)
    content = re.sub(r'\.Should\(\)\.BeLessThan\(([^)]+)\)', r', Is.LessThan(\1))', content)
    content = re.sub(r'\.Should\(\)\.BeOfType<([^>]+)>\(\)', r', Is.TypeOf<\1>())', content)

    # Fix up Assert.That for fluent patterns - add Assert.That( at the beginning
    # This is a bit tricky - we need to find patterns like "something, Is.EqualTo(x))"
    # and wrap them with Assert.That(
    # For now, let's handle this manually case by case

    if content != original_content:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        return True
    return False

def main():
    """Main migration function"""
    test_files = []

    # Find all *Tests.cs files, excluding bin and obj directories
    for root, dirs, files in os.walk(TEST_DIR):
        # Skip bin and obj directories
        dirs[:] = [d for d in dirs if d not in ['bin', 'obj']]

        for file in files:
            if file.endswith('Tests.cs'):
                filepath = os.path.join(root, file)
                test_files.append(filepath)

    print(f"Found {len(test_files)} test files to migrate")

    migrated_count = 0
    for filepath in sorted(test_files):
        if migrate_file(filepath):
            migrated_count += 1

    print(f"\nMigration complete. Migrated {migrated_count} files.")

if __name__ == '__main__':
    main()
