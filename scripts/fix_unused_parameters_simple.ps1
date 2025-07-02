# 未使用パラメーター一括修正スクリプト (簡易版)
# 特定のファイルの特定の行で、最後のパラメーターを _ に置換

$files = @{
    "E:\dev\Baketa\Baketa.Core\Services\Imaging\Pipeline\PipelineProfileManager.cs" = @(356)
    "E:\dev\Baketa\Baketa.Core\Settings\Migration\V0ToV1Migration.cs" = @(201)
    "E:\dev\Baketa\Baketa.Core\Translation\Common\TranslationExtensions.cs" = @(173, 183)
    "E:\dev\Baketa\Baketa.Core\Translation\Models\LanguageDetectionModels.cs" = @(70)
    "E:\dev\Baketa\Baketa.Core\Translation\Models\TranslationResponse.cs" = @(197)
    "E:\dev\Baketa\Baketa.Core\Translation\Repositories\InMemoryTranslationRepository.cs" = @(48)
    "E:\dev\Baketa\Baketa.Infrastructure\Capture\DifferenceDetection\EdgeDifferenceAlgorithm.cs" = @(140, 193)
    "E:\dev\Baketa\Baketa.Infrastructure\Capture\DifferenceDetection\HistogramDifferenceAlgorithm.cs" = @(237, 252, 270)
    "E:\dev\Baketa\Baketa.Infrastructure\Capture\DifferenceDetection\PixelDifferenceAlgorithm.cs" = @(227)
    "E:\dev\Baketa\Baketa.Infrastructure\OCR\PaddleOCR\Engine\PaddleOcrEngine.cs" = @(678)
    "E:\dev\Baketa\Baketa.Infrastructure\OCR\TextDetection\MserTextRegionDetector.cs" = @(152)
    "E:\dev\Baketa\Baketa.Infrastructure\OCR\TextDetection\SwtTextRegionDetector.cs" = @(204, 207)
    "E:\dev\Baketa\Baketa.Infrastructure\Services\EnhancedSettingsService.cs" = @(647)
    "E:\dev\Baketa\Baketa.Infrastructure\Translation\Hybrid\HybridTranslationEngine.cs" = @(261)
    "E:\dev\Baketa\Baketa.Infrastructure\Translation\Local\Onnx\Chinese\ChineseTranslationEngine.cs" = @(412, 438, 607)
}

function Fix-UnusedParametersInFile {
    param(
        [string]$FilePath,
        [int[]]$LineNumbers
    )
    
    if (-not (Test-Path $FilePath)) {
        Write-Warning "File not found: $FilePath"
        return
    }
    
    try {
        $content = Get-Content $FilePath -Encoding UTF8
        $modified = $false
        
        foreach ($lineNum in $LineNumbers) {
            if ($lineNum -le $content.Count) {
                $line = $content[$lineNum - 1]
                $originalLine = $line
                
                # 最後のパラメーター名を _ に置換するパターン
                # メソッドパラメーターの最後の識別子を _、_1、_2 などに置換
                $newLine = $line -replace '\b(\w+)(?=\s*[,\)])', {
                    param($match)
                    $paramName = $match.Groups[1].Value
                    
                    # 特定のキーワードはスキップ
                    if ($paramName -in @('this', 'var', 'int', 'string', 'bool', 'double', 'float', 'object', 'Task', 'async', 'await', 'public', 'private', 'protected', 'internal', 'static', 'override', 'virtual', 'void', 'return', 'throw', 'new', 'class', 'interface', 'namespace')) {
                        return $match.Value
                    }
                    
                    # 既に _ パターンの場合はスキップ
                    if ($paramName -match '^_\d*$') {
                        return $match.Value
                    }
                    
                    # 型名らしきものはスキップ
                    if ($paramName -match '^[A-Z][a-zA-Z]*$' -and $paramName.Length -gt 1) {
                        return $match.Value
                    }
                    
                    return "_"
                }
                
                if ($newLine -ne $originalLine) {
                    $content[$lineNum - 1] = $newLine
                    $modified = $true
                    Write-Host "Fixed line $lineNum in $([System.IO.Path]::GetFileName($FilePath))"
                    Write-Host "  OLD: $originalLine"
                    Write-Host "  NEW: $newLine"
                }
            }
        }
        
        if ($modified) {
            Set-Content $FilePath -Value $content -Encoding UTF8
            Write-Host "Saved changes to $([System.IO.Path]::GetFileName($FilePath))" -ForegroundColor Green
        }
    }
    catch {
        Write-Error "Failed to process $FilePath : $_"
    }
}

Write-Host "Starting unused parameter fixes..." -ForegroundColor Yellow

$totalFiles = 0
foreach ($file in $files.Keys) {
    $lines = $files[$file]
    Write-Host "`nProcessing $([System.IO.Path]::GetFileName($file)) ($($lines.Count) lines)..." -ForegroundColor Cyan
    Fix-UnusedParametersInFile -FilePath $file -LineNumbers $lines
    $totalFiles++
}

Write-Host "`nCompleted processing $totalFiles files!" -ForegroundColor Green
