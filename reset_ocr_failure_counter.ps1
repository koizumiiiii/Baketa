# PaddleOCR連続失敗カウンターリセット用スクリプト
# P0実装テスト用緊急修正

Write-Host "PaddleOCR連続失敗カウンターリセット開始" -ForegroundColor Yellow

# Baketaプロセス確認
$baketaProcess = Get-Process -Name "Baketa.UI" -ErrorAction SilentlyContinue
if ($baketaProcess) {
    Write-Host "Baketa.UI プロセス確認: ID=$($baketaProcess.Id)" -ForegroundColor Green
    
    # プロセス停止
    Write-Host "Baketaプロセスを停止します..." -ForegroundColor Yellow
    Stop-Process -Id $baketaProcess.Id -Force
    Start-Sleep -Seconds 3
    
    Write-Host "Baketaプロセスを停止しました" -ForegroundColor Green
} else {
    Write-Host "Baketa.UIプロセスが見つかりません" -ForegroundColor Orange
}

# レポートファイル削除
$reportPath = "C:\Users\suke0\AppData\Roaming\Baketa\Reports"
if (Test-Path $reportPath) {
    Write-Host "エラーレポートファイルを削除中..." -ForegroundColor Yellow
    Get-ChildItem -Path $reportPath -Filter "flush_*" | Remove-Item -Force
    Write-Host "レポートファイル削除完了" -ForegroundColor Green
}

# Baketaアプリ再起動
Write-Host "Baketaアプリケーションを再起動中..." -ForegroundColor Cyan

# dotnet run で実行
Set-Location "E:\dev\Baketa"
Start-Process -FilePath "dotnet" -ArgumentList "run --project Baketa.UI" -NoNewWindow

Write-Host "Baketa.UI 再起動完了" -ForegroundColor Green
Write-Host "5秒後にプロセス確認..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

$newProcess = Get-Process -Name "Baketa.UI" -ErrorAction SilentlyContinue
if ($newProcess) {
    Write-Host "Baketa.UI 正常起動確認: ID=$($newProcess.Id)" -ForegroundColor Green
} else {
    Write-Host "Baketa.UI 起動確認中..." -ForegroundColor Yellow
}

Write-Host "PaddleOCR連続失敗カウンターリセット完了" -ForegroundColor Green
Write-Host "P0画像変化検知システムのテスト準備完了" -ForegroundColor Cyan
Write-Host "次: ゲーム画面をキャプチャしてP0動作を確認してください" -ForegroundColor Yellow