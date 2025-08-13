# Advanced Code Review Script with ripgrep
param(
    [string]$Target = "all",
    [switch]$Detailed
)

Write-Host "=== Baketa Advanced Code Review (with ripgrep) ===" -ForegroundColor Green

# Check for ripgrep
$rgPath = ".\ripgrep\ripgrep-14.1.1-x86_64-pc-windows-msvc\rg.exe"
if (-not (Test-Path $rgPath)) {
    Write-Host "❌ ripgrep not found at $rgPath" -ForegroundColor Red
    Write-Host "Falling back to basic review..." -ForegroundColor Yellow
    & ".\scripts\basic-code-review.ps1" @PSBoundParameters
    exit
}

Write-Host "✅ Using ripgrep: $rgPath" -ForegroundColor Green

$Issues = New-Object System.Collections.ArrayList

function Add-Issue {
    param($File, $Line, $Severity, $Category, $Description)
    [void]$Issues.Add([PSCustomObject]@{
        File = $File
        Line = $Line
        Severity = $Severity
        Category = $Category
        Description = $Description
    })
}

# 1. File-scoped namespace check
Write-Host "`n🔍 C# 12 Namespace Check..." -ForegroundColor Yellow
try {
    $oldNamespaces = & $rgPath --type cs "namespace\s+\w+\s*\{" . 2>$null
    if ($oldNamespaces) {
        foreach ($line in $oldNamespaces) {
            $parts = $line -split ":"
            if ($parts.Length -ge 2) {
                Add-Issue -File $parts[0] -Line $parts[1] -Severity "Info" -Category "Modern C#" `
                    -Description "Consider file-scoped namespace (C# 12)"
            }
        }
    }
    Write-Host "✅ Namespace check completed" -ForegroundColor Green
} catch {
    Write-Host "⚠️ Namespace check failed: $_" -ForegroundColor Yellow
}

# 2. ConfigureAwait check
Write-Host "`n⚡ ConfigureAwait Check..." -ForegroundColor Yellow
try {
    $awaitWithoutConfigure = & $rgPath --type cs "await\s+\w+.*(?<!ConfigureAwait\(false\))" . 2>$null
    if ($awaitWithoutConfigure) {
        foreach ($line in $awaitWithoutConfigure) {
            # Skip test files
            if ($line -notmatch "Test") {
                $parts = $line -split ":"
                if ($parts.Length -ge 2) {
                    Add-Issue -File $parts[0] -Line $parts[1] -Severity "Warning" -Category "Async" `
                        -Description "Consider ConfigureAwait(false) in library code"
                }
            }
        }
    }
    Write-Host "✅ ConfigureAwait check completed" -ForegroundColor Green
} catch {
    Write-Host "⚠️ ConfigureAwait check failed: $_" -ForegroundColor Yellow
}

# 3. Architecture violation check
Write-Host "`n🏗️ Architecture Check..." -ForegroundColor Yellow
try {
    # Core layer should not reference UI
    $coreToUI = & $rgPath --type cs "using.*\.UI" "Baketa.Core" 2>$null
    if ($coreToUI) {
        foreach ($line in $coreToUI) {
            $parts = $line -split ":"
            if ($parts.Length -ge 2) {
                Add-Issue -File $parts[0] -Line $parts[1] -Severity "Error" -Category "Architecture" `
                    -Description "Core layer should not reference UI layer"
            }
        }
    }
    Write-Host "✅ Architecture check completed" -ForegroundColor Green
} catch {
    Write-Host "⚠️ Architecture check failed: $_" -ForegroundColor Yellow
}

# 4. Baketa-specific patterns
if ($Target -eq "all" -or $Target -eq "baketa") {
    Write-Host "`n🎮 Baketa-specific Check..." -ForegroundColor Yellow
    try {
        # ViewModelBase inheritance check
        $viewModels = & $rgPath --type cs "class.*ViewModel.*:" "Baketa.UI" 2>$null
        if ($viewModels) {
            foreach ($line in $viewModels) {
                if ($line -notmatch "ViewModelBase") {
                    $parts = $line -split ":"
                    if ($parts.Length -ge 2) {
                        Add-Issue -File $parts[0] -Line $parts[1] -Severity "Warning" -Category "UI Pattern" `
                            -Description "ViewModel should inherit from ViewModelBase"
                    }
                }
            }
        }
        Write-Host "✅ Baketa patterns check completed" -ForegroundColor Green
    } catch {
        Write-Host "⚠️ Baketa patterns check failed: $_" -ForegroundColor Yellow
    }
}

# 5. Security patterns
Write-Host "`n🔒 Security Check..." -ForegroundColor Yellow
try {
    # API key hardcoding check
    $hardcodedKeys = & $rgPath --type cs "AIzaSy|sk-" . 2>$null
    if ($hardcodedKeys) {
        foreach ($line in $hardcodedKeys) {
            $parts = $line -split ":"
            if ($parts.Length -ge 2) {
                Add-Issue -File $parts[0] -Line $parts[1] -Severity "Error" -Category "Security" `
                    -Description "Potential hardcoded API key detected"
            }
        }
    }
    Write-Host "✅ Security check completed" -ForegroundColor Green
} catch {
    Write-Host "⚠️ Security check failed: $_" -ForegroundColor Yellow
}

# Results Summary
Write-Host "`n📊 Advanced Review Summary" -ForegroundColor Cyan
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
            Write-Host "  [$($issue.Severity)] $($issue.File):$($issue.Line) - $($issue.Description)" -ForegroundColor $color
        }
    }
} else {
    Write-Host "🎉 No issues found! Code quality is excellent." -ForegroundColor Green
}

Write-Host "`n💪 Advanced analysis with ripgrep completed!" -ForegroundColor Green