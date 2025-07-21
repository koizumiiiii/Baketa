# PP-OCRv5モデルをHuggingFaceからダウンロード（簡易版）

$ErrorActionPreference = "Stop"
$ProgressPreference = 'SilentlyContinue'

$modelsDir = Join-Path $PSScriptRoot "..\models\pp-ocrv5"

if (-not (Test-Path $modelsDir)) {
    New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null
    Write-Host "Created models directory: $modelsDir"
}

Write-Host "Starting PP-OCRv5 model download from HuggingFace..."

# 検出モデル
$detModelDir = Join-Path $modelsDir "PP-OCRv5_server_det"
if (-not (Test-Path $detModelDir)) {
    New-Item -ItemType Directory -Path $detModelDir -Force | Out-Null
}

$detFiles = @(
    @{name="config.json"; url="https://huggingface.co/PaddlePaddle/PP-OCRv5_server_det/resolve/main/config.json"},
    @{name="inference.json"; url="https://huggingface.co/PaddlePaddle/PP-OCRv5_server_det/resolve/main/inference.json"},
    @{name="inference.pdiparams"; url="https://huggingface.co/PaddlePaddle/PP-OCRv5_server_det/resolve/main/inference.pdiparams"},
    @{name="inference.yml"; url="https://huggingface.co/PaddlePaddle/PP-OCRv5_server_det/resolve/main/inference.yml"}
)

Write-Host "Downloading detection model files..."
foreach ($file in $detFiles) {
    $filePath = Join-Path $detModelDir $file.name
    if (-not (Test-Path $filePath)) {
        Write-Host "- Downloading $($file.name)..."
        Invoke-WebRequest -Uri $file.url -OutFile $filePath -UseBasicParsing
        Write-Host "  Downloaded successfully"
    } else {
        Write-Host "- $($file.name) already exists"
    }
}

# 認識モデル
$recModelDir = Join-Path $modelsDir "PP-OCRv5_server_rec"
if (-not (Test-Path $recModelDir)) {
    New-Item -ItemType Directory -Path $recModelDir -Force | Out-Null
}

$recFiles = @(
    @{name="config.json"; url="https://huggingface.co/PaddlePaddle/PP-OCRv5_server_rec/resolve/main/config.json"},
    @{name="inference.json"; url="https://huggingface.co/PaddlePaddle/PP-OCRv5_server_rec/resolve/main/inference.json"},
    @{name="inference.pdiparams"; url="https://huggingface.co/PaddlePaddle/PP-OCRv5_server_rec/resolve/main/inference.pdiparams"},
    @{name="inference.yml"; url="https://huggingface.co/PaddlePaddle/PP-OCRv5_server_rec/resolve/main/inference.yml"}
)

Write-Host "Downloading recognition model files..."
foreach ($file in $recFiles) {
    $filePath = Join-Path $recModelDir $file.name
    if (-not (Test-Path $filePath)) {
        Write-Host "- Downloading $($file.name)..."
        Invoke-WebRequest -Uri $file.url -OutFile $filePath -UseBasicParsing
        Write-Host "  Downloaded successfully"
    } else {
        Write-Host "- $($file.name) already exists"
    }
}

Write-Host ""
Write-Host "PP-OCRv5 models download completed!"
Write-Host "Detection model: $detModelDir"
Write-Host "Recognition model: $recModelDir"