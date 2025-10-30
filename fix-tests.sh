#!/bin/bash
# Batch fix script for MCPsharp test compilation errors

echo "Starting batch fix of MCPsharp test compilation errors..."

# Fix 1: List.Length -> List.Count
echo "Fixing List.Length to List.Count..."
find tests -name "*.cs" -type f -exec sed -i '' 's/\([a-zA-Z_][a-zA-Z0-9_]*\)\.Length/\1.Count/g' {} \;

# Fix 2: Update method calls that no longer exist
echo "Updating deprecated method calls..."
find tests -name "*.cs" -type f -exec sed -i '' 's/IsAnalyzerLoaded([^)]*)/GetAnalyzer(\1) != null/g' {} \;
find tests -name "*.cs" -type f -exec sed -i '' 's/\.Error/.ErrorMessage/g' {} \;

# Fix 3: Add Accessibility to MethodSignature where missing
echo "Adding Accessibility property to MethodSignature..."
find tests -name "*.cs" -type f -exec sed -i '' 's/Name = "\([^"]*\)",[[:space:]]*ContainingType = "\([^"]*\)",[[:space:]]*ReturnType = "\([^"]*\)",/&\
                Accessibility = "public",/g' {} \;

echo "Batch fix completed!"
echo "Run 'dotnet build' to see remaining errors."