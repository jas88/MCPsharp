#!/usr/bin/env python3
"""
Bulk fix CS1998 warnings by removing async and using Task.FromResult.
"""

import re
import subprocess
from pathlib import Path
from collections import defaultdict

def get_cs1998_warnings():
    """Get all CS1998 warnings."""
    result = subprocess.run(
        ['dotnet', 'build', '--no-incremental'],
        capture_output=True,
        text=True,
        cwd='/Users/jas88/Developer/Github/MCPsharp'
    )

    warnings = []
    pattern = r'(.+\.cs)\((\d+),(\d+)\): warning CS1998:'

    output = result.stdout + result.stderr
    for line in output.split('\n'):
        match = re.search(pattern, line)
        if match:
            file_path = match.group(1)
            line_num = int(match.group(2))
            if (file_path, line_num) not in warnings:  # Deduplicate
                warnings.append((file_path, line_num))

    return warnings

def fix_file(file_path, line_nums):
    """Fix all async methods in a file."""
    with open(file_path, 'r') as f:
        lines = f.readlines()

    # Sort line numbers in reverse to maintain indices
    for line_num in sorted(set(line_nums), reverse=True):
        idx = line_num - 1
        if idx < 0 or idx >= len(lines):
            continue

        line = lines[idx]

        # Remove 'async ' from method signature
        if 'async ' in line:
            # Pattern: public/private/protected/internal async Task
            lines[idx] = re.sub(r'\basync\s+', '', line)

    # Write back
    with open(file_path, 'w') as f:
        f.writelines(lines)

def find_return_statements(file_path):
    """Find and fix return statements to use Task.FromResult."""
    with open(file_path, 'r') as f:
        content = f.read()

    # This is a simplified approach - may need manual review
    # Look for patterns like:
    # return value;  ->  return Task.FromResult(value);
    # return new ...;  ->  return Task.FromResult(new ...);

    # We'll do this conservatively - only wrap simple returns
    # More complex cases will need manual review

    return content

def main():
    print("Getting CS1998 warnings...")
    warnings = get_cs1998_warnings()
    unique_warnings = list(set(warnings))
    print(f"Found {len(unique_warnings)} unique warnings")

    # Group by file
    by_file = defaultdict(list)
    for file_path, line_num in unique_warnings:
        by_file[file_path].append(line_num)

    print(f"Across {len(by_file)} files\n")

    # Process each file
    fixed_count = 0
    for file_path, line_nums in sorted(by_file.items(), key=lambda x: -len(x[1])):
        file_name = Path(file_path).name
        print(f"Fixing {file_name}: {len(line_nums)} methods", end=' ... ')

        try:
            fix_file(file_path, line_nums)
            fixed_count += len(line_nums)
            print("✓")
        except Exception as e:
            print(f"✗ Error: {e}")

    print(f"\nRemoved 'async' from {fixed_count} methods")
    print("\nNOTE: You must manually add Task.FromResult() to return statements!")
    print("Rebuild to see remaining work.")

if __name__ == '__main__':
    main()
