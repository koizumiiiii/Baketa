# Baketa Release Build Automation Script
# Issue #197: Create release build from latest state reliably

param(
    [switch]$SkipGitSync,     # Skip Git sync (keep local changes)
    [switch]$SkipPyInstaller, # Skip PyInstaller build (when Python unchanged)
    [switch]$SkipTests,       # Skip tests (fast build)
    [string]$OutputDir = "$PSScriptRoot\..\release"  # Output directory
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Baketa Release Build Script" -ForegroundColor Cyan
Write-Host " Project Root: $ProjectRoot" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 0: Prerequisites check
Write-Host "[Step 0] Checking prerequisites..." -ForegroundColor Yellow
$dotnetVersion = dotnet --version
Write-Host "  .NET SDK: $dotnetVersion" -ForegroundColor Gray

if (-not (Test-Path "$ProjectRoot\grpc_server\venv_build")) {
    Write-Host "  WARNING: venv_build does not exist. PyInstaller build requires setup." -ForegroundColor Red
    Write-Host "  Setup commands:" -ForegroundColor Gray
    Write-Host "    cd $ProjectRoot\grpc_server" -ForegroundColor Gray
    Write-Host "    py -3.10 -m venv venv_build" -ForegroundColor Gray
    Write-Host "    .\venv_build\Scripts\pip install -r requirements.txt pyinstaller" -ForegroundColor Gray
    if (-not $SkipPyInstaller) {
        throw "venv_build environment not found"
    }
}

# Step 1: Git sync (optional)
if (-not $SkipGitSync) {
    Write-Host ""
    Write-Host "[Step 1] Git sync with latest..." -ForegroundColor Yellow

    Push-Location $ProjectRoot
    try {
        # Get current branch
        $currentBranch = git rev-parse --abbrev-ref HEAD
        Write-Host "  Current branch: $currentBranch" -ForegroundColor Gray

        # Fetch latest main
        Write-Host "  Fetching origin/main..." -ForegroundColor Gray
        git fetch origin main

        # Check diff from main
        $behindCount = git rev-list --count "HEAD..origin/main" 2>$null
        if ($behindCount -gt 0) {
            Write-Host "  Behind main by $behindCount commits. Merging..." -ForegroundColor Gray
            git merge origin/main --no-edit
            Write-Host "  Merge complete" -ForegroundColor Green
        } else {
            Write-Host "  Already synced with main" -ForegroundColor Green
        }
    } finally {
        Pop-Location
    }
} else {
    Write-Host ""
    Write-Host "[Step 1] Git sync skipped" -ForegroundColor Gray
}

# Step 2: .NET Release build
Write-Host ""
Write-Host "[Step 2] .NET Release build..." -ForegroundColor Yellow
Push-Location $ProjectRoot
try {
    dotnet build Baketa.sln --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw ".NET Release build failed"
    }
    Write-Host "  .NET Release build complete" -ForegroundColor Green
} finally {
    Pop-Location
}

# Step 3: PyInstaller build (optional)
if (-not $SkipPyInstaller) {
    Write-Host ""
    Write-Host "[Step 3] PyInstaller build..." -ForegroundColor Yellow

    $venvPython = "$ProjectRoot\grpc_server\venv_build\Scripts\python.exe"
    $venvPyInstaller = "$ProjectRoot\grpc_server\venv_build\Scripts\pyinstaller.exe"

    Push-Location "$ProjectRoot\grpc_server"
    try {
        # Regenerate proto files
        Write-Host "  Proto files regenerating..." -ForegroundColor Gray
        & $venvPython -m grpc_tools.protoc -I. --python_out=. --grpc_python_out=. protos/translation.proto protos/ocr.proto

        # Build BaketaTranslationServer.exe
        Write-Host "  Building BaketaTranslationServer.exe..." -ForegroundColor Gray
        if (Test-Path "dist\BaketaTranslationServer") {
            Remove-Item -Recurse -Force "dist\BaketaTranslationServer"
        }
        if (Test-Path "build") {
            Remove-Item -Recurse -Force "build"
        }

        & $venvPyInstaller BaketaTranslationServer.spec --clean --noconfirm
        if ($LASTEXITCODE -ne 0) {
            throw "PyInstaller build failed for BaketaTranslationServer"
        }
        Write-Host "  BaketaTranslationServer.exe build complete" -ForegroundColor Green

        # Build BaketaSuryaOcrServer.exe
        Write-Host "  Building BaketaSuryaOcrServer.exe..." -ForegroundColor Gray
        if (Test-Path "dist\BaketaSuryaOcrServer") {
            Remove-Item -Recurse -Force "dist\BaketaSuryaOcrServer"
        }
        if (Test-Path "build") {
            Remove-Item -Recurse -Force "build"
        }

        & $venvPyInstaller BaketaSuryaOcrServer.spec --clean --noconfirm
        if ($LASTEXITCODE -ne 0) {
            throw "PyInstaller build failed for BaketaSuryaOcrServer"
        }
        Write-Host "  BaketaSuryaOcrServer.exe build complete" -ForegroundColor Green
    } finally {
        Pop-Location
    }
} else {
    Write-Host ""
    Write-Host "[Step 3] PyInstaller build skipped" -ForegroundColor Gray
}

# Step 4: Run tests (optional)
if (-not $SkipTests) {
    Write-Host ""
    Write-Host "[Step 4] Running tests..." -ForegroundColor Yellow
    Push-Location $ProjectRoot
    try {
        dotnet test Baketa.sln --configuration Release --no-build --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  WARNING: Some tests failed" -ForegroundColor Red
        } else {
            Write-Host "  All tests passed" -ForegroundColor Green
        }
    } finally {
        Pop-Location
    }
} else {
    Write-Host ""
    Write-Host "[Step 4] Tests skipped" -ForegroundColor Gray
}

# Step 5: Build release package
Write-Host ""
Write-Host "[Step 5] Building release package..." -ForegroundColor Yellow

$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
if (Test-Path $OutputDir) {
    Write-Host "  Removing existing release directory..." -ForegroundColor Gray
    # Use -LiteralPath to avoid issues with special Windows device names like 'nul'
    Get-ChildItem -LiteralPath $OutputDir -Force | ForEach-Object {
        if ($_.Name -eq 'nul') {
            # Skip Windows reserved device name
            Write-Host "  Skipping reserved device name: $($_.FullName)" -ForegroundColor Yellow
        } else {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    Remove-Item -LiteralPath $OutputDir -Force -ErrorAction SilentlyContinue
}
$null = New-Item -ItemType Directory -Path $OutputDir -Force

# 5.1 Copy .NET Release build artifacts
$sourceDir = "$ProjectRoot\Baketa.UI\bin\Release\net8.0-windows10.0.19041.0\win-x64"
if (-not (Test-Path $sourceDir)) {
    throw "Release build artifacts not found: $sourceDir"
}
Write-Host "  Copying .NET Release build..." -ForegroundColor Gray
Copy-Item -Path "$sourceDir\*" -Destination $OutputDir -Recurse

# 5.2 Copy BaketaTranslationServer.exe
$translationServerDir = "$ProjectRoot\grpc_server\dist\BaketaTranslationServer"
if (Test-Path $translationServerDir) {
    $targetDir = "$OutputDir\grpc_server\BaketaTranslationServer"
    $null = New-Item -ItemType Directory -Path $targetDir -Force
    Write-Host "  Copying BaketaTranslationServer..." -ForegroundColor Gray
    Copy-Item -Path "$translationServerDir\*" -Destination $targetDir -Recurse
} else {
    Write-Host "  WARNING: BaketaTranslationServer not found" -ForegroundColor Red
}

# 5.2.1 Copy BaketaSuryaOcrServer.exe
$suryaServerDir = "$ProjectRoot\grpc_server\dist\BaketaSuryaOcrServer"
if (Test-Path $suryaServerDir) {
    $targetDir = "$OutputDir\grpc_server\BaketaSuryaOcrServer"
    $null = New-Item -ItemType Directory -Path $targetDir -Force
    Write-Host "  Copying BaketaSuryaOcrServer..." -ForegroundColor Gray
    Copy-Item -Path "$suryaServerDir\*" -Destination $targetDir -Recurse
} else {
    Write-Host "  WARNING: BaketaSuryaOcrServer not found" -ForegroundColor Red
}

# 5.3 Copy OCR models (ppocrv5-onnx)
$ocrModelDir = "$ProjectRoot\Models\ppocrv5-onnx"
if (Test-Path $ocrModelDir) {
    $targetDir = "$OutputDir\Models\ppocrv5-onnx"
    $null = New-Item -ItemType Directory -Path $targetDir -Force
    Write-Host "  Copying OCR models..." -ForegroundColor Gray
    Copy-Item -Path "$ocrModelDir\*" -Destination $targetDir -Recurse
} else {
    Write-Host "  WARNING: OCR models not found: $ocrModelDir" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Release build complete!" -ForegroundColor Green
Write-Host " Output: $OutputDir" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Display package size
$size = (Get-ChildItem $OutputDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ""
Write-Host "Total size: $([math]::Round($size, 2)) MB" -ForegroundColor Gray
