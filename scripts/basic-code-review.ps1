# Basic Code Review Script for Baketa (Fallback for Gemini API)
param(
    [string]$Target = "all",
    [switch]$Detailed
)

Write-Host "=== Baketa Code Review (Gemini Fallback) ===" -ForegroundColor Green
Write-Host "Target: $Target" -ForegroundColor Cyan

# Initialize issues collection
$Issues = New-Object System.Collections.ArrayList

function Add-Issue {
    param($File, $Line, $Severity, $Category, $Description)
    $issue = [PSCustomObject]@{
        File = $File
        Line = $Line
        Severity = $Severity
        Category = $Category
        Description = $Description
    }
    [void]$Issues.Add($issue)
}

# 1. Find C# files
Write-Host "`n📁 Scanning C# files..." -ForegroundColor Yellow
try {
    $csFiles = Get-ChildItem -Path . -Recurse -Filter "*.cs" -ErrorAction SilentlyContinue | 
               Where-Object { $_.FullName -notmatch "\\bin\\|\\obj\\|\\packages\\" }
    
    Write-Host "✅ Found $($csFiles.Count) C# files" -ForegroundColor Green
    
} catch {
    Write-Host "❌ Failed to scan files: $_" -ForegroundColor Red
    exit 1
}

# 2. Check for modern C# features
Write-Host "`n🔍 Checking modern C# usage..." -ForegroundColor Yellow
$modernIssues = 0

foreach ($file in $csFiles | Select-Object -First 50) {  # Limit for performance
    try {
        $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
        if ($content) {
            # Check for old namespace syntax
            if ($content -match "namespace\s+[\w\.]+\s*\{") {
                $modernIssues++
                Add-Issue -File $file.Name -Line "1" -Severity "Info" -Category "Modern C#" `
                    -Description "Consider file-scoped namespace (C# 12)"
            }
        }
    } catch {
        # Skip problematic files
        continue
    }
}

if ($modernIssues -eq 0) {
    Write-Host "✅ Modern C# features usage looks good" -ForegroundColor Green
} else {
    Write-Host "ℹ️ Found $modernIssues modernization opportunities" -ForegroundColor Cyan
}

# 3. Basic architecture check
Write-Host "`n🏗️ Basic architecture check..." -ForegroundColor Yellow
$archIssues = 0

# Check for common architectural issues
foreach ($file in $csFiles | Select-Object -First 30) {
    try {
        $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
        if ($content) {
            # Check for direct UI references in Core layer
            if ($file.FullName -match "Baketa\.Core" -and $content -match "using.*\.UI") {
                $archIssues++
                Add-Issue -File $file.Name -Line "N/A" -Severity "Warning" -Category "Architecture" `
                    -Description "Core layer should not reference UI layer"
            }
        }
    } catch {
        continue
    }
}

if ($archIssues -eq 0) {
    Write-Host "✅ Basic architecture looks good" -ForegroundColor Green
} else {
    Write-Host "⚠️ Found $archIssues potential architecture issues" -ForegroundColor Yellow
}

# 4. Summary
Write-Host "`n📊 Review Summary" -ForegroundColor Cyan
Write-Host "Total Issues: $($Issues.Count)" -ForegroundColor White

if ($Issues.Count -gt 0) {
    $groupedIssues = $Issues | Group-Object Severity
    foreach ($group in $groupedIssues) {
        $color = switch ($group.Name) {
            "Error" { "Red" }
            "Warning" { "Yellow" }
            "Info" { "Cyan" }
            default { "White" }
        }
        Write-Host "- $($group.Name): $($group.Count)" -ForegroundColor $color
    }
    
    if ($Detailed) {
        Write-Host "`n📋 Detailed Issues:" -ForegroundColor White
        foreach ($issue in $Issues) {
            $color = switch ($issue.Severity) {
                "Error" { "Red" }
                "Warning" { "Yellow" }
                "Info" { "Cyan" }
                default { "White" }
            }
            Write-Host "  [$($issue.Severity)] $($issue.File) - $($issue.Description)" -ForegroundColor $color
        }
    }
} else {
    Write-Host "🎉 No issues found in quick scan!" -ForegroundColor Green
}

Write-Host "`n💡 This is a basic fallback review when Gemini API is unavailable." -ForegroundColor Gray
Write-Host "For comprehensive review, use @Code-Reviewer agent when Gemini is working." -ForegroundColor Gray

Write-Host "`n使用方法:" -ForegroundColor Cyan
Write-Host "  .\basic-code-review.ps1              # 基本チェック" -ForegroundColor White
Write-Host "  .\basic-code-review.ps1 -Detailed    # 詳細表示" -ForegroundColor White