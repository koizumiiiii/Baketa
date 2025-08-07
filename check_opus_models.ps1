$modelPaths = @(
    'Models\ONNX\opus-mt-ja-en.onnx',
    'Models\ONNX\opus-mt-en-jap.onnx', 
    'Models\SentencePiece\opus-mt-ja-en.model',
    'Models\SentencePiece\opus-mt-en-jap.model'
)

Write-Host '=== OPUS-MT モデルファイル配置状況調査 ===' -ForegroundColor Cyan
Write-Host ''

foreach ($path in $modelPaths) {
    $fullPath = Join-Path (Get-Location) $path
    if (Test-Path $fullPath) {
        $size = (Get-Item $fullPath).Length / 1MB
        Write-Host "✅ 存在: $path (サイズ: $([math]::Round($size, 2)) MB)" -ForegroundColor Green
    } else {
        Write-Host "❌ 不足: $path" -ForegroundColor Red
    }
}

Write-Host ''
Write-Host '=== SentencePieceディレクトリ内の全ファイル ===' -ForegroundColor Cyan
if (Test-Path 'Models\SentencePiece') {
    Get-ChildItem 'Models\SentencePiece' | ForEach-Object {
        $size = $_.Length / 1MB
        Write-Host "📄 $($_.Name) (サイズ: $([math]::Round($size, 2)) MB)" -ForegroundColor Yellow
    }
} else {
    Write-Host '❌ SentencePieceディレクトリが存在しません' -ForegroundColor Red
}

Write-Host ''
Write-Host '=== ONNXディレクトリ内の全ファイル ===' -ForegroundColor Cyan
if (Test-Path 'Models\ONNX') {
    Get-ChildItem 'Models\ONNX' | ForEach-Object {
        $size = $_.Length / 1MB
        Write-Host "📄 $($_.Name) (サイズ: $([math]::Round($size, 2)) MB)" -ForegroundColor Yellow
    }
} else {
    Write-Host '❌ ONNXディレクトリが存在しません' -ForegroundColor Red
}

Write-Host ''
Write-Host '=== AlphaOpusMtEngineFactoryが期待するファイルパス ===' -ForegroundColor Cyan
Write-Host '日本語→英語 (ja->en):'
Write-Host '  ONNXモデル: Models\ONNX\opus-mt-ja-en.onnx' -ForegroundColor White
Write-Host '  SentencePieceトークナイザー: Models\SentencePiece\opus-mt-ja-en.model' -ForegroundColor White

Write-Host ''
Write-Host '英語→日本語 (en->ja):'
Write-Host '  ONNXモデル: Models\ONNX\opus-mt-en-jap.onnx' -ForegroundColor White
Write-Host '  SentencePieceトークナイザー: Models\SentencePiece\opus-mt-en-jap.model' -ForegroundColor White