# 一括修正用PowerShellスクリプト - 未使用パラメーター対応
# 各ファイルの特定の行で未使用パラメーターを _ に置換

$modifications = @(
    @{ File = "E:\dev\Baketa\Baketa.Core\Translation\Models\LanguageDetectionModels.cs"; Line = 70; Pattern = 'public.*\(\s*([^,\)]+)\s*\)'; Replacement = 'public.*\(\s*_\s*\)' },
    @{ File = "E:\dev\Baketa\Baketa.Core\Translation\Models\TranslationResponse.cs"; Line = 197; Pattern = '\w+(?=\s*[,\)])'; Replacement = '_' },
    @{ File = "E:\dev\Baketa\Baketa.Core\Translation\Repositories\InMemoryTranslationRepository.cs"; Line = 48; Pattern = '\w+(?=\s*[,\)])'; Replacement = '_' },
    @{ File = "E:\dev\Baketa\Baketa.Core\Settings\Migration\V0ToV1Migration.cs"; Line = 201; Pattern = '\w+(?=\s*[,\)])'; Replacement = '_' }
)

foreach ($mod in $modifications) {
    $file = $mod.File
    $lineNum = $mod.Line
    
    if (Test-Path $file) {
        try {
            $content = Get-Content $file -Encoding UTF8
            
            if ($lineNum -le $content.Count) {
                $line = $content[$lineNum - 1]
                $originalLine = $line
                
                # 特定のパターンで末尾のパラメーター名を _ に置換
                $newLine = $line -replace '\b\w+(?=\s*[,\)\s]*$)', '_'
                
                if ($newLine -ne $originalLine) {
                    $content[$lineNum - 1] = $newLine
                    Set-Content $file -Value $content -Encoding UTF8
                    
                    Write-Host "Modified $([System.IO.Path]::GetFileName($file)) line $lineNum" -ForegroundColor Green
                    Write-Host "  OLD: $originalLine" -ForegroundColor Red
                    Write-Host "  NEW: $newLine" -ForegroundColor Green
                }
            }
        }
        catch {
            Write-Error "Failed to process $file : $_"
        }
    } else {
        Write-Warning "File not found: $file"
    }
}

Write-Host "`nBatch modification completed!" -ForegroundColor Yellow
