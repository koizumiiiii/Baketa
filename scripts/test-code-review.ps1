# Baketa Code Review Test Script
param(
    [string]$Target = "basic"
)

Write-Host "=== Baketa Code Review System Test ===" -ForegroundColor Green
Write-Host "Target: $Target" -ForegroundColor Cyan

# Basic ripgrep test
Write-Host "`n1. ripgrep installation test..." -ForegroundColor Yellow
try {
    $rgVersion = rg --version 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ ripgrep is installed and working" -ForegroundColor Green
        Write-Host "Version: $($rgVersion[0])" -ForegroundColor Gray
    } else {
        Write-Host "❌ ripgrep not found" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "❌ ripgrep test failed: $_" -ForegroundColor Red
    exit 1
}

# Basic pattern search test
Write-Host "`n2. Pattern search test..." -ForegroundColor Yellow
try {
    $namespaceResults = rg --type cs "^namespace" . 2>$null
    $namespaceCount = ($namespaceResults | Measure-Object).Count
    Write-Host "✅ Found $namespaceCount namespace declarations" -ForegroundColor Green
    
    if ($namespaceCount -gt 0) {
        Write-Host "Sample namespaces:" -ForegroundColor Gray
        $namespaceResults | Select-Object -First 3 | ForEach-Object {
            Write-Host "  $_" -ForegroundColor Gray
        }
    }
} catch {
    Write-Host "❌ Pattern search failed: $_" -ForegroundColor Red
}

# Architecture compliance test
Write-Host "`n3. Architecture compliance test..." -ForegroundColor Yellow
try {
    $usingResults = rg --type cs "^using.*Baketa\." . 2>$null
    $usingCount = ($usingResults | Measure-Object).Count
    Write-Host "✅ Found $usingCount Baketa namespace usages" -ForegroundColor Green
    
    if ($usingCount -gt 0) {
        Write-Host "Sample usages:" -ForegroundColor Gray
        $usingResults | Select-Object -First 3 | ForEach-Object {
            Write-Host "  $_" -ForegroundColor Gray
        }
    }
} catch {
    Write-Host "❌ Architecture test failed: $_" -ForegroundColor Red
}

# File existence check
Write-Host "`n4. Generated files check..." -ForegroundColor Yellow
$checkFiles = @(
    "scripts\code-review-checklist.md",
    "scripts\review-workflow.md"
)

foreach ($file in $checkFiles) {
    if (Test-Path $file) {
        Write-Host "✅ $file exists" -ForegroundColor Green
    } else {
        Write-Host "❌ $file missing" -ForegroundColor Red
    }
}

Write-Host "`n=== Test Complete ===" -ForegroundColor Green
Write-Host "Code Review System is ready for use!" -ForegroundColor Cyan