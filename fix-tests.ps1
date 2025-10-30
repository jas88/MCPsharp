#!/usr/bin/env pwsh
# Batch fix script for MCPsharp test compilation errors

Write-Host "Starting batch fix of MCPsharp test compilation errors..." -ForegroundColor Green

# Fix 1: List.Length -> List.Count
Write-Host "Fixing List.Length to List.Count..." -ForegroundColor Yellow
Get-ChildItem -Path "tests" -Filter "*.cs" -Recurse | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $content = $content -replace '(\w+)\.Length', '$1.Count'
    Set-Content $_.FullName $content -NoNewline
}

# Fix 2: Add missing using directives
Write-Host "Adding missing using directives..." -ForegroundColor Yellow
$files = Get-ChildItem -Path "tests" -Filter "*.cs" -Recurse
foreach ($file in $files) {
    $content = Get-Content $file.FullName
    $hasModels = $content -match "using MCPsharp.Models;"
    $hasRoslynModels = $content -match "using MCPsharp.Models.Roslyn;"
    $hasStreamingModels = $content -match "using MCPsharp.Models.Streaming;"
    $hasAnalyzersModels = $content -match "using MCPsharp.Models.Analyzers;"

    $needsUpdate = $false

    if ((Select-String -Path $file.FullName -Pattern "CallerResult|CallType|ConfidenceLevel" -Quiet) -and !$hasRoslynModels) {
        $content = $content -replace "using MCPsharp.Models;", "using MCPsharp.Models;`nusing MCPsharp.Models.Roslyn;"
        $needsUpdate = $true
    }

    if ((Select-String -Path $file.FullName -Pattern "StreamProcessRequest|StreamProcessorType" -Quiet) -and !$hasStreamingModels) {
        $content = $content -replace "using MCPsharp.Models;", "using MCPsharp.Models;`nusing MCPsharp.Models.Streaming;"
        $needsUpdate = $true
    }

    if ($needsUpdate) {
        Set-Content $file.FullName $content
    }
}

# Fix 3: Update common method calls
Write-Host "Updating common method calls..." -ForegroundColor Yellow

# Fix IsAnalyzerLoaded -> GetAnalyzer != null
Get-ChildItem -Path "tests" -Filter "*.cs" -Recurse | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $content = $content -replace '(\w+)\.IsAnalyzerLoaded\(([^)]+)\)', '$1.GetAnalyzer($2) != null'
    $content = $content -replace 'IsAnalyzerLoaded\(([^)]+)\)', 'GetAnalyzer($1) != null'
    Set-Content $_.FullName $content -NoNewline
}

# Fix 4: Update result.Error checks
Write-Host "Updating error checks..." -ForegroundColor Yellow
Get-ChildItem -Path "tests" -Filter "*.cs" -Recurse | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $content = $content -replace 'result\.Error', 'result.ErrorMessage'
    $content = $content -replace 'result\.Analyzer', 'result.Success'
    Set-Content $_.FullName $content -NoNewline
}

# Fix 5: Add required properties to MethodSignature
Write-Host "Adding required properties to MethodSignature..." -ForegroundColor Yellow
Get-ChildItem -Path "tests" -Filter "*.cs" -Recurse | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    # Add Accessibility property if missing
    $content = $content -replace '(Name\s*=\s*"[^"]+",[^}]+?ContainingType\s*=\s*"[^"]+",[^}]+?ReturnType\s*=\s*"[^"]+",)([^}]*})', '$1 Accessibility = "public",$2'
    Set-Content $_.FullName $content -NoNewline
}

# Fix 6: Update CallerResult usage
Write-Host "Updating CallerResult usage..." -ForegroundColor Yellow
Get-ChildItem -Path "tests" -Filter "*.cs" -Recurse | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $content = $content -replace 'result\.MethodName', 'result.TargetSignature.Name'
    $content = $content -replace 'result\.ContainingType', 'result.TargetSignature.ContainingType'
    Set-Content $_.FullName $content -NoNewline
}

Write-Host "Batch fix completed!" -ForegroundColor Green
Write-Host "Run 'dotnet build' to see remaining errors." -ForegroundColor Cyan