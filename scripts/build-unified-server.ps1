<#
.SYNOPSIS
    BaketaUnifiedServer CPU/CUDA版同期ビルドスクリプト
    Issue #366: CPU版とCUDA版を同時にビルドし、バージョン不整合を防止

.DESCRIPTION
    - Gitタグからバージョンを取得し、unified_server.pyに埋め込み
    - CPU版とCUDA版を順次ビルド
    - models-v2リリースへのアップロード準備

.PARAMETER All
    CPU版とCUDA版の両方をビルド（デフォルト）

.PARAMETER Cpu
    CPU版のみビルド

.PARAMETER Cuda
    CUDA版のみビルド

.PARAMETER SkipVersionEmbed
    バージョン埋め込みをスキップ

.PARAMETER Upload
    ビルド後にGitHub Releasesにアップロード

.EXAMPLE
    .\build-unified-server.ps1 -All
    .\build-unified-server.ps1 -Cpu
    .\build-unified-server.ps1 -Cuda
    .\build-unified-server.ps1 -All -Upload
#>

param(
    [switch]$All,
    [switch]$Cpu,
    [switch]$Cuda,
    [switch]$SkipVersionEmbed,
    [switch]$Upload
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$grpcServerDir = Join-Path $rootDir "grpc_server"
$distDir = Join-Path $grpcServerDir "dist"

# デフォルトは両方ビルド
if (-not $All -and -not $Cpu -and -not $Cuda) {
    $All = $true
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BaketaUnifiedServer Build Script" -ForegroundColor Cyan
Write-Host "Issue #366: CPU/CUDA同期ビルド" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Step 1: Gitタグからバージョン取得
function Get-AppVersion {
    try {
        $tag = git describe --tags --abbrev=0 2>$null
        if ($tag -match '^v?(\d+\.\d+\.\d+)') {
            return $Matches[1]
        }
    } catch {}

    # タグがない場合はfallback
    return "0.0.0-dev"
}

$version = Get-AppVersion
Write-Host "`n[Step 1] Version: $version" -ForegroundColor Green

# Step 2: unified_server.pyにVERSION定数を埋め込み
function Set-ServerVersion {
    param([string]$Version)

    $serverFile = Join-Path $grpcServerDir "unified_server.py"
    $content = Get-Content $serverFile -Raw -Encoding UTF8

    # VERSION定数を更新（既存の場合は置換、なければ追加）
    $versionPattern = 'SERVER_VERSION\s*=\s*"[^"]*"'
    $versionLine = "SERVER_VERSION = `"$Version`""

    if ($content -match $versionPattern) {
        $content = $content -replace $versionPattern, $versionLine
        Write-Host "  VERSION定数を更新: $Version" -ForegroundColor Yellow
    } else {
        # importsの後に挿入
        $insertPoint = $content.IndexOf("import grpc")
        if ($insertPoint -gt 0) {
            $beforeImport = $content.Substring(0, $insertPoint)
            $afterImport = $content.Substring($insertPoint)
            $content = $beforeImport + "# [Issue #366] Auto-generated version`n$versionLine`n`n" + $afterImport
            Write-Host "  VERSION定数を追加: $Version" -ForegroundColor Yellow
        }
    }

    Set-Content $serverFile -Value $content -Encoding UTF8 -NoNewline
}

if (-not $SkipVersionEmbed) {
    Write-Host "`n[Step 2] Embedding version into unified_server.py" -ForegroundColor Green
    Set-ServerVersion -Version $version
} else {
    Write-Host "`n[Step 2] Skipped version embedding" -ForegroundColor Yellow
}

# Step 3: PyInstallerビルド
function Build-UnifiedServer {
    param(
        [string]$BuildType,  # "cpu" or "cuda"
        [string]$VenvName
    )

    $venvPath = Join-Path $grpcServerDir $VenvName
    $pyinstaller = Join-Path $venvPath "Scripts\pyinstaller.exe"
    $specFile = Join-Path $grpcServerDir "BaketaUnifiedServer.spec"

    if (-not (Test-Path $pyinstaller)) {
        Write-Host "  ERROR: PyInstaller not found at $pyinstaller" -ForegroundColor Red
        Write-Host "  Run: $venvPath\Scripts\pip install pyinstaller" -ForegroundColor Yellow
        return $false
    }

    Write-Host "  Building $BuildType version..." -ForegroundColor Cyan

    Push-Location $grpcServerDir
    try {
        # クリーンビルド
        $buildDir = Join-Path $grpcServerDir "build"
        if (Test-Path $buildDir) {
            Remove-Item $buildDir -Recurse -Force
        }

        # PyInstaller実行
        & $pyinstaller $specFile --noconfirm 2>&1 | ForEach-Object {
            if ($_ -match "error|Error|ERROR") {
                Write-Host "    $_" -ForegroundColor Red
            } elseif ($_ -match "warn|Warn|WARN") {
                Write-Host "    $_" -ForegroundColor Yellow
            } else {
                Write-Host "    $_" -ForegroundColor Gray
            }
        }

        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ERROR: Build failed for $BuildType" -ForegroundColor Red
            return $false
        }

        # 出力ディレクトリをリネーム
        $outputDir = Join-Path $distDir "BaketaUnifiedServer"
        $targetDir = Join-Path $distDir "BaketaUnifiedServer-$BuildType"

        if (Test-Path $targetDir) {
            Remove-Item $targetDir -Recurse -Force
        }

        if (Test-Path $outputDir) {
            Rename-Item $outputDir $targetDir
            Write-Host "  Output: $targetDir" -ForegroundColor Green
        }

        return $true
    } finally {
        Pop-Location
    }
}

# Step 4: ビルド実行
$buildSuccess = @{}

if ($All -or $Cpu) {
    Write-Host "`n[Step 3a] Building CPU version" -ForegroundColor Green
    $buildSuccess["cpu"] = Build-UnifiedServer -BuildType "cpu" -VenvName "venv_build"
}

if ($All -or $Cuda) {
    Write-Host "`n[Step 3b] Building CUDA version" -ForegroundColor Green
    $buildSuccess["cuda"] = Build-UnifiedServer -BuildType "cuda" -VenvName "venv_build_cuda"
}

# Step 5: ZIPパッケージ作成
function Create-ZipPackage {
    param(
        [string]$BuildType
    )

    $sourceDir = Join-Path $distDir "BaketaUnifiedServer-$BuildType"
    $zipFile = Join-Path $distDir "BaketaUnifiedServer-$BuildType.zip"

    if (-not (Test-Path $sourceDir)) {
        Write-Host "  SKIP: $sourceDir not found" -ForegroundColor Yellow
        return $null
    }

    if (Test-Path $zipFile) {
        Remove-Item $zipFile -Force
    }

    Write-Host "  Creating $zipFile..." -ForegroundColor Cyan
    Compress-Archive -Path "$sourceDir\*" -DestinationPath $zipFile -CompressionLevel Optimal

    $size = (Get-Item $zipFile).Length / 1MB
    Write-Host "  Size: $([math]::Round($size, 2)) MB" -ForegroundColor Green

    # CUDA版が2GB超えの場合は分割
    if ($BuildType -eq "cuda" -and $size -gt 2000) {
        Write-Host "  Splitting for GitHub 2GB limit..." -ForegroundColor Yellow
        # 分割処理
        $splitSize = 2000MB
        $splitDir = Join-Path $distDir "split"
        if (Test-Path $splitDir) { Remove-Item $splitDir -Recurse -Force }
        New-Item $splitDir -ItemType Directory | Out-Null

        # PowerShellでの分割
        $bytes = [System.IO.File]::ReadAllBytes($zipFile)
        $partNum = 1
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $partSize = [Math]::Min($splitSize, $bytes.Length - $offset)
            $partBytes = New-Object byte[] $partSize
            [Array]::Copy($bytes, $offset, $partBytes, 0, $partSize)
            $partFile = Join-Path $splitDir "BaketaUnifiedServer-cuda.zip.$('{0:D3}' -f $partNum)"
            [System.IO.File]::WriteAllBytes($partFile, $partBytes)
            Write-Host "    Created: $partFile ($([math]::Round($partSize/1MB, 2)) MB)" -ForegroundColor Gray
            $offset += $partSize
            $partNum++
        }
    }

    return $zipFile
}

Write-Host "`n[Step 4] Creating ZIP packages" -ForegroundColor Green
$packages = @{}
foreach ($type in $buildSuccess.Keys) {
    if ($buildSuccess[$type]) {
        $packages[$type] = Create-ZipPackage -BuildType $type
    }
}

# Step 6: サマリー
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Build Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Version: $version" -ForegroundColor White

foreach ($type in $buildSuccess.Keys) {
    $status = if ($buildSuccess[$type]) { "SUCCESS" } else { "FAILED" }
    $color = if ($buildSuccess[$type]) { "Green" } else { "Red" }
    Write-Host "$($type.ToUpper()): $status" -ForegroundColor $color

    if ($packages[$type]) {
        Write-Host "  Package: $($packages[$type])" -ForegroundColor Gray
    }
}

# Step 7: アップロード（オプション）
if ($Upload) {
    Write-Host "`n[Step 5] Uploading to GitHub Releases (models-v2)" -ForegroundColor Green

    foreach ($type in $packages.Keys) {
        if ($packages[$type] -and (Test-Path $packages[$type])) {
            Write-Host "  Uploading $($packages[$type])..." -ForegroundColor Cyan
            gh release upload models-v2 $packages[$type] --clobber
        }
    }

    # 分割ファイルがある場合
    $splitDir = Join-Path $distDir "split"
    if (Test-Path $splitDir) {
        Get-ChildItem $splitDir -Filter "*.zip.*" | ForEach-Object {
            Write-Host "  Uploading $($_.Name)..." -ForegroundColor Cyan
            gh release upload models-v2 $_.FullName --clobber
        }
    }
}

Write-Host "`nDone!" -ForegroundColor Green
