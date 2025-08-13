# Simple Code Review Script for Baketa (without ripgrep dependency)
param(
    [string]$Target = "all",
    [switch]$Detailed
)

Write-Host "=== Baketa Simple Code Review ===" -ForegroundColor Green
Write-Host "Target: $Target" -ForegroundColor Cyan

$Issues = @()

function Add-Issue {
    param($File, $Line, $Severity, $Category, $Description)
    $issue = [PSCustomObject]@{
        File = $File
        Line = $Line
        Severity = $Severity
        Category = $Category
        Description = $Description
    }
    $global:Issues = $global:Issues + $issue
}

# 1. Basic Architecture Check
Write-Host "`n🏗️ Architecture Check..." -ForegroundColor Yellow
try {
    # Find all C# files
    $csFiles = Get-ChildItem -Path . -Recurse -Filter "*.cs" | Where-Object { 
        $_.FullName -notmatch "\\bin\\|\\obj\\|\\packages\\" 
    }
    
    Write-Host "Found $($csFiles.Count) C# files" -ForegroundColor Gray
    
    # Check for file-scoped namespaces
    $oldNamespaceCount = 0
    foreach ($file in $csFiles) {
        $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
        if ($content -and $content -match "namespace\s+\w+\s*\{") {
            $oldNamespaceCount++
            Add-Issue -File $file.Name -Line "1" -Severity "Info" -Category "Modern C#" `
                -Description "Consider using file-scoped namespace (C# 12 feature)"
        }
    }
    
    if ($oldNamespaceCount -gt 0) {
        Write-Host "⚠️ Found $oldNamespaceCount files using old namespace syntax" -ForegroundColor Yellow
    } else {
        Write-Host "✅ All files use modern namespace syntax" -ForegroundColor Green
    }
    
} catch {
    Write-Host "❌ Architecture check failed: $_" -ForegroundColor Red
}

# 2. Async/Await Check
Write-Host "`n⚡ Async/Await Check..." -ForegroundColor Yellow
try {
    $configureAwaitIssues = 0
    foreach ($file in $csFiles) {
        $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
        if ($content) {
            # Check for await without ConfigureAwait(false) in non-test files
            if ($file.Name -notmatch "Test" -and $content -match "await\s+\w+.*(?<!ConfigureAwait\(false\))") {
                $configureAwaitIssues++
                Add-Issue -File $file.Name -Line "N/A" -Severity "Warning" -Category "Async" `
                    -Description "Consider using ConfigureAwait(false) in library code"
            }
        }
    }
    
    if ($configureAwaitIssues -eq 0) {
        Write-Host "✅ Async/await patterns look good" -ForegroundColor Green
    } else {
        Write-Host "⚠️ Found $configureAwaitIssues potential ConfigureAwait issues" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "❌ Async check failed: $_" -ForegroundColor Red
}

# 3. Baketa-specific checks
if ($Target -eq "all" -or $Target -eq "baketa") {
    Write-Host "`n🎮 Baketa-specific Check..." -ForegroundColor Yellow
    try {
        # Check for ViewModelBase inheritance in UI layer
        $uiFiles = $csFiles | Where-Object { $_.FullName -match "Baketa\.UI" }
        $viewModelIssues = 0
        
        foreach ($file in $uiFiles) {
            $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
            if ($content -and $file.Name -match "ViewModel\.cs$" -and $content -notmatch "ViewModelBase") {
                $viewModelIssues++
                Add-Issue -File $file.Name -Line "N/A" -Severity "Warning" -Category "UI Pattern" `
                    -Description "ViewModel should inherit from ViewModelBase"
            }
        }
        
        if ($viewModelIssues -eq 0) {
            Write-Host "✅ Baketa UI patterns look good" -ForegroundColor Green
        } else {
            Write-Host "⚠️ Found $viewModelIssues UI pattern issues" -ForegroundColor Yellow
        }
        
    } catch {
        Write-Host "❌ Baketa-specific check failed: $_" -ForegroundColor Red
    }
}

# Results Summary
Write-Host "`n📊 Review Summary" -ForegroundColor Cyan
Write-Host "Total Issues Found: $($Issues.Count)" -ForegroundColor White

if ($Issues.Count -gt 0) {
    $severityGroups = $Issues | Group-Object Severity
    foreach ($group in $severityGroups) {
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
            Write-Host "[$($issue.Severity)] $($issue.Category) - $($issue.File): $($issue.Description)" -ForegroundColor $color
        }
    }
} else {
    Write-Host "🎉 No issues found! Code looks good." -ForegroundColor Green
}

Write-Host "`n使用例:" -ForegroundColor Cyan
Write-Host "  .\simple-code-review.ps1                    # 全体レビュー" -ForegroundColor White
Write-Host "  .\simple-code-review.ps1 -Target baketa     # Baketa固有チェック" -ForegroundColor White
Write-Host "  .\simple-code-review.ps1 -Detailed          # 詳細表示" -ForegroundColor White