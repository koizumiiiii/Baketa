# Claude Code 自動承認モード設定スクリプト

param(
    [string]$Task = "",
    [switch]$AutoApprove = $true,
    [switch]$Japanese = $true
)

Write-Host "=== Claude Code 自動承認モード ===" -ForegroundColor Green

# Baketaプロジェクトディレクトリに移動
Set-Location "E:\dev\Baketa"

# 基本的な自動承認コマンドテンプレート
$baseCommand = "claude"

# オプション構築
$options = @()

if ($AutoApprove) {
    $options += "--auto-approve"
}

if ($Japanese) {
    $options += "--language=japanese"
}

# タスクが指定されている場合
if ($Task) {
    $fullCommand = "$baseCommand $($options -join ' ') `"【日本語必須・自動承認】$Task`""
    Write-Host "実行コマンド: $fullCommand" -ForegroundColor Yellow
    
    # 実際にコマンドを実行
    Invoke-Expression $fullCommand
} else {
    # 使用例を表示
    Write-Host "使用例:" -ForegroundColor Cyan
    Write-Host "  .\scripts\claude_auto.ps1 -Task 'エラーを修正して'" -ForegroundColor Gray
    Write-Host "  .\scripts\claude_auto.ps1 -Task '新しいOCRフィルターを実装して'" -ForegroundColor Gray
    Write-Host "  .\scripts\claude_auto.ps1 -Task 'コードをリファクタリングして'" -ForegroundColor Gray
    
    Write-Host "`n自動承認で使用する場合のキーボードショートカット:" -ForegroundColor Cyan
    Write-Host "  Shift + Tab = 'Yes, and don't ask again this session'" -ForegroundColor Yellow
}

# 便利なエイリアス関数を定義
function Claude-Auto {
    param([string]$Task)
    & "E:\dev\Baketa\scripts\claude_auto.ps1" -Task $Task -AutoApprove -Japanese
}

Write-Host "`n便利な使い方:" -ForegroundColor Cyan
Write-Host "  Claude-Auto 'エラーを修正して'" -ForegroundColor Gray
Write-Host "  この関数を PowerShell プロファイルに追加すると便利です" -ForegroundColor Yellow