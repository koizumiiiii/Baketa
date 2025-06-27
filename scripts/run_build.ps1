# Baketa ビルドスクリプト（Claude Code用）

param(
    [string]$Configuration = "Debug",
    [string]$Architecture = "x64",
    [string]$Project = "",
    [string]$Verbosity = "minimal",
    [switch]$Clean = $false,
    [switch]$Restore = $true
)

Write-Host "=== Baketa ビルドスクリプト ===" -ForegroundColor Green

# Baketaプロジェクトディレクトリに移動
Set-Location "E:\dev\Baketa"

# dotnetコマンドのパス確認
$dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetPath) {
    Write-Host "❌ dotnetコマンドが見つかりません" -ForegroundColor Red
    Write-Host "フルパスで実行を試みます..." -ForegroundColor Yellow
    $dotnetPath = "C:\Program Files\dotnet\dotnet.exe"
    if (-not (Test-Path $dotnetPath)) {
        Write-Host "❌ dotnetが見つかりません: $dotnetPath" -ForegroundColor Red
        exit 1
    }
    $dotnetCmd = "`"$dotnetPath`""
} else {
    Write-Host "✅ dotnet found: $($dotnetPath.Source)" -ForegroundColor Green
    $dotnetCmd = "dotnet"
}

# クリーンビルド
if ($Clean) {
    Write-Host "🧹 クリーニング中..." -ForegroundColor Yellow
    $cleanCommand = "$dotnetCmd clean"
    if ($Project) { $cleanCommand += " $Project" }
    Invoke-Expression $cleanCommand
}

# パッケージ復元
if ($Restore) {
    Write-Host "📦 パッケージ復元中..." -ForegroundColor Yellow
    $restoreCommand = "$dotnetCmd restore"
    if ($Project) { $restoreCommand += " $Project" }
    Invoke-Expression $restoreCommand
}

# ビルドコマンド構築
$buildCommand = "$dotnetCmd build"

# プロジェクト指定
if ($Project) {
    $buildCommand += " $Project"
    Write-Host "🎯 ターゲットプロジェクト: $Project" -ForegroundColor Cyan
} else {
    Write-Host "🎯 ソリューション全体をビルド" -ForegroundColor Cyan
}

# 設定
$buildCommand += " --configuration $Configuration"
$buildCommand += " --arch $Architecture"
$buildCommand += " --verbosity $Verbosity"

# ビルド実行
Write-Host "🔨 ビルド実行: $buildCommand" -ForegroundColor Yellow

try {
    $buildResult = Invoke-Expression $buildCommand
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ ビルド成功！" -ForegroundColor Green
    } else {
        Write-Host "❌ ビルド失敗（終了コード: $LASTEXITCODE）" -ForegroundColor Red
        return $LASTEXITCODE
    }
}
catch {
    Write-Host "❌ ビルドエラー: $($_.Exception.Message)" -ForegroundColor Red
    return 1
}

# 使用例表示
Write-Host "`n📖 使用例:" -ForegroundColor Cyan
Write-Host "  .\scripts\run_build.ps1                                    # 通常ビルド" -ForegroundColor Gray
Write-Host "  .\scripts\run_build.ps1 -Configuration Release            # リリースビルド" -ForegroundColor Gray
Write-Host "  .\scripts\run_build.ps1 -Project Baketa.UI                # 特定プロジェクト" -ForegroundColor Gray
Write-Host "  .\scripts\run_build.ps1 -Clean -Verbosity detailed        # クリーンビルド（詳細ログ）" -ForegroundColor Gray

return 0