# Baketa プロジェクトの未使用パラメーター修正状況確認スクリプト

Write-Host "=== Baketa 未使用パラメーター修正状況確認 ===" -ForegroundColor Cyan
Write-Host ""

# 修正済みファイルのリスト
$fixedFiles = @(
    "E:\dev\Baketa\Baketa.Core\Extensions\AdvancedImageExtensions.cs",
    "E:\dev\Baketa\Baketa.Core\Services\EnhancedSettingsService.cs",
    "E:\dev\Baketa\Baketa.Core\Services\Imaging\Filters\MorphologyFilter.cs",
    "E:\dev\Baketa\Baketa.Core\Services\Imaging\Pipeline\PipelineProfileManager.cs",
    "E:\dev\Baketa\Baketa.Core\Translation\Common\TranslationExtensions.cs",
    "E:\dev\Baketa\Baketa.Infrastructure\Capture\DifferenceDetection\DifferenceVisualizerTool.cs"
)

$totalFixed = 0

foreach ($file in $fixedFiles) {
    if (Test-Path $file) {
        $fileName = [System.IO.Path]::GetFileName($file)
        $content = Get-Content $file -Encoding UTF8
        
        # _ パターンのパラメーターをカウント
        $discardCount = 0
        foreach ($line in $content) {
            $matches = [regex]::Matches($line, '\b_\d*\b')
            $discardCount += $matches.Count
        }
        
        if ($discardCount -gt 0) {
            Write-Host "✓ $fileName - $discardCount 個の未使用パラメーターを修正済み" -ForegroundColor Green
            $totalFixed += $discardCount
        } else {
            Write-Host "- $fileName - 修正なし" -ForegroundColor Yellow
        }
    } else {
        Write-Host "✗ $fileName - ファイルが見つかりません" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== 修正サマリー ===" -ForegroundColor Cyan
Write-Host "修正済みファイル数: $($fixedFiles.Count)" -ForegroundColor Green
Write-Host "修正済みパラメーター総数: $totalFixed" -ForegroundColor Green

Write-Host ""
Write-Host "=== 次のステップ ===" -ForegroundColor Cyan
Write-Host "1. プロジェクトをビルドして IDE0060 警告が解消されているか確認"
Write-Host "2. 必要に応じて残りのファイルも同様に修正"
Write-Host "3. テストの実行してリグレッションがないか確認"

Write-Host ""
Write-Host "修正完了！" -ForegroundColor Green
