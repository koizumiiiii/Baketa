# gemini-usage-tracker.ps1 - Gemini CLI使用量追跡とクリーンアップ

param([string]$Action = "status")

$ProjectRoot = "E:\dev\Baketa"
$CloudeGeminiRoot = "$ProjectRoot\claude-gemini"
$LogsDir = "$CloudeGeminiRoot\logs"
$UsageFile = "$LogsDir\integration\gemini_usage.json"

function Initialize-UsageTracking {
    if (!(Test-Path $UsageFile)) {
        $initialUsage = @{
            dailyUsage = @{
                date = (Get-Date).ToString("yyyy-MM-dd")
                requests = 0
                features = @()
            }
            limits = @{
                perMinute = 60
                perDay = 1000
            }
            history = @()
        }
        $initialUsage | ConvertTo-Json -Depth 5 | Out-File -FilePath $UsageFile -Encoding UTF8
        Write-Host "✅ Gemini使用量追跡を初期化しました" -ForegroundColor Green
    }
}

function Get-GeminiUsageStatus {
    Initialize-UsageTracking
    $usage = Get-Content -Path $UsageFile -Raw | ConvertFrom-Json

    $today = (Get-Date).ToString("yyyy-MM-dd")

    if ($usage.dailyUsage.date -ne $today) {
        $usage.dailyUsage.requests = 0
        $usage.dailyUsage.date = $today
    }

    $remainingDaily = $usage.limits.perDay - $usage.dailyUsage.requests
    $usagePercent = [math]::Round(($usage.dailyUsage.requests / $usage.limits.perDay) * 100, 1)

    Write-Host "📊 Gemini CLI使用状況 ($today)" -ForegroundColor Cyan
    Write-Host "   今日の使用: $($usage.dailyUsage.requests) / $($usage.limits.perDay) リクエスト ($usagePercent%)" -ForegroundColor White
    Write-Host "   残り: $remainingDaily リクエスト" -ForegroundColor White

    if ($remainingDaily -lt 100) {
        Write-Host "⚠️  残りリクエスト数が少なくなっています！" -ForegroundColor Yellow
    }

    if ($remainingDaily -le 0) {
        Write-Host "❌ 今日の制限に達しました！" -ForegroundColor Red
        return $false
    }

    return $true
}

function Invoke-LogCleanup {
    param([int]$DaysToKeep = 7, [switch]$DryRun)

    Write-Host "🧹 Baketaログクリーンアップ開始 ($DaysToKeep日以前のファイルを削除)" -ForegroundColor Yellow

    $cutoffDate = (Get-Date).AddDays(-$DaysToKeep)
    $directories = @("$LogsDir\claude", "$LogsDir\gemini", "$CloudeGeminiRoot\temp\test-plans")

    $totalFilesToDelete = 0
    $totalSizeToFree = 0

    foreach ($dir in $directories) {
        if (Test-Path $dir) {
            $oldFiles = Get-ChildItem $dir | Where-Object { $_.LastWriteTime -lt $cutoffDate }

            if ($oldFiles) {
                $fileCount = $oldFiles.Count
                $totalSize = ($oldFiles | Measure-Object -Property Length -Sum).Sum
                $totalFilesToDelete += $fileCount
                $totalSizeToFree += $totalSize

                Write-Host "📂 $dir`: $fileCount ファイル ($([math]::Round($totalSize/1MB, 2)) MB)" -ForegroundColor Cyan

                if (!$DryRun) {
                    $oldFiles | Remove-Item -Force
                    Write-Host "   ✅ 削除完了" -ForegroundColor Green
                } else {
                    Write-Host "   🔍 削除予定 (DryRun)" -ForegroundColor Yellow
                }
            }
        }
    }

    # 要求履歴のクリーンアップ
    $requestFiles = @("$CloudeGeminiRoot\bridge\wsl_requests.json", "$CloudeGeminiRoot\triggers\development_requests.json")

    foreach ($file in $requestFiles) {
        if (Test-Path $file) {
            if (!$DryRun) {
                $requests = Get-Content -Path $file -Raw | ConvertFrom-Json
                $completedOld = $requests.requests | Where-Object {
                    $_.status -eq "completed" -and
                    [DateTime]$_.completedAt -lt $cutoffDate
                }

                if ($completedOld) {
                    $requests.requests = $requests.requests | Where-Object {
                        $_.status -eq "pending" -or
                        [DateTime]$_.completedAt -ge $cutoffDate
                    }

                    $requests | ConvertTo-Json -Depth 5 | Out-File -FilePath $file -Encoding UTF8
                    Write-Host "📝 $file`: $($completedOld.Count) 古い完了済み要求を削除" -ForegroundColor Green
                }
            }
        }
    }

    Write-Host ""
    Write-Host "📊 クリーンアップサマリー:" -ForegroundColor Cyan
    Write-Host "   削除ファイル数: $totalFilesToDelete" -ForegroundColor White
    Write-Host "   解放容量: $([math]::Round($totalSizeToFree/1MB, 2)) MB" -ForegroundColor White

    if ($DryRun) {
        Write-Host "   💡 実際に削除するには -DryRun を外して実行してください" -ForegroundColor Yellow
    }
}

function Show-BaketaUsageSummary {
    Write-Host "📈 Baketa開発統計サマリー" -ForegroundColor Green
    Write-Host ""

    Get-GeminiUsageStatus
    Write-Host ""

    $claudeFiles = Get-ChildItem "$LogsDir\claude" -ErrorAction SilentlyContinue | Measure-Object
    $geminiFiles = Get-ChildItem "$LogsDir\gemini" -ErrorAction SilentlyContinue | Measure-Object
    $totalSize = Get-ChildItem "$LogsDir" -Recurse -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum

    Write-Host "📁 ログファイル統計:" -ForegroundColor Cyan
    Write-Host "   Claude実装ログ: $($claudeFiles.Count) ファイル" -ForegroundColor White
    Write-Host "   Geminiテストログ: $($geminiFiles.Count) ファイル" -ForegroundColor White
    Write-Host "   総ディスク使用量: $([math]::Round($totalSize.Sum/1MB, 2)) MB" -ForegroundColor White
    Write-Host ""

    $recentFiles = Get-ChildItem "$LogsDir" -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt (Get-Date).AddDays(-7) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 5

    if ($recentFiles) {
        Write-Host "🕒 最近の開発活動:" -ForegroundColor Cyan
        $recentFiles | ForEach-Object {
            Write-Host "   $($_.LastWriteTime.ToString('MM/dd HH:mm')) - $($_.Name)" -ForegroundColor White
        }
    }
}

switch ($Action.ToLower()) {
    "status" {
        Show-BaketaUsageSummary
    }
    "cleanup" {
        Invoke-LogCleanup -DaysToKeep 7
    }
    "cleanup-dry" {
        Invoke-LogCleanup -DaysToKeep 7 -DryRun
    }
    "reset-usage" {
        if (Test-Path $UsageFile) {
            Remove-Item $UsageFile -Force
            Write-Host "✅ Gemini使用量をリセットしました" -ForegroundColor Green
        }
    }
    default {
        Write-Host @"
Gemini使用量追跡とクリーンアップ

使用法:
  .\gemini-usage-tracker.ps1 status      # 使用状況表示
  .\gemini-usage-tracker.ps1 cleanup     # 7日以前のファイル削除
  .\gemini-usage-tracker.ps1 cleanup-dry # 削除予定ファイル確認
  .\gemini-usage-tracker.ps1 reset-usage # 使用量カウンタリセット
"@ -ForegroundColor Green
    }
}