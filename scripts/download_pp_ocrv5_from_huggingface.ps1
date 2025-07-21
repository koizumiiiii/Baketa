# PP-OCRv5モデルをHuggingFaceからダウンロード
# PP-OCRv5の多言語対応モデルをダウンロードします

$ErrorActionPreference = "Stop"
$ProgressPreference = 'SilentlyContinue'

# モデル保存先ディレクトリ
$modelsDir = Join-Path $PSScriptRoot "..\models\pp-ocrv5"

if (-not (Test-Path $modelsDir)) {
    New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null
    Write-Host "✅ モデルディレクトリを作成しました: $modelsDir"
}

Write-Host "🚀 HuggingFaceからPP-OCRv5モデルをダウンロード開始..."

# PP-OCRv5 検出モデル（HuggingFace）
$detModelDir = Join-Path $modelsDir "PP-OCRv5_server_det"
if (-not (Test-Path $detModelDir)) {
    New-Item -ItemType Directory -Path $detModelDir -Force | Out-Null
    Write-Host "📁 検出モデルディレクトリ作成: $detModelDir"
}

$detFiles = @{
    "config.json" = "https://huggingface.co/PaddlePaddle/PP-OCRv5_server_det/resolve/main/config.json"
    "inference.json" = "https://huggingface.co/PaddlePaddle/PP-OCRv5_server_det/resolve/main/inference.json"
    "inference.pdiparams" = "https://huggingface.co/PaddlePaddle/PP-OCRv5_server_det/resolve/main/inference.pdiparams"
    "inference.yml" = "https://huggingface.co/PaddlePaddle/PP-OCRv5_server_det/resolve/main/inference.yml"
}

Write-Host "⬇️  検出モデルをダウンロード中..."
foreach ($file in $detFiles.Keys) {
    $filePath = Join-Path $detModelDir $file
    if (-not (Test-Path $filePath)) {
        Write-Host "   - $file をダウンロード中..."
        try {
            Invoke-WebRequest -Uri $detFiles[$file] -OutFile $filePath -UseBasicParsing
            Write-Host "   ✅ $file ダウンロード完了"
        } catch {
            Write-Host "   ❌ $file ダウンロード失敗: $_"
        }
    } else {
        Write-Host "   ⏭️  $file は既に存在します"
    }
}

# PP-OCRv5 認識モデル（HuggingFace）
$recModelDir = Join-Path $modelsDir "PP-OCRv5_server_rec"
if (-not (Test-Path $recModelDir)) {
    New-Item -ItemType Directory -Path $recModelDir -Force | Out-Null
    Write-Host "📁 認識モデルディレクトリ作成: $recModelDir"
}

$recFiles = @{
    "config.json" = "https://huggingface.co/PaddlePaddle/PP-OCRv5_server_rec/resolve/main/config.json"
    "inference.json" = "https://huggingface.co/PaddlePaddle/PP-OCRv5_server_rec/resolve/main/inference.json"
    "inference.pdiparams" = "https://huggingface.co/PaddlePaddle/PP-OCRv5_server_rec/resolve/main/inference.pdiparams"
    "inference.yml" = "https://huggingface.co/PaddlePaddle/PP-OCRv5_server_rec/resolve/main/inference.yml"
}

Write-Host "⬇️  認識モデルをダウンロード中..."
foreach ($file in $recFiles.Keys) {
    $filePath = Join-Path $recModelDir $file
    if (-not (Test-Path $filePath)) {
        Write-Host "   - $file をダウンロード中..."
        try {
            Invoke-WebRequest -Uri $recFiles[$file] -OutFile $filePath -UseBasicParsing
            Write-Host "   ✅ $file ダウンロード完了"
        } catch {
            Write-Host "   ❌ $file ダウンロード失敗: $_"
        }
    } else {
        Write-Host "   ⏭️  $file は既に存在します"
    }
}

# 分類モデル（V4の分類モデルを使用）
$clsModelDir = Join-Path $modelsDir "ch_ppocr_mobile_v2.0_cls_infer"
if (-not (Test-Path $clsModelDir)) {
    Write-Host "📝 分類モデルはV4のものを使用します（PP-OCRv5専用分類モデルはV4と同じ）"
}

Write-Host ""
Write-Host "📊 ダウンロード結果:"
Write-Host "=================="

if (Test-Path $detModelDir) {
    $detFiles = Get-ChildItem -Path $detModelDir -File
    Write-Host "✅ 検出モデル: $detModelDir"
    foreach ($file in $detFiles) {
        $sizeKB = [math]::Round($file.Length / 1KB, 1)
        Write-Host "   - $($file.Name) ($sizeKB KB)"
    }
} else {
    Write-Host "❌ 検出モデル: ダウンロード失敗"
}

if (Test-Path $recModelDir) {
    $recFiles = Get-ChildItem -Path $recModelDir -File
    Write-Host "✅ 認識モデル: $recModelDir"
    foreach ($file in $recFiles) {
        $sizeMB = [math]::Round($file.Length / 1MB, 1)
        Write-Host "   - $($file.Name) ($sizeMB MB)"
    }
} else {
    Write-Host "❌ 認識モデル: ダウンロード失敗"
}

Write-Host ""
Write-Host "✨ PP-OCRv5モデルのダウンロードが完了しました!"
Write-Host "次のステップ: Baketa.InfrastructureでPP-OCRv5モデルを使用するように設定してください。"