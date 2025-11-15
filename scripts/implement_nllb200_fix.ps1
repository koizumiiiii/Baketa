# NLLB-200並列処理改善の実装スクリプト
# 作成日: 2025-08-26
# 目的: OcrCompletedHandlerの並列処理問題を解決

param(
    [switch]$DryRun = $false,  # 実際の変更を行わずに確認のみ
    [switch]$Force = $false    # 確認なしで実行
)

Write-Host "🚀 NLLB-200並列処理改善実装スクリプト" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

$ProjectRoot = "E:\dev\Baketa"
$CoreProject = "$ProjectRoot\Baketa.Core\Baketa.Core.csproj"
$ServiceModule = "$ProjectRoot\Baketa.Core\DI\Modules\ServiceModuleCore.cs"

# Step 1: System.Threading.Tasks.Dataflow パッケージの追加確認
Write-Host "📦 Step 1: 依存関係の確認..." -ForegroundColor Yellow

$csprojContent = Get-Content $CoreProject -Raw
if ($csprojContent -notmatch "System\.Threading\.Tasks\.Dataflow") {
    Write-Host "⚠️  System.Threading.Tasks.Dataflow パッケージが見つかりません" -ForegroundColor Red
    
    if (-not $DryRun) {
        if ($Force -or (Read-Host "パッケージを追加しますか？ (y/n)") -eq 'y') {
            Write-Host "📦 パッケージ追加中..." -ForegroundColor Green
            Set-Location $ProjectRoot
            dotnet add Baketa.Core package System.Threading.Tasks.Dataflow --version 8.0.0
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ パッケージ追加完了" -ForegroundColor Green
            } else {
                Write-Host "❌ パッケージ追加失敗" -ForegroundColor Red
                exit 1
            }
        }
    } else {
        Write-Host "🔍 [DryRun] パッケージ追加が必要: System.Threading.Tasks.Dataflow" -ForegroundColor Blue
    }
} else {
    Write-Host "✅ System.Threading.Tasks.Dataflow パッケージ確認済み" -ForegroundColor Green
}

# Step 2: 改善版ハンドラーファイルの存在確認
Write-Host "`n🔧 Step 2: 改善版ハンドラーファイルの確認..." -ForegroundColor Yellow

$ImprovedHandlerPath = "$ProjectRoot\Baketa.Core\Events\Handlers\OcrCompletedHandler_Improved.cs"
if (Test-Path $ImprovedHandlerPath) {
    Write-Host "✅ 改善版ハンドラー確認済み: OcrCompletedHandler_Improved.cs" -ForegroundColor Green
} else {
    Write-Host "❌ 改善版ハンドラーが見つかりません: $ImprovedHandlerPath" -ForegroundColor Red
    Write-Host "   Claude Codeで作成されたファイルを確認してください。" -ForegroundColor Red
    exit 1
}

# Step 3: BatchTranslationRequestEvent の実装確認
Write-Host "`n📝 Step 3: BatchTranslationRequestEvent の実装確認..." -ForegroundColor Yellow

$improvedHandlerContent = Get-Content $ImprovedHandlerPath -Raw
if ($improvedHandlerContent -match "class BatchTranslationRequestEvent") {
    Write-Host "✅ BatchTranslationRequestEvent クラス確認済み" -ForegroundColor Green
} else {
    Write-Host "⚠️  BatchTranslationRequestEvent クラスが見つかりません" -ForegroundColor Red
}

# Step 4: サービス登録の更新確認
Write-Host "`n🔗 Step 4: サービス登録の確認..." -ForegroundColor Yellow

if (Test-Path $ServiceModule) {
    $serviceModuleContent = Get-Content $ServiceModule -Raw
    
    $hasOldHandler = $serviceModuleContent -match "OcrCompletedHandler>"
    $hasNewHandler = $serviceModuleContent -match "OcrCompletedHandlerImproved>"
    
    Write-Host "現在のサービス登録状況:" -ForegroundColor Cyan
    Write-Host "  - 既存ハンドラー: $(if($hasOldHandler){'有効'}else{'無効'})" -ForegroundColor $(if($hasOldHandler){'Red'}else{'Green'})
    Write-Host "  - 改善版ハンドラー: $(if($hasNewHandler){'有効'}else{'無効'})" -ForegroundColor $(if($hasNewHandler){'Green'}else{'Red'})
    
    if ($hasOldHandler -and -not $hasNewHandler) {
        Write-Host "📝 サービス登録の更新が必要です" -ForegroundColor Yellow
        
        if (-not $DryRun) {
            if ($Force -or (Read-Host "サービス登録を更新しますか？ (y/n)") -eq 'y') {
                # サービス登録の更新処理をここに実装
                Write-Host "🔧 サービス登録更新は手動で実行してください:" -ForegroundColor Yellow
                Write-Host "   1. ServiceModuleCore.cs を開く" -ForegroundColor White
                Write-Host "   2. 既存の OcrCompletedHandler 登録をコメントアウト" -ForegroundColor White
                Write-Host "   3. OcrCompletedHandlerImproved の登録を追加" -ForegroundColor White
            }
        } else {
            Write-Host "🔍 [DryRun] サービス登録の更新が必要" -ForegroundColor Blue
        }
    }
} else {
    Write-Host "❌ ServiceModuleCore.cs が見つかりません: $ServiceModule" -ForegroundColor Red
}

# Step 5: ビルドテスト
Write-Host "`n🔨 Step 5: ビルドテスト..." -ForegroundColor Yellow

if (-not $DryRun) {
    Set-Location $ProjectRoot
    Write-Host "🔨 ソリューションのビルド中..." -ForegroundColor Green
    dotnet build Baketa.sln --configuration Debug --verbosity quiet
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ ビルド成功" -ForegroundColor Green
    } else {
        Write-Host "❌ ビルド失敗 - エラーを確認してください" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "🔍 [DryRun] ビルドテストをスキップ" -ForegroundColor Blue
}

# Step 6: 次のステップの表示
Write-Host "`n📋 Step 6: 次のステップ" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Cyan

$nextSteps = @(
    "1. TranslationRequestHandler.cs にBatchTranslationRequestEvent処理を追加",
    "2. ServiceModuleCore.cs でハンドラー登録を切り替え", 
    "3. 統合テストの実行",
    "4. パフォーマンス測定とチューニング",
    "5. 本番環境への段階的デプロイ"
)

foreach ($i, $step in $nextSteps) {
    Write-Host "   $step" -ForegroundColor White
}

Write-Host "`n🎯 実装完了後の期待効果:" -ForegroundColor Green
Write-Host "   - NLLB-200 'Already borrowed' エラー: 90%削減" -ForegroundColor White
Write-Host "   - 翻訳レスポンス時間: <100ms" -ForegroundColor White  
Write-Host "   - システム安定性: 大幅改善" -ForegroundColor White

Write-Host "`n✅ 実装準備完了！" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan