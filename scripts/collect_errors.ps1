# Build and Test Error Collection Script for Baketa

## Usage Instructions
Save this file as `collect_errors.ps1` and run to collect all build and test errors.

```powershell
# Baketa Project Error Collection Script
param(
    [string]$OutputDir = ".\error_logs",
    [switch]$IncludeTests
)

# Create output directory
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

Write-Host "Collecting Baketa build errors..." -ForegroundColor Green

# Build errors
Write-Host "Running dotnet build..." -ForegroundColor Yellow
dotnet build --configuration Debug --arch x64 --verbosity normal > "$OutputDir\build_$timestamp.txt" 2>&1

# Code analysis warnings
Write-Host "Collecting code analysis warnings..." -ForegroundColor Yellow
dotnet build --verbosity normal --configuration Debug | Where-Object { $_ -match "warning (CA|IDE|CS)" } > "$OutputDir\warnings_$timestamp.txt"

# Test errors (if requested)
if ($IncludeTests) {
    Write-Host "Running tests..." -ForegroundColor Yellow
    dotnet test --logger "console;verbosity=detailed" > "$OutputDir\test_$timestamp.txt" 2>&1
}

Write-Host "Error collection complete. Files saved to: $OutputDir" -ForegroundColor Green
Write-Host "Use these files with Claude Code:" -ForegroundColor Cyan
Write-Host "  claude 'このファイルに記載されているエラーを修正して' --file $OutputDir\build_$timestamp.txt" -ForegroundColor Cyan
```

## Manual Error Collection

For quick manual collection, use these commands:

### Build Errors
```bash
dotnet build --configuration Debug --arch x64 --verbosity normal > build_errors.txt 2>&1
```

### Specific Project Errors
```bash
dotnet build Baketa.Infrastructure --verbosity normal > infrastructure_errors.txt 2>&1
```

### Test Failures
```bash
dotnet test --logger "console;verbosity=detailed" > test_failures.txt 2>&1
```

### Code Analysis Only
```bash
dotnet build --verbosity minimal | findstr "warning\|error" > analysis_warnings.txt
```

## File Format for Claude Code

The most effective format for Claude Code is the direct command-line output:

```
E:\dev\Baketa\Baketa.Infrastructure\Capture\DifferenceDetection\DifferenceVisualizerTool.cs(221,45): warning IDE0060: Remove unused parameter 'parameter' if it is not part of a shipped API [E:\dev\Baketa\Baketa.Infrastructure\Baketa.Infrastructure.csproj]
```

This format provides:
- Exact file path
- Line and column numbers
- Warning/error code
- Detailed description
- Project context
