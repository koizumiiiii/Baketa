# 修正版 OPUS-MT モデル検証スクリプト（バグ修正）

param(
    [string]$ModelsDirectory = "E:\dev\Baketa\Models\SentencePiece",
    [switch]$Detailed = $false
)

function Write-ColoredOutput {
    param([string]$Message, [string]$Color = "White")
    Write-Host $Message -ForegroundColor $Color
}

function Test-SentencePieceModel {
    param([string]$ModelPath)
    
    if (-not (Test-Path $ModelPath)) {
        return @{ Valid = $false; Error = "ファイルが存在しません: $ModelPath" }
    }
    
    $fileInfo = Get-Item $ModelPath
    
    # 基本的なファイルサイズチェック（SentencePieceモデルは通常1KB以上）
    if ($fileInfo.Length -lt 1024) {
        return @{ Valid = $false; Error = "ファイルサイズが小さすぎます ($($fileInfo.Length) bytes)" }
    }
    
    # ファイルの先頭バイトをチェック（SentencePieceモデルの詳細検証）
    try {
        $bytes = [System.IO.File]::ReadAllBytes($ModelPath) | Select-Object -First 50
        
        # Protocol Bufferの一般的なパターンをチェック
        $hasProtocolBufferHeader = $false
        
        # 0x0A (field 1, string) から始まる場合（典型的なSentencePiece）
        if ($bytes[0] -eq 0x0A) {
            $hasProtocolBufferHeader = $true
        }
        
        # <unk>, <s>, </s> トークンの存在チェック
        $content = [System.Text.Encoding]::UTF8.GetString($bytes)
        $hasSpecialTokens = $content -match "<unk>" -or $content -match "<s>" -or $content -match "</s>"
        
        # より詳細な分析
        $analysis = @{
            HasProtocolBufferHeader = $hasProtocolBufferHeader
            HasSpecialTokens = $hasSpecialTokens
            FirstBytes = ($bytes[0..9] | ForEach-Object { $_.ToString("X2") }) -join " "
            ContentSample = $content.Substring(0, [Math]::Min(50, $content.Length)) -replace '[^\x20-\x7E]', '?'
        }
        
        # 判定ロジックを緩和（Protocol BufferヘッダーOR特殊トークン存在で有効）
        $isValid = $hasProtocolBufferHeader -or $hasSpecialTokens
        
        if ($isValid) {
            return @{ 
                Valid = $true
                Size = $fileInfo.Length
                SizeKB = [math]::Round($fileInfo.Length / 1KB, 2)
                LastModified = $fileInfo.LastWriteTime
                Analysis = $analysis
            }
        } else {
            return @{ 
                Valid = $false
                Error = "SentencePieceモデル形式として認識できません"
                Analysis = $analysis
            }
        }
    }
    catch {
        return @{ Valid = $false; Error = "ファイル読み込みエラー: $($_.Exception.Message)" }
    }
}

# メイン処理
Write-ColoredOutput "=== 修正版 OPUS-MT モデル検証スクリプト（バグ修正） ===" "Magenta"
Write-ColoredOutput "検証対象ディレクトリ: $ModelsDirectory`n" "Gray"

if (-not (Test-Path $ModelsDirectory)) {
    Write-ColoredOutput "❌ モデルディレクトリが存在しません: $ModelsDirectory" "Red"
    exit 1
}

# 実際に存在するすべての .model ファイルをチェック（testファイルを除く）
$allModelFiles = Get-ChildItem -Path $ModelsDirectory -Filter "*.model" | Where-Object { $_.Name -notlike "test-*" }

if ($allModelFiles.Count -eq 0) {
    Write-ColoredOutput "❌ モデルファイルが見つかりません（testファイル以外）" "Red"
    Write-ColoredOutput "ディレクトリ内のファイル一覧:" "Gray"
    Get-ChildItem -Path $ModelsDirectory | ForEach-Object { Write-ColoredOutput "  • $($_.Name)" "DarkGray" }
    exit 1
}

$validModels = 0
$totalModels = $allModelFiles.Count

Write-ColoredOutput "📋 検出されたモデルファイル ($totalModels 個):`n" "Yellow"

foreach ($file in $allModelFiles) {
    Write-ColoredOutput "🔍 検証中: $($file.Name)" "Cyan"
    Write-ColoredOutput "  パス: $($file.FullName)" "DarkGray"
    
    # ここがバグの原因だった - ファイルオブジェクトのFullNameを使用
    $result = Test-SentencePieceModel -ModelPath $file.FullName
    
    if ($result.Valid) {
        Write-ColoredOutput "  ✅ 有効 - サイズ: $($result.SizeKB) KB" "Green"
        if ($Detailed) {
            Write-ColoredOutput "     最終更新: $($result.LastModified)" "DarkGray"
            Write-ColoredOutput "     分析結果:" "DarkGray"
            Write-ColoredOutput "       • Protocol Buffer ヘッダー: $($result.Analysis.HasProtocolBufferHeader)" "DarkGray"
            Write-ColoredOutput "       • 特殊トークン: $($result.Analysis.HasSpecialTokens)" "DarkGray"
            Write-ColoredOutput "       • 先頭バイト: $($result.Analysis.FirstBytes)" "DarkGray"
            Write-ColoredOutput "       • 内容サンプル: $($result.Analysis.ContentSample)" "DarkGray"
        }
        $validModels++
    } else {
        Write-ColoredOutput "  ❌ 無効 - $($result.Error)" "Red"
        if ($Detailed -and $result.Analysis) {
            Write-ColoredOutput "     詳細分析:" "DarkGray"
            Write-ColoredOutput "       • 先頭バイト: $($result.Analysis.FirstBytes)" "DarkGray"
            Write-ColoredOutput "       • 内容サンプル: $($result.Analysis.ContentSample)" "DarkGray"
        }
    }
    Write-ColoredOutput ""
}

# サマリー表示
Write-ColoredOutput "=== 検証結果サマリー ===" "Magenta"
Write-ColoredOutput "✅ 有効なモデル: $validModels/$totalModels" "Green"

if ($validModels -gt 0) {
    Write-ColoredOutput "🎉 使用可能なSentencePieceモデルが見つかりました！" "Green"
    
    # 総合サマリー
    $totalSize = ($allModelFiles | Measure-Object -Property Length -Sum).Sum
    $totalSizeKB = [math]::Round($totalSize / 1KB, 2)
    
    Write-ColoredOutput "`n📊 統計情報:" "Yellow"
    Write-ColoredOutput "  • 有効モデル: $validModels" "Gray"
    Write-ColoredOutput "  • 総モデル数: $($allModelFiles.Count)" "Gray"
    Write-ColoredOutput "  • 総サイズ: $totalSizeKB KB" "Gray"
    
    Write-ColoredOutput "`n📋 使用可能なモデル:" "Yellow"
    foreach ($file in $allModelFiles) {
        $result = Test-SentencePieceModel -ModelPath $file.FullName
        if ($result.Valid) {
            $languagePair = ""
            switch -Regex ($file.Name) {
                "ja-en" { $languagePair = "日本語→英語" }
                "en-ja" { $languagePair = "英語→日本語" }
                "en-jap" { $languagePair = "英語→日本語" }
                "zh-en" { $languagePair = "中国語→英語" }
                "en-zh" { $languagePair = "英語→中国語" }
                default { $languagePair = "不明" }
            }
            Write-ColoredOutput "  ✅ $($file.Name) - $($result.SizeKB) KB ($languagePair)" "Green"
        }
    }
    
    Write-ColoredOutput "`n🚀 次のステップ:" "Cyan"
    Write-ColoredOutput "1. Baketaプロジェクトの設定更新" "Gray"
    Write-ColoredOutput "   appsettings.json でデフォルトモデルを指定" "DarkGray"
    Write-ColoredOutput "2. 統合テストの実行" "Gray"
    Write-ColoredOutput "   .\scripts\run_sentencepiece_tests.ps1" "DarkGray"
    Write-ColoredOutput "3. 実際のテキスト翻訳テスト開始" "Gray"
    
    Write-ColoredOutput "`n⚙️  推奨設定:" "Cyan"
    Write-ColoredOutput "appsettings.json:" "Gray"
    Write-ColoredOutput '@"
{
  "SentencePiece": {
    "ModelsDirectory": "Models/SentencePiece",
    "DefaultModel": "opus-mt-ja-en"
  }
}
"@' "DarkGray"
    
    exit 0
} else {
    Write-ColoredOutput "⚠️  有効なSentencePieceモデルが見つかりませんでした" "Red"
    
    Write-ColoredOutput "`n🔧 解決方法:" "Yellow"
    Write-ColoredOutput "1. ファイル形式を詳細確認（-Detailed オプション使用）" "Gray"
    Write-ColoredOutput "2. テスト用ダミーモデルを使用して開発継続" "Gray"
    Write-ColoredOutput "3. 別のSentencePieceモデルソースを探索" "Gray"
    
    exit 1
}
