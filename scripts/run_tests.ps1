# Baketa テスト実行スクリプト（Claude Code用）

param(
    [string]$Project = "",
    [string]$Filter = "",
    [string]$Verbosity = "minimal",
    [switch]$NoBuild = $false
)

Write-Host "=== Baketa テスト実行 ===" -ForegroundColor Green

# Baketaプロジェクトディレクトリに移動
Set-Location "E:\dev\Baketa"

# dotnetコマンドのパス確認
$dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetPath) {
    Write-Host "❌ dotnetコマンドが見つかりません" -ForegroundColor Red
    Write-Host "環境変数PATHに以下を追加してください:" -ForegroundColor Yellow
    Write-Host "C:\Program Files\dotnet\" -ForegroundColor Cyan
    exit 1
}

Write-Host "✅ dotnet found: $($dotnetPath.Source)" -ForegroundColor Green

# テストコマンド構築
$testCommand = "dotnet test"

# プロジェクト指定
if ($Project) {
    $testCommand += " $Project"
} else {
    Write-Host "利用可能なテストプロジェクト:" -ForegroundColor Cyan
    Get-ChildItem tests -Directory | ForEach-Object {
        Write-Host "  tests/$($_.Name)" -ForegroundColor Gray
    }
}

# フィルター指定
if ($Filter) {
    $testCommand += " --filter `"$Filter`""
}

# ビルドスキップ
if ($NoBuild) {
    $testCommand += " --no-build"
}

# ログ設定
$testCommand += " --logger `"console;verbosity=$Verbosity`""

Write-Host "実行コマンド: $testCommand" -ForegroundColor Yellow

# 実行
try {
    Invoke-Expression $testCommand
    Write-Host "✅ テスト完了" -ForegroundColor Green
}
catch {
    Write-Host "❌ テスト実行エラー: $($_.Exception.Message)" -ForegroundColor Red
}

# 使用例表示
Write-Host "`n使用例:" -ForegroundColor Cyan
Write-Host "  .\scripts\run_tests.ps1 -Project 'tests/Baketa.UI.Tests'" -ForegroundColor Gray
Write-Host "  .\scripts\run_tests.ps1 -Project 'tests/Baketa.UI.Tests' -Filter 'EnhancedSettingsWindowViewModelIntegrationTests'" -ForegroundColor Gray
Write-Host "  .\scripts\run_tests.ps1 -Project 'tests/Baketa.Core.Tests' -Verbosity 'detailed'" -ForegroundColor Gray