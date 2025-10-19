# Phase 5.2C ArrayPoolメモリリーク対策 - 効果測定スクリプト
# 使用方法:
#   1. 測定1: アプリ起動直後に実行
#   2. 測定2: 翻訳1回実行後に実行
#   3. 測定3: 翻訳2回実行後に実行

param(
    [Parameter(Mandatory=$false)]
    [string]$MeasurementName = "測定$(Get-Date -Format 'HHmmss')"
)

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Phase 5.2C メモリプロファイリング" -ForegroundColor Cyan
Write-Host "測定名: $MeasurementName" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Baketaプロセスを検索
$baketaProcesses = Get-Process | Where-Object { $_.ProcessName -like "*Baketa*" }

if ($baketaProcesses.Count -eq 0) {
    Write-Host "エラー: Baketaプロセスが見つかりません" -ForegroundColor Red
    exit 1
}

# 測定結果を表示
$results = $baketaProcesses | Select-Object `
    ProcessName,
    Id,
    @{Name='StartTime';Expression={$_.StartTime}},
    @{Name='CPU(s)';Expression={[math]::Round($_.TotalProcessorTime.TotalSeconds,2)}},
    @{Name='Threads';Expression={$_.Threads.Count}},
    @{Name='Handles';Expression={$_.HandleCount}},
    @{Name='WorkingSet(MB)';Expression={[math]::Round($_.WorkingSet64/1MB,2)}},
    @{Name='PrivateMemory(MB)';Expression={[math]::Round($_.PrivateMemorySize64/1MB,2)}},
    @{Name='VirtualMemory(MB)';Expression={[math]::Round($_.VirtualMemorySize64/1MB,2)}}

$results | Format-Table -AutoSize

# 測定結果をCSVに追記
$csvPath = "E:\dev\Baketa\docs\analysis\phase52c_memory_measurements.csv"
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

foreach ($proc in $results) {
    $csvLine = [PSCustomObject]@{
        Timestamp = $timestamp
        MeasurementName = $MeasurementName
        ProcessName = $proc.ProcessName
        ProcessId = $proc.Id
        StartTime = $proc.StartTime
        'CPU(s)' = $proc.'CPU(s)'
        Threads = $proc.Threads
        Handles = $proc.Handles
        'WorkingSet(MB)' = $proc.'WorkingSet(MB)'
        'PrivateMemory(MB)' = $proc.'PrivateMemory(MB)'
        'VirtualMemory(MB)' = $proc.'VirtualMemory(MB)'
    }

    # CSVファイルが存在しない場合はヘッダー付きで作成
    if (-not (Test-Path $csvPath)) {
        $csvLine | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8
    } else {
        $csvLine | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8 -Append
    }
}

Write-Host "`n測定結果を保存しました: $csvPath`n" -ForegroundColor Green

# メモリ増加量の計算（2回目以降の測定の場合）
if (Test-Path $csvPath) {
    $allMeasurements = Import-Csv $csvPath
    $currentProcId = ($results | Select-Object -First 1).Id
    $procMeasurements = $allMeasurements | Where-Object { $_.ProcessId -eq $currentProcId } | Sort-Object Timestamp

    if ($procMeasurements.Count -gt 1) {
        Write-Host "========================================" -ForegroundColor Yellow
        Write-Host "メモリ増加量分析" -ForegroundColor Yellow
        Write-Host "========================================`n" -ForegroundColor Yellow

        $first = $procMeasurements | Select-Object -First 1
        $last = $procMeasurements | Select-Object -Last 1

        $workingSetIncrease = [math]::Round([double]$last.'WorkingSet(MB)' - [double]$first.'WorkingSet(MB)', 2)
        $privateMemoryIncrease = [math]::Round([double]$last.'PrivateMemory(MB)' - [double]$first.'PrivateMemory(MB)', 2)

        Write-Host "最初の測定: $($first.MeasurementName) ($($first.Timestamp))" -ForegroundColor Cyan
        Write-Host "  WorkingSet: $($first.'WorkingSet(MB)') MB" -ForegroundColor White
        Write-Host "  PrivateMemory: $($first.'PrivateMemory(MB)') MB" -ForegroundColor White

        Write-Host "`n現在の測定: $($last.MeasurementName) ($($last.Timestamp))" -ForegroundColor Cyan
        Write-Host "  WorkingSet: $($last.'WorkingSet(MB)') MB" -ForegroundColor White
        Write-Host "  PrivateMemory: $($last.'PrivateMemory(MB)') MB" -ForegroundColor White

        Write-Host "`n増加量:" -ForegroundColor Yellow
        Write-Host "  WorkingSet: +$workingSetIncrease MB" -ForegroundColor $(if ($workingSetIncrease -gt 100) { "Red" } else { "Green" })
        Write-Host "  PrivateMemory: +$privateMemoryIncrease MB`n" -ForegroundColor $(if ($privateMemoryIncrease -gt 100) { "Red" } else { "Green" })
    }
}

Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  - Measurement 1: After app startup (current)" -ForegroundColor White
Write-Host "  - Measurement 2: Run after 1st translation" -ForegroundColor White
Write-Host "  - Measurement 3: Run after 2nd translation" -ForegroundColor White
Write-Host ""
