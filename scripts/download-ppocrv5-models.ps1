# PP-OCRv5 ONNX Model Download Script
# Issue #181: GPU/CPU auto-switching support
# Source: https://huggingface.co/monkt/paddleocr-onnx

param(
    [string]$OutputDir = "$PSScriptRoot\..\models\ppocrv5-onnx"
)

$ErrorActionPreference = "Stop"

# HuggingFace repository base URL
$RepoBase = "https://huggingface.co/monkt/paddleocr-onnx/resolve/main"

# Files to download
# Note: Japanese uses Chinese model (PP-OCRv5 Chinese model supports Japanese recognition)
$Files = @(
    # Detection model (v5)
    @{ Url = "$RepoBase/detection/v5/det.onnx"; Path = "detection/det.onnx" },

    # Chinese recognition model (also supports Japanese)
    @{ Url = "$RepoBase/languages/chinese/rec.onnx"; Path = "languages/chinese/rec.onnx" },
    @{ Url = "$RepoBase/languages/chinese/dict.txt"; Path = "languages/chinese/dict.txt" },

    # Latin (English) recognition model
    @{ Url = "$RepoBase/languages/latin/rec.onnx"; Path = "languages/latin/rec.onnx" },
    @{ Url = "$RepoBase/languages/latin/dict.txt"; Path = "languages/latin/dict.txt" }
)

Write-Host "PP-OCRv5 ONNX Model Download Started" -ForegroundColor Cyan
Write-Host "Output Directory: $OutputDir" -ForegroundColor Gray

# Create output directory
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$TotalFiles = $Files.Count
$CurrentFile = 0

foreach ($File in $Files) {
    $CurrentFile++
    $DestPath = Join-Path $OutputDir $File.Path
    $DestDir = Split-Path $DestPath -Parent

    # Create directory
    if (-not (Test-Path $DestDir)) {
        New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
    }

    # Check existing file
    if (Test-Path $DestPath) {
        Write-Host "[$CurrentFile/$TotalFiles] Skipped (exists): $($File.Path)" -ForegroundColor Yellow
        continue
    }

    Write-Host "[$CurrentFile/$TotalFiles] Downloading: $($File.Path)" -ForegroundColor White

    try {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $File.Url -OutFile $DestPath -UseBasicParsing
        $FileSize = (Get-Item $DestPath).Length / 1MB
        $FileSizeStr = [math]::Round($FileSize, 2)
        Write-Host "  -> Done ($FileSizeStr MB)" -ForegroundColor Green
    }
    catch {
        Write-Host "  -> Error: $_" -ForegroundColor Red
        if (Test-Path $DestPath) {
            Remove-Item $DestPath -Force
        }
    }
}

Write-Host ""
Write-Host "Download Complete!" -ForegroundColor Green
Write-Host ""

# Show downloaded files
Write-Host "Downloaded Files:" -ForegroundColor Cyan
Get-ChildItem -Path $OutputDir -Recurse -File | ForEach-Object {
    $RelPath = $_.FullName.Replace($OutputDir, "").TrimStart("\")
    $Size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  $RelPath - $Size MB"
}
