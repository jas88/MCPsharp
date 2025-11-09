#!/usr/bin/env python3
"""
Script to analyze and fix CS1998 async/await warnings.
Identifies methods that should be synchronous and converts them.
"""

import re
import subprocess
import sys
from pathlib import Path
from collections import defaultdict

def get_cs1998_warnings():
    """Get all CS1998 warnings from build output."""
    result = subprocess.run(
        ['dotnet', 'build', '--no-incremental'],
        capture_output=True,
        text=True,
        cwd='/Users/jas88/Developer/Github/MCPsharp'
    )

    warnings = []
    pattern = r'(.+\.cs)\((\d+),(\d+)\): warning CS1998:'

    # Check both stdout and stderr
    output = result.stdout + result.stderr
    for line in output.split('\n'):
        match = re.search(pattern, line)
        if match:
            file_path = match.group(1)
            line_num = int(match.group(2))
            warnings.append((file_path, line_num))

    return warnings

def analyze_method(file_path, line_num):
    """Check if a method actually uses await."""
    try:
        with open(file_path, 'r') as f:
            lines = f.readlines()

        # Find method start
        start_line = line_num - 1
        if start_line < 0 or start_line >= len(lines):
            return None

        # Extract method signature
        method_line = lines[start_line].strip()

        # Find method body boundaries
        brace_count = 0
        in_method = False
        method_body = []

        for i in range(start_line, min(len(lines), start_line + 200)):
            line = lines[i]
            method_body.append(line)

            for char in line:
                if char == '{':
                    brace_count += 1
                    in_method = True
                elif char == '}':
                    brace_count -= 1
                    if in_method and brace_count == 0:
                        # Method ended
                        method_text = ''.join(method_body)
                        has_await = 'await ' in method_text
                        return {
                            'file': file_path,
                            'line': line_num,
                            'signature': method_line,
                            'has_await': has_await,
                            'body_lines': len(method_body)
                        }

        return None
    except Exception as e:
        print(f"Error analyzing {file_path}:{line_num}: {e}")
        return None

def group_by_file(warnings):
    """Group warnings by file."""
    by_file = defaultdict(list)
    for file_path, line_num in warnings:
        by_file[file_path].append(line_num)
    return dict(by_file)

def main():
    print("Analyzing CS1998 warnings...")
    warnings = get_cs1998_warnings()
    print(f"Found {len(warnings)} warnings")

    # Group by file
    by_file = group_by_file(warnings)
    print(f"Across {len(by_file)} files")

    # Analyze each warning
    can_fix = []
    needs_await = []

    for file_path, line_nums in sorted(by_file.items(), key=lambda x: -len(x[1])):
        file_name = Path(file_path).name
        print(f"\n{file_name}: {len(line_nums)} warnings")

        for line_num in line_nums[:3]:  # Sample first 3
            result = analyze_method(file_path, line_num)
            if result:
                if result['has_await']:
                    needs_await.append(result)
                    print(f"  Line {line_num}: HAS await - needs investigation")
                else:
                    can_fix.append(result)
                    print(f"  Line {line_num}: NO await - can remove async")

    print(f"\n\nSummary:")
    print(f"  Can auto-fix (no await): {len(can_fix)}")
    print(f"  Needs investigation (has await): {len(needs_await)}")

    # Show files with most auto-fixable methods
    print(f"\nTop files for auto-fix:")
    fix_by_file = defaultdict(int)
    for item in can_fix:
        fix_by_file[Path(item['file']).name] += 1

    for file_name, count in sorted(fix_by_file.items(), key=lambda x: -x[1])[:10]:
        print(f"  {file_name}: {count}")

if __name__ == '__main__':
    main()
