# Baketa アプリケーション実行スクリプト（Claude Code用）

param(
    [string]$Project = "Baketa.UI",
    [string]$Configuration = "Debug",
    [string]$LaunchProfile = "",
    [hashtable]$Arguments = @{},
    [switch]$NoBuild = $false,
    [switch]$Watch = $false
)

Write-Host "=== Baketa アプリケーション実行 ===" -ForegroundColor Green

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

# 実行コマンド構築
if ($Watch) {
    $runCommand = "$dotnetCmd watch run"
    Write-Host "👀 ファイル監視モードで実行" -ForegroundColor Yellow
} else {
    $runCommand = "$dotnetCmd run"
}

# プロジェクト指定
$runCommand += " --project $Project"

# 設定
$runCommand += " --configuration $Configuration"

# ビルドスキップ
if ($NoBuild) {
    $runCommand += " --no-build"
}

# Launch Profile
if ($LaunchProfile) {
    $runCommand += " --launch-profile $LaunchProfile"
}

# 引数
if ($Arguments.Count -gt 0) {
    $runCommand += " --"
    foreach ($arg in $Arguments.GetEnumerator()) {
        $runCommand += " --$($arg.Key) $($arg.Value)"
    }
}

Write-Host "🚀 実行コマンド: $runCommand" -ForegroundColor Yellow

# 利用可能なプロジェクトを表示
Write-Host "📁 利用可能なプロジェクト:" -ForegroundColor Cyan
$projects = @("Baketa.UI", "Baketa.Application", "Baketa.Infrastructure", "Baketa.Core")
foreach ($proj in $projects) {
    if (Test-Path "$proj/$proj.csproj") {
        if ($proj -eq $Project) {
            Write-Host "  ▶ $proj (選択中)" -ForegroundColor Green
        } else {
            Write-Host "    $proj" -ForegroundColor Gray
        }
    }
}

# 実行
try {
    Write-Host "🎮 Baketa を起動しています..." -ForegroundColor Magenta
    Invoke-Expression $runCommand
}
catch {
    Write-Host "❌ 実行エラー: $($_.Exception.Message)" -ForegroundColor Red
    return 1
}

# 使用例表示
Write-Host "`n📖 使用例:" -ForegroundColor Cyan
Write-Host "  .\scripts\run_app.ps1                                      # UI実行" -ForegroundColor Gray
Write-Host "  .\scripts\run_app.ps1 -Configuration Release               # リリース版実行" -ForegroundColor Gray
Write-Host "  .\scripts\run_app.ps1 -Watch                               # ファイル監視モード" -ForegroundColor Gray
Write-Host "  .\scripts\run_app.ps1 -NoBuild                             # ビルドスキップ" -ForegroundColor Gray

return 0