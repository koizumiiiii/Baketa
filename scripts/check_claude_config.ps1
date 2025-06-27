# Claude Code 設定確認スクリプト

Write-Host "=== Claude Code & Claude Desktop 設定確認 ===" -ForegroundColor Green

# 1. Claude Desktop 設定確認
Write-Host "`n1. Claude Desktop 設定" -ForegroundColor Yellow
$claudeDesktopConfig = "$env:APPDATA\Claude\claude_desktop_config.json"
if (Test-Path $claudeDesktopConfig) {
    Write-Host "✅ Claude Desktop設定ファイル存在: $claudeDesktopConfig" -ForegroundColor Green
    try {
        $desktopConfig = Get-Content $claudeDesktopConfig | ConvertFrom-Json
        Write-Host "設定内容:" -ForegroundColor Cyan
        $desktopConfig | ConvertTo-Json -Depth 3
    }
    catch {
        Write-Host "❌ 設定ファイル読み込みエラー: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "❌ Claude Desktop設定ファイルなし: $claudeDesktopConfig" -ForegroundColor Red
}

# 2. Claude Code グローバル設定確認
Write-Host "`n2. Claude Code グローバル設定" -ForegroundColor Yellow
$claudeCodePaths = @(
    "$env:USERPROFILE\.claude\config.json",
    "$env:APPDATA\Claude-Code\config.json",
    "$env:LOCALAPPDATA\Claude-Code\config.json"
)

foreach ($path in $claudeCodePaths) {
    if (Test-Path $path) {
        Write-Host "✅ Claude Code設定ファイル発見: $path" -ForegroundColor Green
        try {
            $codeConfig = Get-Content $path | ConvertFrom-Json
            Write-Host "設定内容:" -ForegroundColor Cyan
            $codeConfig | ConvertTo-Json -Depth 3
        }
        catch {
            Write-Host "❌ 設定ファイル読み込みエラー: $($_.Exception.Message)" -ForegroundColor Red
        }
    } else {
        Write-Host "❌ 設定ファイルなし: $path" -ForegroundColor Gray
    }
}

# 3. Baketa プロジェクト設定確認
Write-Host "`n3. Baketa プロジェクト設定" -ForegroundColor Yellow
$baketaClaudeConfig = "E:\dev\Baketa\.claude\project.json"
if (Test-Path $baketaClaudeConfig) {
    Write-Host "✅ Baketaプロジェクト設定存在: $baketaClaudeConfig" -ForegroundColor Green
    try {
        $projectConfig = Get-Content $baketaClaudeConfig | ConvertFrom-Json
        Write-Host "設定内容:" -ForegroundColor Cyan
        $projectConfig | ConvertTo-Json -Depth 3
    }
    catch {
        Write-Host "❌ 設定ファイル読み込みエラー: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "❌ Baketaプロジェクト設定なし: $baketaClaudeConfig" -ForegroundColor Red
}

# 4. 環境変数確認
Write-Host "`n4. Claude関連環境変数" -ForegroundColor Yellow
$claudeEnvVars = Get-ChildItem Env: | Where-Object { $_.Name -like "*CLAUDE*" }
if ($claudeEnvVars) {
    Write-Host "✅ Claude関連環境変数:" -ForegroundColor Green
    $claudeEnvVars | ForEach-Object {
        Write-Host "  $($_.Name) = $($_.Value)" -ForegroundColor Cyan
    }
} else {
    Write-Host "❌ Claude関連環境変数なし" -ForegroundColor Gray
}

# 5. MCP関連確認
Write-Host "`n5. MCP (Model Context Protocol) 状況" -ForegroundColor Yellow
if ($env:CLAUDE_MCP_TOKEN) {
    Write-Host "✅ MCP Token存在: $($env:CLAUDE_MCP_TOKEN.Substring(0,8))..." -ForegroundColor Green
} else {
    Write-Host "❌ MCP Token なし" -ForegroundColor Gray
}

# 6. プロセス確認
Write-Host "`n6. 実行中のClaude関連プロセス" -ForegroundColor Yellow
$claudeProcesses = Get-Process | Where-Object { $_.ProcessName -like "*claude*" }
if ($claudeProcesses) {
    Write-Host "✅ 実行中のClaude関連プロセス:" -ForegroundColor Green
    $claudeProcesses | Select-Object ProcessName, Id, Path | Format-Table
} else {
    Write-Host "❌ Claude関連プロセスなし" -ForegroundColor Gray
}

Write-Host "`n=== 確認完了 ===" -ForegroundColor Green