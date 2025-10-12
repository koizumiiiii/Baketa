# Phase 5.2D統合テスト - メモリ/スレッド監視スクリプト
param(
    [int]$DurationSeconds = 120,
    [string]$LogFile = "E:\dev\Baketa\phase5.2d_test_results.log"
)

Write-Host "🔍 Phase 5.2D統合テスト開始" -ForegroundColor Green
Write-Host "監視時間: $DurationSeconds 秒" -ForegroundColor Cyan
Write-Host "ログファイル: $LogFile" -ForegroundColor Cyan

# ログファイル初期化
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
"" | Out-File -FilePath $LogFile -Encoding UTF8
"Phase 5.2D統合テスト結果 - $timestamp" | Out-File -FilePath $LogFile -Append -Encoding UTF8
"=" * 80 | Out-File -FilePath $LogFile -Append -Encoding UTF8
"" | Out-File -FilePath $LogFile -Append -Encoding UTF8

$interval = 5
$iterations = [Math]::Ceiling($DurationSeconds / $interval)

Write-Host "`n📊 監視開始..." -ForegroundColor Yellow

for ($i = 0; $i -lt $iterations; $i++) {
    $elapsed = $i * $interval

    try {
        $process = Get-Process -Name "Baketa.UI" -ErrorAction Stop

        $memoryMB = [Math]::Round($process.WorkingSet64 / 1MB, 2)
        $threads = $process.Threads.Count
        $handles = $process.HandleCount

        $output = "[{0:D3}s] メモリ: {1:N2}MB | スレッド: {2:D3} | ハンドル: {3:D4}" -f $elapsed, $memoryMB, $threads, $handles

        Write-Host $output -ForegroundColor White
        $output | Out-File -FilePath $LogFile -Append -Encoding UTF8

        # 異常検知
        if ($memoryMB -gt 100) {
            $warning = "  ⚠️ 警告: メモリ使用量が100MBを超過しました！"
            Write-Host $warning -ForegroundColor Red
            $warning | Out-File -FilePath $LogFile -Append -Encoding UTF8
        }

        if ($threads -gt 30) {
            $warning = "  ⚠️ 警告: スレッド数が30を超過しました！"
            Write-Host $warning -ForegroundColor Red
            $warning | Out-File -FilePath $LogFile -Append -Encoding UTF8
        }
    }
    catch {
        $error = "[{0:D3}s] ❌ Baketa.UIプロセスが見つかりません" -f $elapsed
        Write-Host $error -ForegroundColor Red
        $error | Out-File -FilePath $LogFile -Append -Encoding UTF8
        break
    }

    Start-Sleep -Seconds $interval
}

Write-Host "`n✅ 監視完了" -ForegroundColor Green
Write-Host "ログファイル: $LogFile" -ForegroundColor Cyan

# 最終結果サマリー
try {
    $process = Get-Process -Name "Baketa.UI" -ErrorAction Stop

    "" | Out-File -FilePath $LogFile -Append -Encoding UTF8
    "=" * 80 | Out-File -FilePath $LogFile -Append -Encoding UTF8
    "最終結果サマリー" | Out-File -FilePath $LogFile -Append -Encoding UTF8
    "=" * 80 | Out-File -FilePath $LogFile -Append -Encoding UTF8

    $finalMemoryMB = [Math]::Round($process.WorkingSet64 / 1MB, 2)
    $finalThreads = $process.Threads.Count
    $finalHandles = $process.HandleCount

    "最終メモリ: $finalMemoryMB MB" | Out-File -FilePath $LogFile -Append -Encoding UTF8
    "最終スレッド数: $finalThreads" | Out-File -FilePath $LogFile -Append -Encoding UTF8
    "最終ハンドル数: $finalHandles" | Out-File -FilePath $LogFile -Append -Encoding UTF8

    "" | Out-File -FilePath $LogFile -Append -Encoding UTF8
    "期待値との比較:" | Out-File -FilePath $LogFile -Append -Encoding UTF8

    if ($finalMemoryMB -le 50) {
        "✅ メモリ: 合格 ($finalMemoryMB MB <= 50 MB)" | Out-File -FilePath $LogFile -Append -Encoding UTF8
        Write-Host "✅ メモリ: 合格 ($finalMemoryMB MB <= 50 MB)" -ForegroundColor Green
    }
    else {
        "❌ メモリ: 不合格 ($finalMemoryMB MB > 50 MB)" | Out-File -FilePath $LogFile -Append -Encoding UTF8
        Write-Host "❌ メモリ: 不合格 ($finalMemoryMB MB > 50 MB)" -ForegroundColor Red
    }

    if ($finalThreads -le 20) {
        "✅ スレッド: 合格 ($finalThreads <= 20)" | Out-File -FilePath $LogFile -Append -Encoding UTF8
        Write-Host "✅ スレッド: 合格 ($finalThreads <= 20)" -ForegroundColor Green
    }
    else {
        "❌ スレッド: 不合格 ($finalThreads > 20)" | Out-File -FilePath $LogFile -Append -Encoding UTF8
        Write-Host "❌ スレッド: 不合格 ($finalThreads > 20)" -ForegroundColor Red
    }

    if ($finalHandles -le 500) {
        "✅ ハンドル: 合格 ($finalHandles <= 500)" | Out-File -FilePath $LogFile -Append -Encoding UTF8
        Write-Host "✅ ハンドル: 合格 ($finalHandles <= 500)" -ForegroundColor Green
    }
    else {
        "❌ ハンドル: 不合格 ($finalHandles > 500)" | Out-File -FilePath $LogFile -Append -Encoding UTF8
        Write-Host "❌ ハンドル: 不合格 ($finalHandles > 500)" -ForegroundColor Red
    }
}
catch {
    "プロセスが終了しました" | Out-File -FilePath $LogFile -Append -Encoding UTF8
}
