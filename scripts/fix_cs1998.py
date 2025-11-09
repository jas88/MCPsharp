#!/usr/bin/env python3
"""
Automated CS1998 warning fixer.
Removes 'async' modifier from methods that don't use 'await'.
Uses Task.FromResult() for methods that need to maintain Task<T> interface.
"""

import re
import subprocess
import sys
from pathlib import Path
from typing import Dict, List, Tuple

def get_cs1998_warnings() -> List[Tuple[str, int]]:
    """Get all CS1998 warnings from build output."""
    result = subprocess.run(
        ['dotnet', 'build'],
        capture_output=True,
        text=True,
        cwd=Path(__file__).parent.parent
    )

    output = result.stdout + result.stderr
    warnings = []
    pattern = r'(.+\.cs)\((\d+),\d+\): warning CS1998:'

    for match in re.finditer(pattern, output):
        file_path = match.group(1)
        line_num = int(match.group(2))
        if (file_path, line_num) not in warnings:
            warnings.append((file_path, line_num))

    return warnings

def find_method_body(lines: List[str], start_idx: int) -> Tuple[int, int, List[str]]:
    """Find the method body boundaries and return statements."""
    brace_count = 0
    found_brace = False
    end_idx = start_idx
    return_statements = []

    for i in range(start_idx, min(start_idx + 200, len(lines))):
        line = lines[i]

        if '{' in line:
            found_brace = True
            brace_count += line.count('{')

        if '}' in line:
            brace_count -= line.count('}')

        if found_brace and brace_count > 0:
            # Look for return statements
            return_match = re.search(r'\breturn\s+(.+?);', line)
            if return_match:
                return_value = return_match.group(1).strip()
                if return_value:  # Not just "return;"
                    return_statements.append((i, return_value))

        if found_brace and brace_count == 0:
            end_idx = i
            break

    return start_idx, end_idx, return_statements

def fix_async_method(file_path: str, line_num: int) -> bool:
    """
    Fix a single async method by removing async and wrapping returns with Task.FromResult().
    Returns True if fixed, False if manual intervention needed.
    """
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()

        if line_num < 1 or line_num > len(lines):
            return False

        line_idx = line_num - 1
        original_line = lines[line_idx]

        if 'async' not in original_line:
            return False

        # Remove async keyword
        fixed_line = re.sub(r'\basync\s+', '', original_line)

        # Determine if this returns Task<T> or Task
        task_match = re.search(r'Task<(.+?)>', original_line)
        value_task_match = re.search(r'ValueTask<(.+?)>', original_line)
        returns_task_t = task_match or value_task_match
        returns_plain_task = 'Task ' in original_line and not returns_task_t

        # Find method body and return statements
        _, end_idx, return_statements = find_method_body(lines, line_idx)

        # Update the method signature
        lines[line_idx] = fixed_line

        # If returns Task<T>, wrap all return values with Task.FromResult()
        if returns_task_t:
            for ret_idx, ret_value in reversed(return_statements):
                if 'Task.FromResult' not in lines[ret_idx]:
                    old_return = lines[ret_idx]
                    new_return = re.sub(
                        r'\breturn\s+(.+?);',
                        r'return Task.FromResult(\1);',
                        old_return
                    )
                    lines[ret_idx] = new_return

        # If returns plain Task with no return value, convert "return;" to "return Task.CompletedTask;"
        elif returns_plain_task:
            for ret_idx, ret_value in reversed(return_statements):
                old_return = lines[ret_idx]
                if re.match(r'\s*return\s*;', old_return):
                    new_return = re.sub(r'\breturn\s*;', 'return Task.CompletedTask;', old_return)
                    lines[ret_idx] = new_return

        with open(file_path, 'w', encoding='utf-8') as f:
            f.writelines(lines)

        return True

    except Exception as e:
        print(f"  ✗ Error: {e}")
        return False

def main():
    print("🔍 Finding CS1998 warnings...")
    warnings = get_cs1998_warnings()

    if not warnings:
        print("✅ No CS1998 warnings found!")
        return 0

    print(f"\n📊 Found {len(warnings)} CS1998 warnings\n")

    # Group by file
    files_dict: Dict[str, List[int]] = {}
    for file_path, line_num in warnings:
        if file_path not in files_dict:
            files_dict[file_path] = []
        files_dict[file_path].append(line_num)

    # Sort line numbers in reverse order (fix from bottom to top to preserve line numbers)
    for file_path in files_dict:
        files_dict[file_path].sort(reverse=True)

    # Fix each file
    total_fixed = 0
    total_failed = 0

    for file_path, line_numbers in sorted(files_dict.items()):
        try:
            rel_path = str(Path(file_path).relative_to(Path(__file__).parent.parent))
        except ValueError:
            rel_path = file_path
        print(f"\n📝 {rel_path} ({len(line_numbers)} warnings)")

        for line_num in line_numbers:
            if fix_async_method(file_path, line_num):
                total_fixed += 1
                print(f"  ✓ Line {line_num}")
            else:
                total_failed += 1
                print(f"  ✗ Line {line_num}")

    print(f"\n{'='*60}")
    print(f"✅ Fixed: {total_fixed}")
    print(f"⚠️  Failed: {total_failed}")
    print(f"{'='*60}\n")

    # Rebuild to check remaining warnings
    print("🔨 Rebuilding to verify...")
    remaining = get_cs1998_warnings()

    if remaining:
        print(f"\n⚠️  Still {len(remaining)} CS1998 warnings remaining")
        return 1
    else:
        print("\n✅ All CS1998 warnings resolved!")
        return 0

if __name__ == '__main__':
    sys.exit(main())
