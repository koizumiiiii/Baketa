# Claude Code 通知設定テストスクリプト

Write-Host "=== Claude Code 通知設定テスト ===" -ForegroundColor Green

# 1. 設定ファイル確認
$settingsFile = "E:\dev\Baketa\.claude\settings.json"
if (Test-Path $settingsFile) {
    Write-Host "✅ 設定ファイル確認: $settingsFile" -ForegroundColor Green
    
    try {
        $settings = Get-Content $settingsFile | ConvertFrom-Json
        Write-Host "設定内容:" -ForegroundColor Cyan
        $settings | ConvertTo-Json -Depth 3
        
        if ($settings.preferredNotifChannel) {
            Write-Host "✅ 通知チャンネル設定: $($settings.preferredNotifChannel)" -ForegroundColor Green
        } else {
            Write-Host "⚠️ 通知チャンネル設定なし" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "❌ 設定ファイル読み込みエラー: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "❌ 設定ファイルなし: $settingsFile" -ForegroundColor Red
}

# 2. ターミナルベル音テスト
Write-Host "`n*** ターミナルベル音テスト ***" -ForegroundColor Yellow
Write-Host "以下でベル音が鳴るはずです:" -ForegroundColor Cyan

# ベル音を鳴らす
[Console]::Beep()
Write-Host "`a" -NoNewline  # ASCII ベル文字

Write-Host "`n✅ ベル音テスト完了" -ForegroundColor Green

# 3. Claude Code設定確認用メッセージ
Write-Host "`n*** Claude Code での確認方法 ***" -ForegroundColor Cyan
Write-Host "1. 以下のコマンドでタスク実行:" -ForegroundColor Gray
Write-Host "   claude '【通知テスト】この処理完了時にベル音が鳴るかテストしてください'" -ForegroundColor Gray
Write-Host "2. 処理完了時にベル音が鳴ることを確認" -ForegroundColor Gray
Write-Host "3. 音が鳴らない場合は Claude Code を再起動してテスト" -ForegroundColor Gray

Write-Host "`n*** 通知設定テスト完了！ ***" -ForegroundColor Green