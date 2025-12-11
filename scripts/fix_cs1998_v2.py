#!/usr/bin/env python3
"""
Automated CS1998 warning fixer - Version 2.
Properly removes 'async' modifier and wraps ALL return values with Task.FromResult().
Handles multi-line return statements correctly.
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

def find_method_end(lines: List[str], start_idx: int) -> int:
    """Find the ending brace of a method."""
    brace_count = 0
    found_start = False

    for i in range(start_idx, len(lines)):
        line = lines[i]
        for char in line:
            if char == '{':
                brace_count += 1
                found_start = True
            elif char == '}':
                brace_count -= 1
                if found_start and brace_count == 0:
                    return i

    return len(lines) - 1

def fix_async_method(file_path: str, line_num: int) -> bool:
    """
    Fix async method by removing 'async' and properly handling return statements.
    """
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()

        lines = content.splitlines(keepends=True)

        if line_num < 1 or line_num > len(lines):
            return False

        line_idx = line_num - 1
        original_line = lines[line_idx]

        if 'async' not in original_line:
            return False

        # Remove async keyword from method signature
        lines[line_idx] = re.sub(r'\basync\s+', '', original_line)

        # Determine return type
        task_match = re.search(r'Task<(.+?)>', lines[line_idx])
        value_task_match = re.search(r'ValueTask<(.+?)>', lines[line_idx])
        returns_task_t = task_match or value_task_match
        returns_plain_task = 'Task ' in lines[line_idx] and not returns_task_t

        # Find method end
        method_end = find_method_end(lines, line_idx)

        # Join all lines in method body into single string for processing
        method_body = ''.join(lines[line_idx:method_end + 1])

        if returns_task_t:
            # Wrap all return statements with Task.FromResult()
            # Use a proper state machine to handle strings, chars, and nested braces
            new_body = []
            i = 0
            while i < len(method_body):
                # Check if we're at a return statement
                if method_body[i:i+6] == 'return' and (i == 0 or not method_body[i-1].isalnum()):
                    # Check if next char is whitespace
                    if i + 6 < len(method_body) and method_body[i+6].isspace():
                        # Found a return statement - need to find the full expression
                        return_start = i
                        i += 6

                        # Skip whitespace
                        while i < len(method_body) and method_body[i].isspace():
                            i += 1

                        # Find the semicolon that ends this return statement
                        # Handle strings, chars, and nested parens/braces/brackets
                        value_start = i
                        in_string = False
                        in_char = False
                        in_verbatim = False
                        escape_next = False
                        paren_depth = 0
                        brace_depth = 0
                        bracket_depth = 0

                        while i < len(method_body):
                            char = method_body[i]

                            if escape_next:
                                escape_next = False
                                i += 1
                                continue

                            if in_verbatim:
                                if char == '"':
                                    if i + 1 < len(method_body) and method_body[i+1] == '"':
                                        i += 2  # Skip escaped quote in verbatim string
                                        continue
                                    else:
                                        in_verbatim = False
                                i += 1
                                continue

                            if char == '\\' and (in_string or in_char):
                                escape_next = True
                                i += 1
                                continue

                            if char == '@' and i + 1 < len(method_body) and method_body[i+1] == '"' and not in_string and not in_char:
                                in_verbatim = True
                                i += 2
                                continue

                            if char == '"' and not in_char:
                                in_string = not in_string
                                i += 1
                                continue

                            if char == "'" and not in_string:
                                in_char = not in_char
                                i += 1
                                continue

                            if not in_string and not in_char:
                                if char == '(':
                                    paren_depth += 1
                                elif char == ')':
                                    paren_depth -= 1
                                elif char == '{':
                                    brace_depth += 1
                                elif char == '}':
                                    brace_depth -= 1
                                elif char == '[':
                                    bracket_depth += 1
                                elif char == ']':
                                    bracket_depth -= 1
                                elif char == ';' and paren_depth == 0 and brace_depth == 0 and bracket_depth == 0:
                                    # Found the end of the return statement
                                    return_value = method_body[value_start:i].strip()

                                    # Check if already wrapped
                                    if 'Task.FromResult' not in return_value:
                                        new_body.append(f'return Task.FromResult({return_value});')
                                    else:
                                        new_body.append(method_body[return_start:i+1])

                                    i += 1
                                    break

                            i += 1
                        continue

                new_body.append(method_body[i])
                i += 1

            method_body = ''.join(new_body)

        elif returns_plain_task:
            # Replace bare return with Task.CompletedTask
            method_body = re.sub(
                r'\breturn\s*;',
                'return Task.CompletedTask;',
                method_body
            )

        # Split back into lines
        new_lines = method_body.splitlines(keepends=True)
        lines[line_idx:method_end + 1] = new_lines

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

    # Fix each file
    total_fixed = 0
    total_failed = 0

    for file_path in sorted(files_dict.keys()):
        line_numbers = files_dict[file_path]
        try:
            rel_path = str(Path(file_path).relative_to(Path(__file__).parent.parent))
        except ValueError:
            rel_path = file_path
        print(f"\n📝 {rel_path} ({len(line_numbers)} warnings)")

        # Process warnings in reverse order (bottom to top) to avoid line number shifts
        # This way, fixing a method lower in the file won't affect line numbers above it
        for line_num in sorted(line_numbers, reverse=True):
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

    # Final rebuild
    print("🔨 Final rebuild...")
    remaining = get_cs1998_warnings()

    if remaining:
        print(f"\n⚠️  Still {len(remaining)} CS1998 warnings remaining")
        return 1
    else:
        print("\n✅ All CS1998 warnings resolved!")
        return 0

if __name__ == '__main__':
    sys.exit(main())
