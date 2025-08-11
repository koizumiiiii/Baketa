# Baketa Development Commands Reference

## Essential Build Commands

### Core Build Operations
```bash
# Standard build (recommended for development)
dotnet build --configuration Debug --arch x64

# Release build for production
dotnet build --configuration Release --arch x64

# Clean build (when facing dependency issues)
dotnet clean
dotnet build --configuration Debug --arch x64

# Restore packages
dotnet restore
```

### Native DLL Build (Critical - Must Build First)
```bash
# Build native DLL using MSBuild (Visual Studio 2022 required)
msbuild BaketaCaptureNative\BaketaCaptureNative.sln /p:Configuration=Debug /p:Platform=x64

# Alternative: Use Visual Studio 2022 to build BaketaCaptureNative.sln
# File -> Open -> Project/Solution -> BaketaCaptureNative\BaketaCaptureNative.sln
```

## Testing Commands

### Comprehensive Testing
```bash
# Run all tests
dotnet test

# Run tests with specific verbosity
dotnet test --verbosity normal

# Run specific test project
dotnet test tests/Baketa.Core.Tests/
dotnet test tests/Baketa.Infrastructure.Tests/
dotnet test tests/Baketa.UI.Tests/

# Run specific test categories
dotnet test --filter "ClassName~RealSentencePieceTokenizerTests"
dotnet test --filter "Category=Performance"

# Run with timeout (for performance tests)
timeout 20 dotnet test tests/Baketa.Infrastructure.Tests/ --filter "CompareOnnxModels_QualityComparison" --verbosity minimal
```

## Running the Application

### Development Execution
```bash
# Run UI application
dotnet run --project Baketa.UI

# Run with Release configuration
dotnet run --project Baketa.UI --configuration Release

# Direct executable (after build)
.\Baketa.UI\bin\Debug\net8.0-windows10.0.19041.0\Baketa.UI.exe
```

## Model Setup Commands

### OPUS-MT Model Preparation
```powershell
# Download required translation models
.\scripts\download_opus_mt_models.ps1

# Verify model integrity
.\scripts\verify_opus_mt_models.ps1

# Run SentencePiece tokenization tests
.\scripts\run_sentencepiece_tests.ps1
```

## Python Environment Commands

### Environment Management (PowerShell Recommended)
```bash
# Set Python version (if using pyenv-win)
pyenv global 3.10.9

# Verify Python setup
py --version
where python

# Run Python scripts (PowerShell method - preferred)
powershell -Command "python script_name.py"

# Alternative: Use Python launcher
py script_name.py

# Command Prompt method
cmd /c "python script_name.py"
```

## Diagnostic and Debug Commands

### Performance Analysis
```powershell
# Run performance bottleneck analysis
powershell -Command "cd 'E:\dev\Baketa'; python scripts\current_bottleneck_analysis.py"

# Simple performance check
.\scripts\simple_performance_analysis.ps1

# Check system resources during execution
.\scripts\diagnose_capture_issues.ps1
```

### Build Diagnostics
```bash
# Detailed build with verbose output
dotnet build --verbosity detailed

# Check DLL dependencies
.\scripts\check_dll.ps1

# Verify implementation integrity
.\scripts\check_implementation.ps1
```

## Git Commands for Development

### Standard Git Operations
```bash
# Check status
git status

# View recent commits
git log --oneline -10

# View detailed changes
git diff

# Stage and commit changes (example)
git add .
git commit -m "feat: implement feature XYZ

🤖 Generated with Claude Code
Co-Authored-By: Claude <noreply@anthropic.com>"
```

## Windows-Specific Utility Commands

### File Operations
```bash
# List directory contents
dir                    # Command Prompt
ls                     # PowerShell or Git Bash

# Find files
dir /s *.cs           # Command Prompt - find C# files recursively

# Search file contents (ripgrep - preferred)
rg "search_pattern"

# Copy files
copy source destination     # Command Prompt
Copy-Item source destination # PowerShell
```

### Process Management
```bash
# List running processes
tasklist | findstr Baketa

# Kill process by name
taskkill /f /im Baketa.UI.exe
```

## PowerShell Script Execution

### Script Execution Policy
```powershell
# Check current execution policy
Get-ExecutionPolicy

# Set execution policy (if needed)
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# Run PowerShell scripts
.\scripts\run_build.ps1
.\scripts\run_app.ps1
```

## Troubleshooting Commands

### Common Issues Resolution
```bash
# Clean all build artifacts
dotnet clean
Remove-Item -Recurse -Force .\bin\
Remove-Item -Recurse -Force .\obj\

# Reset NuGet cache
dotnet nuget locals all --clear

# Verify .NET installation
dotnet --info

# Check Visual Studio components
where msbuild
```

### Environment Verification
```bash
# Check Claude Code configuration
.\scripts\check_claude_config.ps1

# Verify environment setup
.\scripts\check-environment.ps1

# Test notifications system
.\scripts\test_notifications.ps1
```

## Development Workflow Commands

### Recommended Daily Workflow
```bash
# 1. Update codebase
git pull

# 2. Clean build
dotnet clean
dotnet build --configuration Debug --arch x64

# 3. Run tests
dotnet test

# 4. Run application for testing
dotnet run --project Baketa.UI

# 5. Performance check (if making performance changes)
powershell -Command "cd 'E:\dev\Baketa'; python scripts\current_bottleneck_analysis.py"
```

### After Implementation Verification
```bash
# Mandatory build verification after code changes
dotnet build Baketa.sln --configuration Debug

# Gemini code review (if successful build)
gemini -p "実装完了しました。以下のコードについてレビューをお願いします。..."
```

Note: Always use PowerShell for Python script execution to avoid Git Bash compatibility issues with pyenv-win.