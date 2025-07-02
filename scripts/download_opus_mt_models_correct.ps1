# 正しいURL構造でのOPUS-MTモデルダウンロードスクリプト

param(
    [string]$ModelsDirectory = "E:\dev\Baketa\Models\SentencePiece",
    [switch]$Force = $false,
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Stop"

function Write-ColoredOutput {
    param([string]$Message, [string]$Color = "White")
    Write-Host $Message -ForegroundColor $Color
}

function Download-ModelFile {
    param(
        [string]$Url,
        [string]$OutputPath,
        [string]$ModelName
    )
    
    Write-ColoredOutput "ダウンロード開始: $ModelName" "Cyan"
    Write-ColoredOutput "URL: $Url" "DarkGray"
    Write-ColoredOutput "保存先: $OutputPath" "DarkGray"
    
    try {
        # TLS 1.2 を有効化
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        
        # ユーザーエージェントを設定
        $webClient = New-Object System.Net.WebClient
        $webClient.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36")
        
        # ダウンロード実行
        $webClient.DownloadFile($Url, $OutputPath)
        $webClient.Dispose()
        
        # 成功チェック
        if (Test-Path $OutputPath) {
            $fileInfo = Get-Item $OutputPath
            if ($fileInfo.Length -gt 1024) {  # 1KB以上
                Write-ColoredOutput "✅ ダウンロード完了: $ModelName (サイズ: $([math]::Round($fileInfo.Length / 1KB, 2)) KB)" "Green"
                return $true
            } else {
                Write-ColoredOutput "❌ ダウンロードファイルが小さすぎます: $ModelName ($($fileInfo.Length) bytes)" "Red"
                Remove-Item $OutputPath -Force
                return $false
            }
        } else {
            Write-ColoredOutput "❌ ファイル作成失敗: $ModelName" "Red"
            return $false
        }
    }
    catch {
        Write-ColoredOutput "❌ ダウンロードエラー: $ModelName" "Red"
        Write-ColoredOutput "エラー詳細: $($_.Exception.Message)" "Red"
        return $false
    }
}

Write-ColoredOutput "=== 正しいURL構造でのOPUS-MTモデルダウンロード ===" "Magenta"
Write-ColoredOutput "保存先ディレクトリ: $ModelsDirectory" "Gray"

# ディレクトリの作成
if (-not (Test-Path $ModelsDirectory)) {
    New-Item -Path $ModelsDirectory -ItemType Directory -Force | Out-Null
    Write-ColoredOutput "ディレクトリを作成しました: $ModelsDirectory" "Green"
}

# 正しいHuggingFace URL構造に基づくモデル定義
$models = @(
    @{
        Name = "opus-mt-ja-en"
        Urls = @(
            "https://huggingface.co/Helsinki-NLP/opus-mt-ja-en/resolve/main/source.spm",
            "https://huggingface.co/Helsinki-NLP/opus-mt-jap-en/resolve/main/source.spm"
        )
        FileName = "opus-mt-ja-en.model"
        Description = "日本語→英語翻訳モデル"
    },
    @{
        Name = "opus-mt-en-jap"
        Urls = @(
            "https://huggingface.co/Helsinki-NLP/opus-mt-en-jap/resolve/main/source.spm",
            "https://huggingface.co/Helsinki-NLP/opus-mt-en-ja/resolve/main/source.spm"
        )
        FileName = "opus-mt-en-ja.model"
        Description = "英語→日本語翻訳モデル"
    },
    @{
        Name = "opus-mt-zh-en"
        Urls = @(
            "https://huggingface.co/Helsinki-NLP/opus-mt-zh-en/resolve/main/source.spm"
        )
        FileName = "opus-mt-zh-en.model"
        Description = "中国語→英語翻訳モデル"
    },
    @{
        Name = "opus-mt-en-zh"
        Urls = @(
            "https://huggingface.co/Helsinki-NLP/opus-mt-en-zh/resolve/main/source.spm"
        )
        FileName = "opus-mt-en-zh.model"
        Description = "英語→中国語翻訳モデル"
    }
)

$successCount = 0
$totalCount = $models.Count

Write-ColoredOutput "`n📋 ダウンロード予定モデル ($totalCount 個):" "Yellow"
foreach ($model in $models) {
    Write-ColoredOutput "  • $($model.Name): $($model.Description)" "Gray"
}

Write-ColoredOutput "`n🚀 ダウンロード開始..." "Yellow"

# 各モデルをダウンロード
foreach ($model in $models) {
    $outputPath = Join-Path $ModelsDirectory $model.FileName
    
    # 既存ファイルのチェック
    if ((Test-Path $outputPath) -and (-not $Force)) {
        Write-ColoredOutput "⏭️  スキップ（既存）: $($model.Name)" "DarkYellow"
        $successCount++
        continue
    }
    
    # 複数URLを試行
    $downloadSuccess = $false
    foreach ($url in $model.Urls) {
        Write-ColoredOutput "🔄 試行中: $($model.Name)" "Yellow"
        if (Download-ModelFile -Url $url -OutputPath $outputPath -ModelName $model.Name) {
            $downloadSuccess = $true
            break
        }
        Write-ColoredOutput "   次のURLを試行..." "DarkYellow"
    }
    
    if ($downloadSuccess) {
        $successCount++
    } else {
        Write-ColoredOutput "❌ すべてのURLでダウンロードに失敗: $($model.Name)" "Red"
    }
    
    Write-ColoredOutput "進捗: $successCount/$totalCount 完了`n" "DarkGray"
}

# 結果サマリー
Write-ColoredOutput "=== ダウンロード結果 ===" "Magenta"
Write-ColoredOutput "✅ 成功: $successCount/$totalCount モデル" "Green"

if ($successCount -eq $totalCount) {
    Write-ColoredOutput "🎉 すべてのモデルダウンロードが完了しました！" "Green"
    
    Write-ColoredOutput "`n📁 モデルファイル一覧:" "Yellow"
    Get-ChildItem -Path $ModelsDirectory -Filter "*.model" | Where-Object { $_.Name -notlike "test-*" } | ForEach-Object {
        $sizeKB = [math]::Round($_.Length / 1KB, 2)
        Write-ColoredOutput "  • $($_.Name) (${sizeKB} KB)" "Gray"
    }
    
    Write-ColoredOutput "`n🔧 次のステップ:" "Cyan"
    Write-ColoredOutput "1. モデル検証スクリプトを実行" "Gray"
    Write-ColoredOutput "   .\scripts\verify_opus_mt_models_fixed.ps1" "DarkGray"
    Write-ColoredOutput "2. 統合テストの実行" "Gray"
    Write-ColoredOutput "   .\scripts\run_sentencepiece_tests.ps1" "DarkGray"
    
    exit 0
} elseif ($successCount -gt 0) {
    Write-ColoredOutput "⚠️  一部のモデルダウンロードに失敗しました ($successCount/$totalCount 成功)" "Yellow"
    Write-ColoredOutput "成功したモデルで開発を継続できます" "Green"
    exit 0
} else {
    Write-ColoredOutput "❌ すべてのモデルダウンロードに失敗しました" "Red"
    
    Write-ColoredOutput "`n🔧 手動ダウンロード手順:" "Yellow"
    Write-ColoredOutput "1. ブラウザで以下のURLにアクセス" "Gray"
    foreach ($model in $models) {
        Write-ColoredOutput "   • $($model.Urls[0])" "DarkGray"
    }
    Write-ColoredOutput "2. 'source.spm' または 'tokenizer.model' ファイルをダウンロード" "Gray"
    Write-ColoredOutput "3. '$ModelsDirectory' に保存" "Gray"
    
    exit 1
}
