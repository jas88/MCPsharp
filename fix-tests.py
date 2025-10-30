#!/usr/bin/env python3
"""
Batch fix script for MCPsharp test compilation errors
"""

import os
import re
import glob
from pathlib import Path

def fix_list_length_to_count(content):
    """Fix List.Length to List.Count"""
    return re.sub(r'(\w+)\.Length', r'\1.Count', content)

def fix_is_analyzer_loaded(content):
    """Fix IsAnalyzerLoaded to GetAnalyzer != null"""
    content = re.sub(r'(\w+)\.IsAnalyzerLoaded\(([^)]+)\)', r'\1.GetAnalyzer(\2) != null', content)
    content = re.sub(r'IsAnalyzerLoaded\(([^)]+)\)', r'GetAnalyzer(\1) != null', content)
    return content

def fix_error_property(content):
    """Fix result.Error to result.ErrorMessage"""
    return content.replace('.Error', '.ErrorMessage')

def add_missing_using(content, filepath):
    """Add missing using directives"""
    lines = content.split('\n')
    using_lines = [line for line in lines if line.strip().startswith('using ')]

    # Check what's needed
    needs_roslyn = 'CallerResult' in content or 'CallType' in content or 'ConfidenceLevel' in content
    needs_streaming = 'StreamProcessRequest' in content or 'StreamProcessorType' in content
    needs_analyzers = 'AnalyzerLoadResult' in content or 'IAnalyzerRegistry' in content

    has_roslyn = any('MCPsharp.Models.Roslyn' in line for line in using_lines)
    has_streaming = any('MCPsharp.Models.Streaming' in line for line in using_lines)
    has_analyzers = any('MCPsharp.Models.Analyzers' in line for line in using_lines)

    # Add missing using statements
    if needs_roslyn and not has_roslyn:
        for i, line in enumerate(lines):
            if line.strip().startswith('using MCPsharp.Models;'):
                lines.insert(i + 1, 'using MCPsharp.Models.Roslyn;')
                break

    if needs_streaming and not has_streaming:
        for i, line in enumerate(lines):
            if line.strip().startswith('using MCPsharp.Models;'):
                lines.insert(i + 1, 'using MCPsharp.Models.Streaming;')
                break

    if needs_analyzers and not has_analyzers:
        for i, line in enumerate(lines):
            if line.strip().startswith('using MCPsharp.Models;'):
                lines.insert(i + 1, 'using MCPsharp.Models.Analyzers;')
                break

    return '\n'.join(lines)

def fix_method_signature_accessibility(content):
    """Add Accessibility property to MethodSignature if missing"""
    # Pattern to find MethodSignature initializations
    pattern = r'(Name\s*=\s*"[^"]+",\s*ContainingType\s*=\s*"[^"]+",\s*ReturnType\s*=\s*"[^"]+",)'
    replacement = r'\1\n                Accessibility = "public",'
    return re.sub(pattern, replacement, content, flags=re.MULTILINE)

def fix_caller_result_properties(content):
    """Fix CallerResult property accesses"""
    content = content.replace('result.MethodName', 'result.TargetSignature.Name')
    content = content.replace('result.ContainingType', 'result.TargetSignature.ContainingType')
    return content

def fix_analyzer_registry_methods(content):
    """Fix analyzer registry method calls"""
    content = content.replace('.UnregisterAnalyzer', '.UnregisterAnalyzerAsync')
    content = content.replace('.UnloadAnalyzer', '.UnloadAnalyzerAsync')
    content = content.replace('.LoadAnalyzer', '.LoadAnalyzerAsync')
    content = content.replace('.RunAnalyzer', '.RunAnalyzerAsync')
    content = content.replace('.ValidateAnalyzer', '.ValidateAnalyzerAsync')
    return content

def fix_common_issues(content):
    """Fix other common issues"""
    # Fix SecurityValidationResult
    content = re.sub(
        r'new SecurityValidationResult\(\s*\{\s*IsValid\s*=\s*true\s*\}',
        'new SecurityValidationResult { IsValid = true, IsSigned = false, IsTrusted = false, HasMaliciousPatterns = false }',
        content
    )

    # Fix ProcessFileAsync calls that still have old signature
    content = re.sub(
        r'(\w+)\.ProcessFileAsync\(([^,]+),\s*([^)]+)\)',
        r'\1.ProcessFileAsync(\2)',
        content
    )

    return content

def main():
    print("Starting batch fix of MCPsharp test compilation errors...")

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
            content = fix_list_length_to_count(content)
            content = fix_is_analyzer_loaded(content)
            content = fix_error_property(content)
            content = add_missing_using(content, filepath)
            content = fix_method_signature_accessibility(content)
            content = fix_caller_result_properties(content)
            content = fix_analyzer_registry_methods(content)
            content = fix_common_issues(content)

            # Write back if changed
            if content != original_content:
                with open(filepath, 'w', encoding='utf-8') as f:
                    f.write(content)
                fixed_count += 1
                print(f"Fixed: {filepath}")

        except Exception as e:
            print(f"Error processing {filepath}: {e}")

    print(f"\nBatch fix completed! Fixed {fixed_count} files.")
    print("Run 'dotnet build' to see remaining errors.")

if __name__ == "__main__":
    main()