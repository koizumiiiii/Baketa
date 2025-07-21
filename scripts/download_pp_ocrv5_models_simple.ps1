# PP-OCRv5モデルダウンロードスクリプト（簡易版）
# PP-OCRv5の多言語対応モデルをダウンロードします

$ErrorActionPreference = "Stop"
$ProgressPreference = 'SilentlyContinue'

# モデル保存先ディレクトリ
$modelsDir = Join-Path $PSScriptRoot "..\models\pp-ocrv5"

if (-not (Test-Path $modelsDir)) {
    New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null
    Write-Host "Created models directory: $modelsDir"
}

Write-Host "Starting PP-OCRv5 model download..."

# 検出モデルダウンロード
$detUrl = "https://paddleocr.bj.bcebos.com/PP-OCRv5/chinese/det/ch_PP-OCRv5_det_infer.tar"
$detTar = Join-Path $modelsDir "ch_PP-OCRv5_det_infer.tar"
$detPath = Join-Path $modelsDir "ch_PP-OCRv5_det_infer"

if (-not (Test-Path $detPath)) {
    Write-Host "Downloading detection model..."
    Invoke-WebRequest -Uri $detUrl -OutFile $detTar -UseBasicParsing
    Write-Host "Extracting detection model..."
    tar -xf $detTar -C $modelsDir
    Remove-Item $detTar -Force
    Write-Host "Detection model ready!"
}

# 認識モデルダウンロード
$recUrl = "https://paddleocr.bj.bcebos.com/PP-OCRv5/multilingual/PP-OCRv5_multi_server_rec_infer.tar"
$recTar = Join-Path $modelsDir "PP-OCRv5_multi_server_rec_infer.tar"
$recPath = Join-Path $modelsDir "PP-OCRv5_multi_server_rec_infer"

if (-not (Test-Path $recPath)) {
    Write-Host "Downloading recognition model..."
    Invoke-WebRequest -Uri $recUrl -OutFile $recTar -UseBasicParsing
    Write-Host "Extracting recognition model..."
    tar -xf $recTar -C $modelsDir
    Remove-Item $recTar -Force
    Write-Host "Recognition model ready!"
}

# 分類モデルダウンロード
$clsUrl = "https://paddleocr.bj.bcebos.com/dygraph_v2.0/ch/ch_ppocr_mobile_v2.0_cls_infer.tar"
$clsTar = Join-Path $modelsDir "ch_ppocr_mobile_v2.0_cls_infer.tar"
$clsPath = Join-Path $modelsDir "ch_ppocr_mobile_v2.0_cls_infer"

if (-not (Test-Path $clsPath)) {
    Write-Host "Downloading classification model..."
    Invoke-WebRequest -Uri $clsUrl -OutFile $clsTar -UseBasicParsing
    Write-Host "Extracting classification model..."
    tar -xf $clsTar -C $modelsDir
    Remove-Item $clsTar -Force
    Write-Host "Classification model ready!"
}

Write-Host ""
Write-Host "PP-OCRv5 models download completed!"
Write-Host "Models saved in: $modelsDir"