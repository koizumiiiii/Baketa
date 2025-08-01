#!/usr/bin/env pwsh
# オブジェクトプールおよび非同期パフォーマンス測定システムの実装効果分析スクリプト

Write-Host "📊 オブジェクトプール・非同期パフォーマンス測定システム効果分析" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host ""

# ログファイルパス
$logPath = "E:\dev\Baketa\Baketa.UI\bin\Debug\net8.0-windows10.0.19041.0\baketa_debug.log"

# OCR処理時間の分析
Write-Host "⏱️ OCR処理時間分析:" -ForegroundColor Yellow
Write-Host "=================" -ForegroundColor Yellow

# OCR処理時間を抽出
$ocrTimes = @()
if (Test-Path $logPath) {
    $ocrLines = Select-String -Path $logPath -Pattern "PaddleOCR\.Run\(\)完了.*結果取得完了" -AllMatches
    
    foreach ($line in $ocrLines) {
        # 時刻を抽出
        if ($line.Line -match '\[(\d{2}:\d{2}:\d{2}\.\d{3})\]') {
            $endTime = [DateTime]::ParseExact($matches[1], "HH:mm:ss.fff", $null)
            
            # 対応する開始時刻を探す
            $startPattern = "PaddleOCR\.Run\(\)実行開始"
            $startLines = Select-String -Path $logPath -Pattern $startPattern -AllMatches
            
            foreach ($startLine in $startLines) {
                if ($startLine.Line -match '\[(\d{2}:\d{2}:\d{2}\.\d{3})\]') {
                    $startTime = [DateTime]::ParseExact($matches[1], "HH:mm:ss.fff", $null)
                    if ($startTime -lt $endTime) {
                        $duration = ($endTime - $startTime).TotalMilliseconds
                        $ocrTimes += $duration
                        break
                    }
                }
            }
        }
    }
}

if ($ocrTimes.Count -gt 0) {
    $avgOcrTime = ($ocrTimes | Measure-Object -Average).Average
    $minOcrTime = ($ocrTimes | Measure-Object -Minimum).Minimum
    $maxOcrTime = ($ocrTimes | Measure-Object -Maximum).Maximum
    
    Write-Host "  ✅ OCR実行回数: $($ocrTimes.Count)回" -ForegroundColor Green
    Write-Host "  ⚡ 平均処理時間: $([Math]::Round($avgOcrTime, 2))ms" -ForegroundColor White
    Write-Host "  📈 最短処理時間: $([Math]::Round($minOcrTime, 2))ms" -ForegroundColor White
    Write-Host "  📉 最長処理時間: $([Math]::Round($maxOcrTime, 2))ms" -ForegroundColor White
} else {
    Write-Host "  ❌ OCR処理時間データが見つかりませんでした" -ForegroundColor Red
}

Write-Host ""

# 画像処理パイプライン分析
Write-Host "🖼️ 画像処理パイプライン分析:" -ForegroundColor Yellow
Write-Host "=======================" -ForegroundColor Yellow

$preprocessingLines = Select-String -Path $logPath -Pattern "\[PHASE3\].*前処理.*完了" -AllMatches
if ($preprocessingLines) {
    Write-Host "  ✅ Phase3前処理実行回数: $($preprocessingLines.Count)回" -ForegroundColor Green
    
    # 前処理時間を計算
    $preprocessTimes = @()
    foreach ($line in $preprocessingLines) {
        if ($line.Line -match '\[(\d{2}:\d{2}:\d{2}\.\d{3})\].*\[PHASE3\].*前処理.*完了') {
            $endTime = [DateTime]::ParseExact($matches[1], "HH:mm:ss.fff", $null)
            
            # 対応する開始時刻を探す
            $startPattern = "\[PHASE3\].*前処理サービス開始"
            $startLine = Select-String -Path $logPath -Pattern $startPattern | Where-Object { $_.LineNumber -lt $line.LineNumber } | Select-Object -Last 1
            
            if ($startLine -and $startLine.Line -match '\[(\d{2}:\d{2}:\d{2}\.\d{3})\]') {
                $startTime = [DateTime]::ParseExact($matches[1], "HH:mm:ss.fff", $null)
                $duration = ($endTime - $startTime).TotalMilliseconds
                $preprocessTimes += $duration
            }
        }
    }
    
    if ($preprocessTimes.Count -gt 0) {
        $avgPreprocessTime = ($preprocessTimes | Measure-Object -Average).Average
        Write-Host "  ⚡ 平均前処理時間: $([Math]::Round($avgPreprocessTime, 2))ms" -ForegroundColor White
    }
} else {
    Write-Host "  ❌ Phase3前処理データが見つかりませんでした" -ForegroundColor Red
}

Write-Host ""

# メモリ効率化の理論値計算
Write-Host "💾 メモリ効率化分析（理論値）:" -ForegroundColor Yellow
Write-Host "========================" -ForegroundColor Yellow

# 画像サイズから推定
$imageSize = 2560 * 1080 * 4  # BGRA32の場合
$imagesSizeMB = [Math]::Round($imageSize / 1MB, 2)

Write-Host "  📐 処理画像サイズ: 2560x1080 (約 $imagesSizeMB MB/画像)" -ForegroundColor White

if ($ocrTimes.Count -gt 0) {
    $withoutPool = $ocrTimes.Count * $imagesSizeMB
    $withPool = [Math]::Min(50, $ocrTimes.Count) * $imagesSizeMB  # プール容量50
    $memorySaved = $withoutPool - $withPool
    $memorySavedPercent = [Math]::Round(($memorySaved / $withoutPool) * 100, 1)
    
    Write-Host "  🚫 プールなし推定メモリ使用: $([Math]::Round($withoutPool, 2)) MB" -ForegroundColor Red
    Write-Host "  ✅ プールあり推定メモリ使用: $([Math]::Round($withPool, 2)) MB" -ForegroundColor Green
    Write-Host "  💰 メモリ削減量: $([Math]::Round($memorySaved, 2)) MB ($memorySavedPercent%)" -ForegroundColor Green
}

Write-Host ""

# 実装状況サマリー
Write-Host "📋 実装効果サマリー:" -ForegroundColor Cyan
Write-Host "==================" -ForegroundColor Cyan

Write-Host ""
Write-Host "✅ 実装完了コンポーネント:" -ForegroundColor Green
Write-Host "  • IAdvancedImagePool (容量: 50) - 画像オブジェクト再利用" -ForegroundColor White
Write-Host "  • ITextRegionPool (容量: 200) - OCR結果領域再利用" -ForegroundColor White
Write-Host "  • IObjectPool<IMatWrapper> (容量: 30) - OpenCV Mat再利用" -ForegroundColor White
Write-Host "  • IAsyncPerformanceAnalyzer - 非同期処理性能測定" -ForegroundColor White
Write-Host "  • ObjectPoolStatisticsReporter - 統計レポート機能" -ForegroundColor White

Write-Host ""
Write-Host "⚠️ 現在の状況:" -ForegroundColor Yellow
Write-Host "  • オブジェクトプールは実装済みだが、ログ出力が未実装" -ForegroundColor White
Write-Host "  • 実際のプール使用統計を取得するにはログ実装が必要" -ForegroundColor White
Write-Host "  • OCR処理は正常に動作（平均 $([Math]::Round($avgOcrTime, 0))ms）" -ForegroundColor White

Write-Host ""
Write-Host "📈 期待される改善効果:" -ForegroundColor Cyan
Write-Host "  • メモリ効率: 60-80% オブジェクト作成削減" -ForegroundColor White
Write-Host "  • GC負荷: 40-70% 削減" -ForegroundColor White
Write-Host "  • 処理速度: 15-30% 改善（オブジェクト再利用による）" -ForegroundColor White

Write-Host ""
Write-Host "🔄 次のステップ:" -ForegroundColor Green
Write-Host "  1. ObjectPoolStatisticsReporter のログ出力実装" -ForegroundColor White
Write-Host "  2. AsyncPerformanceAnalyzer の測定結果ログ実装" -ForegroundColor White
Write-Host "  3. プール有効/無効での比較測定実施" -ForegroundColor White
Write-Host ""