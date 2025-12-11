#!/usr/bin/env python3
"""
Script to fix Assert.That patterns after initial migration
"""

import os
import re
from pathlib import Path

TEST_DIR = "/Users/jas88/Developer/Github/MCPsharp/tests/MCPsharp.Tests"

def fix_file(filepath):
    """Fix Assert.That patterns in a migrated test file"""
    print(f"Fixing: {filepath}")

    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    original_content = content

    # Fix patterns like "variable, Is.Something);" -> "Assert.That(variable, Is.Something);"
    # This regex looks for lines that have a variable followed by a comma and Is./Does./Has.
    # and are missing Assert.That(

    # Pattern 1: Lines starting with whitespace + identifier + comma + constraint
    content = re.sub(
        r'(\s+)([a-zA-Z_][a-zA-Z0-9_.!?]*)\s*,\s*(Is\.|Does\.|Has\.)',
        r'\1Assert.That(\2, \3',
        content
    )

    # Fix unclosed parentheses - add missing closing paren before semicolon
    # Look for Assert.That( ... ) patterns that might be missing closing parens
    # This is tricky, so let's be conservative

    if content != original_content:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        return True
    return False

def main():
    """Main fix function"""
    test_files = []

    # Find all *Tests.cs files, excluding bin and obj directories
    for root, dirs, files in os.walk(TEST_DIR):
        # Skip bin and obj directories
        dirs[:] = [d for d in dirs if d not in ['bin', 'obj']]

        for file in files:
            if file.endswith('Tests.cs'):
                filepath = os.path.join(root, file)
                test_files.append(filepath)

    print(f"Found {len(test_files)} test files to fix")

    fixed_count = 0
    for filepath in sorted(test_files):
        if fix_file(filepath):
            fixed_count += 1

    print(f"\nFix complete. Fixed {fixed_count} files.")

if __name__ == '__main__':
    main()
