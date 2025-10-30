#!/usr/bin/env python3
"""
Fix issues introduced by the first batch fix script
"""

import os
import re
import glob
from pathlib import Path

def fix_duplicate_accessibility(content):
    """Fix duplicate Accessibility properties"""
    # Replace duplicate Accessibility initializations
    pattern = r'Accessibility\s*=\s*"public",\s*Accessibility\s*=\s*"public",'
    replacement = 'Accessibility = "public",'
    return re.sub(pattern, replacement, content)

def fix_async_suffix_issues(content):
    """Fix double Async suffix issues"""
    content = content.replace('LoadAnalyzerAsyncAsync', 'LoadAnalyzerAsync')
    content = content.replace('UnloadAnalyzerAsyncAsync', 'UnloadAnalyzerAsync')
    content = content.replace('RunAnalyzerAsyncAsync', 'RunAnalyzerAsync')
    content = content.replace('ValidateAnalyzerAsyncAsync', 'ValidateAnalyzerAsync')
    content = content.replace('UnregisterAnalyzerAsyncAsync', 'UnregisterAnalyzerAsync')
    return content

def fix_error_message_message(content):
    """Fix ErrorMessageMessage typo"""
    return content.replace('ErrorMessageMessage', 'ErrorMessage')

def fix_version_property(content):
    """Fix Version property to return System.Version"""
    pattern = r'(\.Version\s*\)\s*\.Returns\("([^"]+)"\)'
    replacement = r'\1.Returns(new Version("\2"))'
    return re.sub(pattern, replacement, content)

def main():
    print("Fixing issues from batch fix...")

    # Find all C# test files
    test_files = []
    for pattern in ['tests/**/*.cs', 'tests\\**\\*.cs']:
        test_files.extend(glob.glob(pattern, recursive=True))

    print(f"Found {len(test_files)} test files")

    fixed_count = 0

    for filepath in test_files:
        try:
            with open(filepath, 'r', encoding='utf-8') as f:
                content = f.read()

            original_content = content

            # Apply fixes
            content = fix_duplicate_accessibility(content)
            content = fix_async_suffix_issues(content)
            content = fix_error_message_message(content)
            content = fix_version_property(content)

            # Write back if changed
            if content != original_content:
                with open(filepath, 'w', encoding='utf-8') as f:
                    f.write(content)
                fixed_count += 1
                print(f"Fixed: {filepath}")

        except Exception as e:
            print(f"Error processing {filepath}: {e}")

    print(f"\nFixed issues in {fixed_count} files.")

if __name__ == "__main__":
    main()