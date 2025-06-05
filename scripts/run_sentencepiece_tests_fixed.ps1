# 修正版 SentencePiece統合テスト実行スクリプト

param(
    [string]$ProjectRoot = "E:\dev\Baketa",
    [switch]$RunUnitTests = $true,
    [switch]$RunIntegrationTests = $true,
    [switch]$RunPerformanceTests = $false,
    [switch]$Verbose = $false
)

function Write-ColoredOutput {
    param([string]$Message, [string]$Color = "White")
    Write-Host $Message -ForegroundColor $Color
}

function Run-TestProject {
    param(
        [string]$ProjectPath,
        [string]$TestName,
        [string]$Filter = $null
    )
    
    Write-ColoredOutput "`n🧪 実行中: $TestName" "Cyan"
    Write-ColoredOutput "プロジェクト: $ProjectPath" "DarkGray"
    
    if (-not (Test-Path $ProjectPath)) {
        Write-ColoredOutput "❌ プロジェクトファイルが見つかりません: $ProjectPath" "Red"
        return $false
    }
    
    try {
        $testArgs = @("test", $ProjectPath)
        if ($Filter) {
            $testArgs += "--filter", $Filter
        }
        if ($Verbose) {
            $testArgs += "--verbosity", "detailed"
        } else {
            $testArgs += "--verbosity", "normal"
        }
        
        # エラー出力をキャプチャ
        $output = & dotnet @testArgs 2>&1
        $exitCode = $LASTEXITCODE
        
        if ($exitCode -eq 0) {
            Write-ColoredOutput "✅ $TestName - 成功" "Green"
            return $true
        } else {
            Write-ColoredOutput "❌ $TestName - 失敗 (終了コード: $exitCode)" "Red"
            if ($Verbose -or $exitCode -ne 0) {
                Write-ColoredOutput "詳細出力:" "Yellow"
                $output | ForEach-Object { Write-ColoredOutput "  $_" "Gray" }
            }
            return $false
        }
    }
    catch {
        Write-ColoredOutput "❌ $TestName - 実行エラー: $($_.Exception.Message)" "Red"
        return $false
    }
}

# メイン処理
Write-ColoredOutput "=== 修正版 SentencePiece統合テスト実行 ===" "Magenta"
Write-ColoredOutput "プロジェクトルート: $ProjectRoot`n" "Gray"

# 作業ディレクトリを設定
Set-Location $ProjectRoot

# プロジェクトファイルの確認
$testProjectPath = "tests\Baketa.Infrastructure.Tests\Baketa.Infrastructure.Tests.csproj"
if (-not (Test-Path $testProjectPath)) {
    Write-ColoredOutput "❌ テストプロジェクトが見つかりません: $testProjectPath" "Red"
    Write-ColoredOutput "プロジェクト構造を確認してください" "Yellow"
    
    Write-ColoredOutput "`n📁 現在のディレクトリ内容:" "Yellow"
    Get-ChildItem | ForEach-Object { Write-ColoredOutput "  $($_.Name)" "Gray" }
    
    if (Test-Path "tests") {
        Write-ColoredOutput "`n📁 testsディレクトリ内容:" "Yellow"
        Get-ChildItem "tests" | ForEach-Object { Write-ColoredOutput "  $($_.Name)" "Gray" }
    }
    
    exit 1
}

# モデルファイルの確認
$modelsDir = "Models\SentencePiece"
if (-not (Test-Path $modelsDir)) {
    Write-ColoredOutput "⚠️  モデルディレクトリが存在しません: $modelsDir" "Yellow"
} else {
    $modelCount = (Get-ChildItem $modelsDir -Filter "*.model" | Where-Object { $_.Name -notlike "test-*" }).Count
    Write-ColoredOutput "📁 モデルファイル数: $modelCount" "Green"
}

# ソリューション全体のビルド
Write-ColoredOutput "🔨 ソリューション全体のビルド実行中..." "Yellow"
$buildOutput = & dotnet build --no-restore --configuration Release 2>&1
$buildExitCode = $LASTEXITCODE

if ($buildExitCode -ne 0) {
    Write-ColoredOutput "❌ ビルドに失敗しました" "Red"
    if ($Verbose) {
        Write-ColoredOutput "ビルド出力:" "Yellow"
        $buildOutput | ForEach-Object { Write-ColoredOutput "  $_" "Gray" }
    }
    exit 1
}
Write-ColoredOutput "✅ ビルド成功" "Green"

# テスト実行
$testResults = @()

if ($RunUnitTests) {
    Write-ColoredOutput "`n📋 SentencePiece関連テスト実行" "Yellow"
    
    # SentencePiece関連のすべてのテストを実行
    $result = Run-TestProject -ProjectPath $testProjectPath -TestName "SentencePiece関連テスト" -Filter "*SentencePiece*"
    $testResults += @{ Name = "SentencePiece関連テスト"; Success = $result; Type = "Unit" }
}

if ($RunIntegrationTests) {
    Write-ColoredOutput "`n📋 統合テスト実行" "Yellow"
    $result = Run-TestProject -ProjectPath $testProjectPath -TestName "統合テスト" -Filter "*Integration*"
    $testResults += @{ Name = "統合テスト"; Success = $result; Type = "Integration" }
}

if ($RunPerformanceTests) {
    Write-ColoredOutput "`n📋 パフォーマンステスト実行" "Yellow"
    $result = Run-TestProject -ProjectPath $testProjectPath -TestName "パフォーマンステスト" -Filter "Category=Performance"
    $testResults += @{ Name = "パフォーマンステスト"; Success = $result; Type = "Performance" }
}

# 結果サマリー
Write-ColoredOutput "`n=== テスト結果サマリー ===" "Magenta"

$successCount = ($testResults | Where-Object { $_.Success }).Count
$totalCount = $testResults.Count

Write-ColoredOutput "✅ 成功: $successCount/$totalCount テストスイート" "Green"

if ($totalCount -gt 0) {
    Write-ColoredOutput "`n詳細結果:" "Yellow"
    foreach ($result in $testResults) {
        $status = if ($result.Success) { "✅" } else { "❌" }
        $color = if ($result.Success) { "Green" } else { "Red" }
        Write-ColoredOutput "  $status $($result.Name) ($($result.Type))" $color
    }
}

if ($successCount -eq $totalCount -and $totalCount -gt 0) {
    Write-ColoredOutput "`n🎉 すべてのテストが成功しました！" "Green"
    
    Write-ColoredOutput "`n🚀 次のステップ:" "Cyan"
    Write-ColoredOutput "1. 実際のBaketaアプリケーションでの動作確認" "Gray"
    Write-ColoredOutput "2. UI統合テスト" "Gray"
    Write-ColoredOutput "3. 長時間動作テスト" "Gray"
    Write-ColoredOutput "4. Gemini API統合の開始" "Gray"
    
    exit 0
} else {
    if ($totalCount -eq 0) {
        Write-ColoredOutput "⚠️  実行されたテストがありません" "Yellow"
    } else {
        $failedCount = $totalCount - $successCount
        Write-ColoredOutput "`n⚠️  $failedCount 個のテストスイートが失敗しました" "Red"
    }
    
    Write-ColoredOutput "`n🔧 確認事項:" "Yellow"
    Write-ColoredOutput "1. モデルファイルが正しく配置されているか" "Gray"
    Write-ColoredOutput "2. appsettings.json が正しく設定されているか" "Gray"
    Write-ColoredOutput "3. Microsoft.ML.Tokenizers の依存関係" "Gray"
    Write-ColoredOutput "4. テストプロジェクトの参照設定" "Gray"
    
    Write-ColoredOutput "`n🔍 詳細確認コマンド:" "Cyan"
    Write-ColoredOutput "dotnet test ""$testProjectPath"" --filter ""*SentencePiece*"" --verbosity detailed" "DarkGray"
    
    exit 1
}
