# Capture and OCR Diagnostic Script

param(
    [int]$TestDurationSeconds = 120
)

$ErrorActionPreference = "Continue"

Write-Host "🔍 Baketa キャプチャ・OCR診断スクリプト" -ForegroundColor Cyan
Write-Host "=" * 60

# アプリケーション開始
$appDir = "E:\dev\Baketa\Baketa.UI\bin\x64\Debug\net8.0-windows10.0.19041.0"
$exePath = Join-Path $appDir "Baketa.UI.exe"

# 既存プロセス終了
Get-Process -Name "Baketa.UI" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host "🚀 アプリケーション開始..." -ForegroundColor Green
$process = Start-Process -FilePath $exePath -WorkingDirectory $appDir -PassThru -WindowStyle Normal

if (!$process) {
    Write-Host "❌ アプリケーションの開始に失敗" -ForegroundColor Red
    return 1
}

Write-Host "✅ アプリケーション開始成功 - PID: $($process.Id)" -ForegroundColor Green

# 初期化待機
Write-Host "⏳ 初期化待機中 (30秒)..." -ForegroundColor Yellow
Start-Sleep -Seconds 30

# 診断開始
Write-Host "`n🔍 診断開始 ($TestDurationSeconds 秒間)" -ForegroundColor Cyan
$startTime = Get-Date
$endTime = $startTime.AddSeconds($TestDurationSeconds)

# 統計収集
$stats = @{
    CaptureSuccess = 0
    CaptureFailures = 0
    MarshalExceptions = 0
    OCRTimeouts = 0
    InvalidOperations = 0
    MemoryPeakMB = 0
    WindowsCaptured = @()
}

$logPattern = @{
    CaptureSuccess = "✅ ネイティブキャプチャ成功"
    CaptureFailure = "❌ キャプチャセッションの作成に失敗"
    PrintWindowFallback = "✅ PrintWindow成功"
    MarshalException = "MarshalDirectiveException"
    OCRTimeout = "TaskCanceledException"
    InvalidOperation = "InvalidOperationException"
}

Write-Host "📊 リアルタイム監視開始..." -ForegroundColor Yellow

while ((Get-Date) -lt $endTime -and !$process.HasExited) {
    Start-Sleep -Seconds 5
    
    try {
        $currentProcess = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($currentProcess) {
            $memoryMB = [math]::Round($currentProcess.WorkingSet64/1MB, 2)
            $stats.MemoryPeakMB = [math]::Max($stats.MemoryPeakMB, $memoryMB)
            
            $elapsed = ((Get-Date) - $startTime).TotalSeconds
            Write-Host "📈 経過: $([math]::Round($elapsed))s | メモリ: ${memoryMB}MB | PID: $($process.Id)" -ForegroundColor White
            
            # CPU使用率チェック（例外ループの可能性）
            if ($currentProcess.CPU -gt 80) {
                Write-Host "⚠️ 高CPU使用率検出 - 例外ループの可能性" -ForegroundColor Red
                $stats.InvalidOperations++
            }
        }
    } catch {
        Write-Host "⚠️ プロセス状態チェックエラー: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    
    # 手動でキャプチャテスト実施（統計収集のため）
    # Note: 実際の統計は Visual Studio の出力やアプリケーションログから手動で確認
}

# 最終統計
Write-Host "`n📊 診断結果レポート" -ForegroundColor Cyan
Write-Host "=" * 60

Write-Host "🎯 アプリケーション状態:" -ForegroundColor Green
if ($process.HasExited) {
    Write-Host "   状態: 終了 (終了コード: $($process.ExitCode))" -ForegroundColor Red
} else {
    Write-Host "   状態: 実行中" -ForegroundColor Green
    Write-Host "   最終メモリ使用量: $([math]::Round((Get-Process -Id $process.Id).WorkingSet64/1MB, 2))MB" -ForegroundColor White
}

Write-Host "   最大メモリ使用量: $($stats.MemoryPeakMB)MB" -ForegroundColor White
Write-Host "   監視時間: $([math]::Round(((Get-Date) - $startTime).TotalMinutes, 1))分" -ForegroundColor White

Write-Host "`n🔍 既知の問題点:" -ForegroundColor Yellow
Write-Host "   1. 大画面ウィンドウ (2560x1080) でキャプチャ失敗"
Write-Host "   2. MarshalDirectiveException 依然発生"
Write-Host "   3. OCR処理でタイムアウト頻発"
Write-Host "   4. PrintWindow フォールバック動作"

Write-Host "`n💡 推奨調査項目:" -ForegroundColor Cyan
Write-Host "   1. Windows Graphics Capture API の権限・互換性"
Write-Host "   2. 残存するP/Invoke問題の特定"
Write-Host "   3. PP-OCRv5 モデル読み込み時の例外"
Write-Host "   4. 高解像度ウィンドウでのメモリ不足"

Write-Host "`n🎯 次のアクション:" -ForegroundColor Green
Write-Host "   1. Visual Studio デバッガーでスタックトレース取得"
Write-Host "   2. 特定サイズのウィンドウでキャプチャテスト"
Write-Host "   3. OCR処理のタイムアウト時間調整"
Write-Host "   4. 残存P/Invoke問題の個別修正"

if (!$process.HasExited) {
    Write-Host "`n✅ アプリケーションは継続実行中 - 手動テスト可能" -ForegroundColor Green
}

Write-Host "`n🏁 診断完了" -ForegroundColor Cyan
return 0