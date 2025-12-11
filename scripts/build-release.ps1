# Baketa リリースビルド自動化スクリプト
# Issue #197: 最新の状態から確実にリリースビルドを作成する

param(
    [switch]$SkipGitSync,     # Git同期をスキップ（ローカル変更を保持したい場合）
    [switch]$SkipPyInstaller, # PyInstallerビルドをスキップ（Pythonコード未変更時）
    [switch]$SkipTests,       # テストをスキップ（高速ビルド用）
    [string]$OutputDir = "$PSScriptRoot\..\release"  # 出力ディレクトリ
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Baketa Release Build Script" -ForegroundColor Cyan
Write-Host " Project Root: $ProjectRoot" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 0: 前提条件チェック
Write-Host "[Step 0] 前提条件チェック..." -ForegroundColor Yellow
$dotnetVersion = dotnet --version
Write-Host "  .NET SDK: $dotnetVersion" -ForegroundColor Gray

if (-not (Test-Path "$ProjectRoot\grpc_server\venv_build")) {
    Write-Host "  WARNING: venv_buildが存在しません。PyInstallerビルドには事前セットアップが必要です。" -ForegroundColor Red
    Write-Host "  セットアップコマンド:" -ForegroundColor Gray
    Write-Host "    cd $ProjectRoot\grpc_server" -ForegroundColor Gray
    Write-Host "    py -3.10 -m venv venv_build" -ForegroundColor Gray
    Write-Host "    .\venv_build\Scripts\pip install -r requirements.txt pyinstaller" -ForegroundColor Gray
    if (-not $SkipPyInstaller) {
        throw "venv_build環境がありません"
    }
}

# Step 1: Git最新同期（オプション）
if (-not $SkipGitSync) {
    Write-Host ""
    Write-Host "[Step 1] Git最新同期..." -ForegroundColor Yellow

    Push-Location $ProjectRoot
    try {
        # 現在のブランチを取得
        $currentBranch = git rev-parse --abbrev-ref HEAD
        Write-Host "  Current branch: $currentBranch" -ForegroundColor Gray

        # mainブランチの最新を取得
        Write-Host "  Fetching origin/main..." -ForegroundColor Gray
        git fetch origin main

        # mainとの差分を確認
        $behindCount = git rev-list --count "HEAD..origin/main" 2>$null
        if ($behindCount -gt 0) {
            Write-Host "  mainから$behindCountコミット遅れています。マージ中..." -ForegroundColor Gray
            git merge origin/main --no-edit
            Write-Host "  マージ完了" -ForegroundColor Green
        } else {
            Write-Host "  既にmainと同期済み" -ForegroundColor Green
        }
    } finally {
        Pop-Location
    }
} else {
    Write-Host ""
    Write-Host "[Step 1] Git同期スキップ" -ForegroundColor Gray
}

# Step 2: .NET Releaseビルド
Write-Host ""
Write-Host "[Step 2] .NET Releaseビルド..." -ForegroundColor Yellow
Push-Location $ProjectRoot
try {
    dotnet build Baketa.sln --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw ".NET Releaseビルド失敗"
    }
    Write-Host "  .NET Releaseビルド完了" -ForegroundColor Green
} finally {
    Pop-Location
}

# Step 3: PyInstallerビルド（オプション）
if (-not $SkipPyInstaller) {
    Write-Host ""
    Write-Host "[Step 3] PyInstallerビルド..." -ForegroundColor Yellow

    $venvPython = "$ProjectRoot\grpc_server\venv_build\Scripts\python.exe"
    $venvPyInstaller = "$ProjectRoot\grpc_server\venv_build\Scripts\pyinstaller.exe"

    Push-Location "$ProjectRoot\grpc_server"
    try {
        # Proto再生成（念のため）
        Write-Host "  Proto files regenerating..." -ForegroundColor Gray
        & $venvPython -m grpc_tools.protoc -I. --python_out=. --grpc_python_out=. protos/translation.proto protos/ocr.proto

        # BaketaTranslationServer.exe ビルド
        Write-Host "  Building BaketaTranslationServer.exe..." -ForegroundColor Gray
        if (Test-Path "dist\BaketaTranslationServer") {
            Remove-Item -Recurse -Force "dist\BaketaTranslationServer"
        }
        if (Test-Path "build") {
            Remove-Item -Recurse -Force "build"
        }

        & $venvPyInstaller BaketaTranslationServer.spec --clean --noconfirm
        if ($LASTEXITCODE -ne 0) {
            throw "PyInstallerビルド失敗"
        }
        Write-Host "  BaketaTranslationServer.exe ビルド完了" -ForegroundColor Green
    } finally {
        Pop-Location
    }
} else {
    Write-Host ""
    Write-Host "[Step 3] PyInstallerビルドスキップ" -ForegroundColor Gray
}

# Step 4: テスト実行（オプション）
if (-not $SkipTests) {
    Write-Host ""
    Write-Host "[Step 4] テスト実行..." -ForegroundColor Yellow
    Push-Location $ProjectRoot
    try {
        dotnet test Baketa.sln --configuration Release --no-build --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  WARNING: 一部テストが失敗しました" -ForegroundColor Red
        } else {
            Write-Host "  全テスト成功" -ForegroundColor Green
        }
    } finally {
        Pop-Location
    }
} else {
    Write-Host ""
    Write-Host "[Step 4] テストスキップ" -ForegroundColor Gray
}

# Step 5: リリースパッケージ構築
Write-Host ""
Write-Host "[Step 5] リリースパッケージ構築..." -ForegroundColor Yellow

$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
if (Test-Path $OutputDir) {
    Write-Host "  既存リリースディレクトリを削除中..." -ForegroundColor Gray
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null

# 5.1 .NET Releaseビルド成果物をコピー
$sourceDir = "$ProjectRoot\Baketa.UI\bin\Release\net8.0-windows10.0.19041.0\win-x64"
if (-not (Test-Path $sourceDir)) {
    throw "Releaseビルド成果物が見つかりません: $sourceDir"
}
Write-Host "  Copying .NET Release build..." -ForegroundColor Gray
Copy-Item -Path "$sourceDir\*" -Destination $OutputDir -Recurse

# 5.2 BaketaTranslationServer.exeをコピー
$translationServerDir = "$ProjectRoot\grpc_server\dist\BaketaTranslationServer"
if (Test-Path $translationServerDir) {
    $targetDir = "$OutputDir\grpc_server\BaketaTranslationServer"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Write-Host "  Copying BaketaTranslationServer..." -ForegroundColor Gray
    Copy-Item -Path "$translationServerDir\*" -Destination $targetDir -Recurse
} else {
    Write-Host "  WARNING: BaketaTranslationServerが見つかりません" -ForegroundColor Red
}

# 5.3 OCRモデル（ppocrv5-onnx）をコピー
$ocrModelDir = "$ProjectRoot\Models\ppocrv5-onnx"
if (Test-Path $ocrModelDir) {
    $targetDir = "$OutputDir\Models\ppocrv5-onnx"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Write-Host "  Copying OCR models..." -ForegroundColor Gray
    Copy-Item -Path "$ocrModelDir\*" -Destination $targetDir -Recurse
} else {
    Write-Host "  WARNING: OCRモデルが見つかりません: $ocrModelDir" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " リリースビルド完了!" -ForegroundColor Green
Write-Host " Output: $OutputDir" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# パッケージサイズを表示
$size = (Get-ChildItem $OutputDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ""
Write-Host "Total size: $([math]::Round($size, 2)) MB" -ForegroundColor Gray
