# 安全なFile.AppendAllText無効化スクリプト

$ErrorActionPreference = "Stop"

# 対象ファイルのリスト
$targetFiles = @(
    "E:\dev\Baketa\Baketa.Application\Services\Translation\TranslationOrchestrationService.cs",
    "E:\dev\Baketa\Baketa.Infrastructure\OCR\PaddleOCR\Engine\PaddleOcrEngine.cs"
)

foreach ($filePath in $targetFiles) {
    Write-Host "処理中: $filePath"
    
    if (-Not (Test-Path $filePath)) {
        Write-Warning "ファイルが見つかりません: $filePath"
        continue
    }
    
    # ファイル内容を読み込み
    $content = Get-Content $filePath -Raw
    
    # バックアップ作成
    $backupPath = $filePath + ".backup_" + (Get-Date -Format "yyyyMMdd_HHmmss")
    Copy-Item $filePath $backupPath
    Write-Host "バックアップ作成: $backupPath"
    
    # File.AppendAllText行をコメントアウト（行全体をコメントアウト）
    $patterns = @(
        '^\s*(System\.IO\.)?File\.AppendAllText\(.*debug_app_logs\.txt.*$',
        '^\s*System\.IO\.File\.AppendAllText\(@?"E:\\\\dev\\\\Baketa\\\\debug_app_logs\.txt".*$'
    )
    
    $modified = $false
    
    foreach ($pattern in $patterns) {
        if ($content -match $pattern) {
            # 複数行にわたる場合も考慮した置換
            $newContent = $content -replace "(?m)^(\s*)(System\.IO\.)?File\.AppendAllText\(.*debug_app_logs\.txt.*$", "`$1// `$2File.AppendAllText(`$3 // 診断システム実装により無効化"
            
            if ($newContent -ne $content) {
                $content = $newContent
                $modified = $true
            }
        }
    }
    
    if ($modified) {
        # 変更をファイルに保存
        Set-Content -Path $filePath -Value $content -NoNewline
        Write-Host "✅ 変更完了: $filePath"
    } else {
        Write-Host "⚠️ 変更なし: $filePath"
    }
}

Write-Host "処理完了"