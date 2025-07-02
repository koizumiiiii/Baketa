# Baketa プロジェクトの未使用パラメーター警告を修正するスクリプト
# IDE0060 の警告を対象とする

param(
    [string]$ProjectPath = "E:\dev\Baketa",
    [switch]$DryRun = $false
)

Write-Host "Fixing unused parameter warnings (IDE0060) in Baketa project"
Write-Host "Project Path: $ProjectPath"
Write-Host "Dry Run: $DryRun"
Write-Host ""

# 修正対象のファイルと行番号のマッピング
$filesToFix = @{
    "E:\dev\Baketa\Baketa.Core\Extensions\AdvancedImageExtensions.cs" = @(40, 59, 74, 146, 163, 177, 192, 235, 269, 284, 302, 322, 336, 352, 368, 383, 403, 420, 437, 454, 470, 486, 502, 521, 536)
    "E:\dev\Baketa\Baketa.Core\Services\EnhancedSettingsService.cs" = @(597, 604, 610, 663)
    "E:\dev\Baketa\Baketa.Core\Services\Imaging\Filters\MorphologyFilter.cs" = @(92, 108)
    "E:\dev\Baketa\Baketa.Core\Services\Imaging\Pipeline\PipelineProfileManager.cs" = @(356)
    "E:\dev\Baketa\Baketa.Core\Settings\Migration\V0ToV1Migration.cs" = @(201)
    "E:\dev\Baketa\Baketa.Core\Translation\Common\TranslationExtensions.cs" = @(173, 183)
    "E:\dev\Baketa\Baketa.Core\Translation\Models\LanguageDetectionModels.cs" = @(70)
    "E:\dev\Baketa\Baketa.Core\Translation\Models\TranslationResponse.cs" = @(197)
    "E:\dev\Baketa\Baketa.Core\Translation\Repositories\InMemoryTranslationRepository.cs" = @(48)
}

function Replace-UnusedParameters {
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
                
                # パラメーター名を _ パターンに置換
                # 複数のパラメーターがある場合、連番で置換
                $parameterCount = 0
                $newLine = [regex]::Replace($line, '\b\w+(?=\s*[,\)])(?!\s*=)', {
                    param($match)
                    $paramName = $match.Value
                    
                    # 既に _ パターンの場合はスキップ
                    if ($paramName -match '^_\d*$') {
                        return $match.Value
                    }
                    
                    # 特定のキーワードはスキップ
                    if ($paramName -in @('this', 'var', 'int', 'string', 'bool', 'double', 'float', 'object', 'Task', 'async', 'await', 'public', 'private', 'protected', 'internal', 'static', 'override', 'virtual')) {
                        return $match.Value
                    }
                    
                    if ($parameterCount -eq 0) {
                        $parameterCount++
                        return "_"
                    } else {
                        $result = "_$parameterCount"
                        $parameterCount++
                        return $result
                    }
                })
                
                if ($newLine -ne $originalLine) {
                    if ($DryRun) {
                        Write-Host "DRY RUN - Would change line $lineNum in $([System.IO.Path]::GetFileName($FilePath)):"
                        Write-Host "  FROM: $originalLine"
                        Write-Host "  TO:   $newLine"
                    } else {
                        $content[$lineNum - 1] = $newLine
                        $modified = $true
                        Write-Host "Fixed line $lineNum in $([System.IO.Path]::GetFileName($FilePath))"
                    }
                }
            }
        }
        
        if ($modified -and -not $DryRun) {
            Set-Content $FilePath -Value $content -Encoding UTF8
            Write-Host "Saved changes to $([System.IO.Path]::GetFileName($FilePath))"
        }
    }
    catch {
        Write-Error "Failed to process $FilePath : $_"
    }
}

# すべてのファイルを処理
$totalFixed = 0
foreach ($file in $filesToFix.Keys) {
    $lines = $filesToFix[$file]
    Write-Host "Processing $([System.IO.Path]::GetFileName($file)) ($($lines.Count) lines)..."
    Replace-UnusedParameters -FilePath $file -LineNumbers $lines
    $totalFixed += $lines.Count
}

Write-Host ""
Write-Host "Processing complete!"
Write-Host "Total lines processed: $totalFixed"

if ($DryRun) {
    Write-Host ""
    Write-Host "This was a dry run. To apply changes, run:"
    Write-Host "  .\fix_unused_parameters.ps1 -DryRun:`$false"
}
