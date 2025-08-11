# PP-OCRv5モデルダウンロードスクリプト
# PP-OCRv5の多言語対応モデルをダウンロードします

$ErrorActionPreference = "Stop"
$ProgressPreference = 'SilentlyContinue'

# モデル保存先ディレクトリ
$modelsDir = Join-Path $PSScriptRoot "..\models\pp-ocrv5"

if (-not (Test-Path $modelsDir)) {
    New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null
    Write-Host "✅ モデルディレクトリを作成しました: $modelsDir"
}

# ダウンロードするモデルのURL
$models = @{
    # PP-OCRv5 検出モデル（テキスト検出用）
    "det" = @{
        url = "https://paddleocr.bj.bcebos.com/PP-OCRv5/chinese/det/ch_PP-OCRv5_det_infer.tar"
        name = "ch_PP-OCRv5_det_infer"
    }
    # PP-OCRv5 認識モデル（テキスト認識用）
    "rec" = @{
        url = "https://paddleocr.bj.bcebos.com/PP-OCRv5/multilingual/PP-OCRv5_multi_server_rec_infer.tar"
        name = "PP-OCRv5_multi_server_rec_infer"
    }
    # PP-OCRv5 分類モデル（テキスト方向分類用）
    "cls" = @{
        url = "https://paddleocr.bj.bcebos.com/dygraph_v2.0/ch/ch_ppocr_mobile_v2.0_cls_infer.tar"
        name = "ch_ppocr_mobile_v2.0_cls_infer"
    }
}

Write-Host "🚀 PP-OCRv5モデルのダウンロードを開始します..."

foreach ($type in $models.Keys) {
    $model = $models[$type]
    $tarPath = Join-Path $modelsDir "$($model.name).tar"
    $extractPath = Join-Path $modelsDir $model.name
    
    # すでに存在する場合はスキップ
    if (Test-Path $extractPath) {
        Write-Host "⏭️  $type モデルは既に存在します: $extractPath"
        continue
    }
    
    Write-Host "⬇️  $type モデルをダウンロード中: $($model.url)"
    
    try {
        # ダウンロード
        Invoke-WebRequest -Uri $model.url -OutFile $tarPath -UseBasicParsing
        Write-Host "✅ ダウンロード完了: $tarPath"
        
        # 展開（Windows用）
        Write-Host "📦 モデルを展開中..."
        
        # tar.exeを使用（Windows 10以降は標準搭載）
        $tarExe = "tar.exe"
        if (Get-Command $tarExe -ErrorAction SilentlyContinue) {
            & $tarExe -xf $tarPath -C $modelsDir
            Write-Host "✅ 展開完了: $extractPath"
        } else {
            Write-Host "❌ tar.exeが見つかりません。手動で展開してください: $tarPath"
        }
        
        # tarファイルを削除
        if (Test-Path $tarPath) {
            Remove-Item $tarPath -Force
            Write-Host "🗑️  一時ファイルを削除しました"
        }
        
    } catch {
        Write-Host "❌ エラーが発生しました: $_"
        if (Test-Path $tarPath) {
            Remove-Item $tarPath -Force
        }
    }
}

Write-Host ""
Write-Host "📊 ダウンロード結果:"
Write-Host "=================="

foreach ($type in $models.Keys) {
    $model = $models[$type]
    $extractPath = Join-Path $modelsDir $model.name
    
    if (Test-Path $extractPath) {
        $files = Get-ChildItem -Path $extractPath -File
        Write-Host "✅ $type モデル: $extractPath"
        foreach ($file in $files) {
            Write-Host "   - $($file.Name)"
        }
    } else {
        Write-Host "❌ $type モデル: ダウンロード失敗"
    }
}

Write-Host ""
Write-Host "✨ PP-OCRv5モデルのダウンロードが完了しました!"
Write-Host "次のステップ: Baketa.InfrastructureでPP-OCRv5モデルを使用するように設定してください。"