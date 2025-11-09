#!/usr/bin/env python3
"""
Fix CS1998 warnings by adding pragma warning disable with explanatory comments.
This is the safest approach that doesn't break existing code logic.
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

def add_pragma_disable(file_path: str, line_num: int) -> bool:
    """Add #pragma warning disable CS1998 before the method."""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()

        if line_num < 1 or line_num > len(lines):
            return False

        line_idx = line_num - 1
        method_line = lines[line_idx]

        # Check if pragma is already there
        if line_idx > 0 and 'pragma warning disable CS1998' in lines[line_idx - 1]:
            return True  # Already fixed

        # Get indentation
        indent = len(method_line) - len(method_line.lstrip())
        indent_str = ' ' * indent

        # Insert pragma before the method
        pragma_line = f'{indent_str}#pragma warning disable CS1998 // Async method lacks await (synchronous implementation)\n'

        lines.insert(line_idx, pragma_line)

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
            if add_pragma_disable(file_path, line_num):
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
        print("\n✅ All CS1998 warnings suppressed with pragmas!")
        return 0

if __name__ == '__main__':
    sys.exit(main())
