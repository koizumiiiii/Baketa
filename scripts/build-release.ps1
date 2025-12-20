# Baketa Release Build Automation Script
# Issue #197: Create release build from latest state reliably

param(
    [switch]$SkipGitSync,     # Skip Git sync (keep local changes)
    [switch]$SkipPyInstaller, # Skip PyInstaller build (when Python unchanged)
    [switch]$SkipTests,       # Skip tests (fast build)
    [switch]$SkipZip,         # Skip zip file creation
    [string]$OutputDir = "$PSScriptRoot\..\release",  # Output directory
    [string]$ZipName = ""     # Zip file name (auto-generated if empty)
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

# Step 2: .NET Release publish (self-contained)
Write-Host ""
Write-Host "[Step 2] .NET Release publish (self-contained)..." -ForegroundColor Yellow
Push-Location $ProjectRoot
try {
    # Use dotnet publish with self-contained to bundle .NET runtime
    # This matches release.yml behavior for consistent builds
    $publishOutput = "$ProjectRoot\publish-temp"
    if (Test-Path $publishOutput) {
        Remove-Item -LiteralPath $publishOutput -Recurse -Force
    }

    dotnet publish Baketa.UI/Baketa.UI.csproj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:IS_DISTRIBUTION=true `
        --output $publishOutput `
        --verbosity minimal

    if ($LASTEXITCODE -ne 0) {
        throw ".NET Release publish failed"
    }
    Write-Host "  .NET Release publish complete" -ForegroundColor Green
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

    # Check for reserved device names first
    $hasReservedNames = $false
    Get-ChildItem -LiteralPath $OutputDir -Force -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.Name -match '^(nul|con|prn|aux|com[1-9]|lpt[1-9])$') {
            $hasReservedNames = $true
            Write-Host "  Skipping reserved device name: $($_.Name)" -ForegroundColor Yellow
        }
    }

    # Remove all deletable items
    Get-ChildItem -LiteralPath $OutputDir -Force -ErrorAction SilentlyContinue | ForEach-Object {
        if (-not ($_.Name -match '^(nul|con|prn|aux|com[1-9]|lpt[1-9])$')) {
            try {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
            } catch {
                Write-Host "  Warning: Could not delete $($_.Name)" -ForegroundColor Yellow
            }
        }
    }

    # Only try to remove directory if no reserved names exist
    if (-not $hasReservedNames) {
        try {
            Remove-Item -LiteralPath $OutputDir -Force -ErrorAction Stop
        } catch {
            # Directory may not be empty due to reserved names
        }
    }
}
$null = New-Item -ItemType Directory -Path $OutputDir -Force

# 5.1 Copy .NET Release publish artifacts
$sourceDir = "$ProjectRoot\publish-temp"
if (-not (Test-Path $sourceDir)) {
    throw "Release publish artifacts not found: $sourceDir"
}
Write-Host "  Copying .NET Release publish output..." -ForegroundColor Gray
Copy-Item -Path "$sourceDir\*" -Destination $OutputDir -Recurse

# Clean up temp publish directory
Remove-Item -LiteralPath $sourceDir -Recurse -Force -ErrorAction SilentlyContinue

# 5.2 Copy BaketaTranslationServer.exe
$translationServerDir = "$ProjectRoot\grpc_server\dist\BaketaTranslationServer"
if (Test-Path $translationServerDir) {
    $targetDir = "$OutputDir\grpc_server\BaketaTranslationServer"
    # 既存ディレクトリを削除してからコピー（_internalディレクトリの競合回避）
    if (Test-Path $targetDir) {
        Remove-Item -LiteralPath $targetDir -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Path $targetDir -Force
    Write-Host "  Copying BaketaTranslationServer..." -ForegroundColor Gray
    Copy-Item -Path "$translationServerDir\*" -Destination $targetDir -Recurse -Force
} else {
    Write-Host "  WARNING: BaketaTranslationServer not found" -ForegroundColor Red
}

# 5.2.1 BaketaSuryaOcrServer - [Issue #210] 初回起動時にGPU検出結果に基づきダウンロード
# CPU版/CUDA版はGitHub Releasesから自動ダウンロードされるため、リリースパッケージには含めない
Write-Host "  BaketaSuryaOcrServer: Skipped (downloaded on first run based on GPU detection)" -ForegroundColor Gray

# 5.3 OCR models - [SIZE_OPTIMIZATION] ppocrv5-onnxはNuGetパッケージに含まれるため不要
Write-Host "  OCR models: Skipped (included in NuGet package Sdcb.PaddleOCR.Models.Local)" -ForegroundColor Gray

# ========================================
# 5.4 [SIZE_OPTIMIZATION] 不要な言語フォルダを削除（ja, en以外）
# ========================================
Write-Host "  Removing unnecessary language folders..." -ForegroundColor Gray
$unnecessaryLangs = @('cs', 'de', 'es', 'fr', 'it', 'ko', 'pl', 'pt-BR', 'ru', 'tr', 'zh-Hans', 'zh-Hant')
$removedCount = 0
$removedSize = 0
foreach ($lang in $unnecessaryLangs) {
    $langPath = Join-Path $OutputDir $lang
    if (Test-Path $langPath) {
        $folderSize = (Get-ChildItem -LiteralPath $langPath -Recurse -File | Measure-Object -Property Length -Sum).Sum
        $removedSize += $folderSize
        Remove-Item -LiteralPath $langPath -Recurse -Force
        $removedCount++
        Write-Host "    Removed: $lang ($([math]::Round($folderSize/1KB, 1)) KB)" -ForegroundColor DarkGray
    }
}
Write-Host "  Removed $removedCount language folders ($([math]::Round($removedSize/1MB, 2)) MB freed)" -ForegroundColor Green

# ========================================
# 5.5 [USER_FRIENDLY] 不要なファイルを削除
# ========================================
Write-Host "  Removing unnecessary files..." -ForegroundColor Gray

# 開発用設定ファイル削除
$devConfigFiles = @(
    'appsettings.Development.json',
    'appsettings.AlphaTest.json',
    'appsettings.Local.json'
)
foreach ($file in $devConfigFiles) {
    $filePath = Join-Path $OutputDir $file
    if (Test-Path $filePath) {
        Remove-Item -LiteralPath $filePath -Force
        Write-Host "    Removed: $file" -ForegroundColor DarkGray
    }
}

# PDB（デバッグシンボル）ファイル削除（Releaseには不要）
$pdbFiles = Get-ChildItem -Path $OutputDir -Filter "*.pdb" -Recurse
$pdbCount = 0
$pdbSize = 0
foreach ($pdb in $pdbFiles) {
    $pdbSize += $pdb.Length
    Remove-Item -LiteralPath $pdb.FullName -Force
    $pdbCount++
}
if ($pdbCount -gt 0) {
    Write-Host "    Removed: $pdbCount PDB files ($([math]::Round($pdbSize/1MB, 2)) MB freed)" -ForegroundColor DarkGray
}

# XML（XMLドキュメント）ファイル削除
$xmlDocFiles = Get-ChildItem -Path $OutputDir -Filter "*.xml" -Recurse | Where-Object { $_.Name -match '\.xml$' -and $_.Name -notmatch 'appsettings|config' }
$xmlCount = 0
$xmlSize = 0
foreach ($xml in $xmlDocFiles) {
    $xmlSize += $xml.Length
    Remove-Item -LiteralPath $xml.FullName -Force
    $xmlCount++
}
if ($xmlCount -gt 0) {
    Write-Host "    Removed: $xmlCount XML doc files ($([math]::Round($xmlSize/1MB, 2)) MB freed)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Release build complete!" -ForegroundColor Green
Write-Host " Output: $OutputDir" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Display package contents summary
Write-Host ""
Write-Host "Package Contents:" -ForegroundColor Yellow

# Count files by category
$exeFile = Get-ChildItem -Path $OutputDir -Filter "Baketa.exe" -ErrorAction SilentlyContinue
$dllFiles = Get-ChildItem -Path $OutputDir -Filter "*.dll" -File -ErrorAction SilentlyContinue
$configFiles = Get-ChildItem -Path $OutputDir -Filter "*.json" -File -ErrorAction SilentlyContinue
$grpcServerExists = Test-Path (Join-Path $OutputDir "grpc_server")

Write-Host "  Main executable: Baketa.exe" -ForegroundColor Gray
Write-Host "  DLL files: $($dllFiles.Count) files" -ForegroundColor Gray
Write-Host "  Config files: $($configFiles.Count) files" -ForegroundColor Gray
Write-Host "  Translation server: $(if ($grpcServerExists) { 'Included' } else { 'Not found' })" -ForegroundColor Gray

# Display package size
$size = (Get-ChildItem $OutputDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ""
Write-Host "Total size: $([math]::Round($size, 2)) MB" -ForegroundColor Cyan

# Display top 10 largest files for reference
Write-Host ""
Write-Host "Top 10 largest files:" -ForegroundColor Yellow
Get-ChildItem $OutputDir -Recurse -File |
    Sort-Object Length -Descending |
    Select-Object -First 10 |
    ForEach-Object {
        $sizeInMB = [math]::Round($_.Length / 1MB, 2)
        $relativePath = $_.FullName.Replace($OutputDir, "").TrimStart("\")
        Write-Host "  $sizeInMB MB`t$relativePath" -ForegroundColor Gray
    }

# ========================================
# Step 6: Create zip file (optional)
# ========================================
if (-not $SkipZip) {
    Write-Host ""
    Write-Host "[Step 6] Creating zip file..." -ForegroundColor Yellow

    # Auto-generate zip name if not provided
    if ([string]::IsNullOrEmpty($ZipName)) {
        # Try to get version from git tag
        $gitTag = git describe --tags --abbrev=0 2>$null
        if ($gitTag) {
            $ZipName = "Baketa-$gitTag.zip"
        } else {
            $ZipName = "Baketa-release.zip"
        }
    }

    $ZipPath = Join-Path $ProjectRoot $ZipName

    # Remove existing zip if exists
    if (Test-Path $ZipPath) {
        Remove-Item $ZipPath -Force
        Write-Host "  Removed existing: $ZipName" -ForegroundColor DarkGray
    }

    # Get all files excluding Windows reserved names
    $filesToZip = Get-ChildItem -Path $OutputDir -Recurse -File |
        Where-Object { $_.Name -notmatch '^(nul|con|prn|aux|com[1-9]|lpt[1-9])$' }

    # Create zip using .NET (more reliable than Compress-Archive for large files)
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    try {
        $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, 'Create')

        $totalFiles = $filesToZip.Count
        $processedFiles = 0

        foreach ($file in $filesToZip) {
            $relativePath = $file.FullName.Substring($OutputDir.Length + 1)
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip,
                $file.FullName,
                $relativePath,
                [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null

            $processedFiles++
            if ($processedFiles % 100 -eq 0) {
                Write-Host "  Progress: $processedFiles / $totalFiles files" -ForegroundColor DarkGray
            }
        }

        $zip.Dispose()

        $zipSize = (Get-Item $ZipPath).Length / 1MB
        Write-Host "  Created: $ZipName ($([math]::Round($zipSize, 2)) MB)" -ForegroundColor Green
        Write-Host ""
        Write-Host "Zip file: $ZipPath" -ForegroundColor Cyan
    }
    catch {
        Write-Host "  ERROR: Failed to create zip - $($_.Exception.Message)" -ForegroundColor Red
        if ($zip) { $zip.Dispose() }
    }
} else {
    Write-Host ""
    Write-Host "[Step 6] Zip creation skipped" -ForegroundColor Gray
}
