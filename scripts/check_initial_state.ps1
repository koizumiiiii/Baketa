$process = Get-Process -Name "Baketa.UI" -ErrorAction SilentlyContinue

if ($process) {
    $memMB = [Math]::Round($process.WorkingSet64/1MB,2)
    $threads = $process.Threads.Count
    $handles = $process.HandleCount

    Write-Host "✅ 初期状態:" -ForegroundColor Green
    Write-Host "  メモリ: $memMB MB"
    Write-Host "  スレッド: $threads"
    Write-Host "  ハンドル: $handles"
}
else {
    Write-Host "❌ プロセスが見つかりません" -ForegroundColor Red
}
