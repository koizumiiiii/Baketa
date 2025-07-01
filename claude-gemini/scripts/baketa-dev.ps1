# baketa-dev.ps1 - Baketa開発統合スクリプト

param(
    [Parameter(Mandatory=$true)]
    [string]$Action,

    [Parameter(Mandatory=$false)]
    [string]$FeatureName,

    [Parameter(Mandatory=$false)]
    [string]$Description,

    [Parameter(Mandatory=$false)]
    [switch]$AutomatedMode
)

# 設定 - スクリプトの場所から相対的にプロジェクトルートを決定
$ScriptRoot = $PSScriptRoot
$ProjectRoot = (Resolve-Path "$ScriptRoot\..\..").Path
$CloudeGeminiRoot = "$ProjectRoot\claude-gemini"
$LogsDir = "$CloudeGeminiRoot\logs"
$TempDir = "$CloudeGeminiRoot\temp"
$ConfigFile = "$CloudeGeminiRoot\config.json"

# 設定ファイル読み込み
function Get-BaketaConfig {
    if (Test-Path $ConfigFile) {
        return Get-Content -Path $ConfigFile -Raw | ConvertFrom-Json
    } else {
        # デフォルト設定
        $defaultConfig = @{
            maxIterations = 3
            daysToKeepLogs = 7
            geminiLimits = @{
                perMinute = 60
                perDay = 1000
            }
            buildConfig = "Debug"
        }
        $defaultConfig | ConvertTo-Json -Depth 3 | Out-File -FilePath $ConfigFile -Encoding UTF8
        return $defaultConfig
    }
}

$config = Get-BaketaConfig

# ディレクトリ作成
@("$LogsDir\claude", "$LogsDir\gemini", "$LogsDir\integration",
  "$TempDir\feature-specs", "$TempDir\test-plans") | ForEach-Object {
    if (!(Test-Path $_)) { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
}

function Write-BaketaLog {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    Write-Host $logMessage
    Add-Content -Path "$LogsDir\integration\baketa-dev.log" -Value $logMessage
}

function Invoke-ClaudeCodeImplementation {
    param(
        [string]$FeatureName,
        [string]$Description
    )

    Write-BaketaLog "Claude Code実装開始: $FeatureName"

    $prompt = @"
Baketa Windows専用OCRオーバーレイアプリの機能実装:

機能名: $FeatureName
説明: $Description

要件:
- Windows専用アプリケーション
- クリーンアーキテクチャ（Core/Infrastructure/Application/UI）
- PaddleOCR + OpenCV画像処理
- Avalonia UI
- 非同期処理とエラー処理を適切に実装

プロジェクト構成:
- Baketa.Core: コア機能と抽象化
- Baketa.Infrastructure: プラットフォーム非依存のインフラ
- Baketa.Infrastructure.Platform: Windows固有の実装
- Baketa.Application: ビジネスロジックと機能統合
- Baketa.UI: ユーザーインターフェース (Avalonia UI)

実装してください。
"@

    try {
        $claudeOutput = & claude-code $prompt 2>&1

        $outputFile = "$LogsDir\claude\claude_${FeatureName}_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
        @{
            Feature = $FeatureName
            Description = $Description
            Timestamp = Get-Date
            Output = $claudeOutput
            Status = "Completed"
        } | ConvertTo-Json -Depth 5 | Out-File -FilePath $outputFile -Encoding UTF8

        Write-BaketaLog "Claude Code実装完了: $outputFile"
        return $outputFile
    }
    catch {
        Write-BaketaLog "Claude Code実装エラー: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

function Invoke-ClaudeCodeFix {
    param(
        [string]$FeatureName,
        [string]$PreviousErrors
    )

    Write-BaketaLog "Claude Code修正実行: $FeatureName"

    $fixPrompt = @"
前回の実装でエラーが発生しました。修正してください。

機能名: $FeatureName
エラー内容:
$PreviousErrors

修正要求:
- ビルドエラーを解決
- テストエラーを修正
- Baketa Windows専用OCRオーバーレイアプリの要件を満たす実装

修正版を実装してください。
"@

    try {
        $claudeOutput = & claude-code $fixPrompt 2>&1

        $outputFile = "$LogsDir\claude\claude_${FeatureName}_fix_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
        @{
            Feature = $FeatureName
            Type = "Fix"
            PreviousErrors = $PreviousErrors
            Timestamp = Get-Date
            Output = $claudeOutput
            Status = "Completed"
        } | ConvertTo-Json -Depth 5 | Out-File -FilePath $outputFile -Encoding UTF8

        Write-BaketaLog "Claude Code修正完了: $outputFile"
        return $outputFile
    }
    catch {
        Write-BaketaLog "Claude Code修正エラー: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

function Test-BaketaBuildWithDetails {
    Write-BaketaLog "詳細ビルド確認実行"

    try {
        Set-Location $ProjectRoot
        # 英語出力で構造化されたエラー情報を取得
        $buildOutput = & dotnet build --configuration $config.buildConfig --verbosity detailed /p:ForceEnglishOutput=true 2>&1

        $result = @{
            Success = ($LASTEXITCODE -eq 0)
            Output = $buildOutput
            Errors = ""
        }

        if (!$result.Success) {
            # より厳密なエラー抽出（エラーコードパターンでフィルタリング）
            $errorLines = $buildOutput | Where-Object {
                $_ -match "error CS\d+:" -or
                $_ -match "error MSB\d+:" -or
                $_ -match "^\s*error\s+:"
            }
            $result.Errors = $errorLines -join "`n"
            Write-BaketaLog "ビルドエラー: $($result.Errors)" "ERROR"
        } else {
            Write-BaketaLog "ビルド成功"
        }

        return $result
    }
    catch {
        Write-BaketaLog "ビルド実行エラー: $($_.Exception.Message)" "ERROR"
        return @{ Success = $false; Output = ""; Errors = $_.Exception.Message }
    }
}

function Test-BaketaTestBuildWithDetails {
    Write-BaketaLog "テストプロジェクトビルド確認実行"

    try {
        Set-Location $ProjectRoot
        $testBuildOutput = & dotnet build tests --configuration $config.buildConfig --verbosity detailed /p:ForceEnglishOutput=true 2>&1

        $result = @{
            Success = ($LASTEXITCODE -eq 0)
            Output = $testBuildOutput
            Errors = ""
        }

        if (!$result.Success) {
            $errorLines = $testBuildOutput | Where-Object {
                $_ -match "error CS\d+:" -or
                $_ -match "error MSB\d+:" -or
                $_ -match "^\s*error\s+:"
            }
            $result.Errors = $errorLines -join "`n"
            Write-BaketaLog "テストビルドエラー: $($result.Errors)" "ERROR"
        } else {
            Write-BaketaLog "テストビルド成功"
        }

        return $result
    }
    catch {
        Write-BaketaLog "テストビルド実行エラー: $($_.Exception.Message)" "ERROR"
        return @{ Success = $false; Output = ""; Errors = $_.Exception.Message }
    }
}

function Invoke-GeminiAutomatedTesting {
    param(
        [string]$ClaudeOutputFile,
        [string]$FeatureName
    )

    Write-BaketaLog "Gemini自動テスト実行: $FeatureName"

    try {
        # 1. まずテストを実行し、すべての出力をキャプチャ
        Set-Location $ProjectRoot
        Write-BaketaLog "dotnet test 実行開始"
        $testOutput = & dotnet test --logger "console;verbosity=detailed" 2>&1
        $testSuccess = ($LASTEXITCODE -eq 0)

        if ($testSuccess) {
            Write-BaketaLog "✅ 全テスト通過"
            return @{
                Success = $true;
                Errors = "";
                TestOutput = $testOutput;
                GeminiAnalysis = "テスト成功のため分析不要"
            }
        }

        # 2. 失敗時: 重要な情報のみを抽出
        Write-BaketaLog "❌ テスト失敗。エラー情報を抽出し、Geminiに分析を依頼" "WARNING"

        # 失敗に関する重要な行を抽出（前後のコンテキスト含む）
        $errorContext = $testOutput | Select-String -Pattern "FAIL", "Failed:", "Stack Trace:", "Exception:" -Context 1, 3 | Out-String

        # テストサマリー情報を抽出
        $testSummary = $testOutput | Select-Object -Last 15 | Select-String -Pattern "Passed|Failed|Skipped|Total" | Out-String

        # コンパクトなエラー情報を作成
        $conciseErrorOutput = @"
## テストサマリー
$testSummary

## エラー詳細（関連箇所抜粋）
$errorContext
"@

        # 3. 抽出した情報でGeminiに分析依頼
        $geminiPrompt = @"
以下のC#プロジェクトのテストが失敗しました。
提供されたエラー情報を分析し、根本原因と具体的な修正案をJSON形式で返してください。

$conciseErrorOutput

JSON形式で回答:
{
  "rootCause": "エラーの根本原因",
  "recommendation": "具体的な修正推奨事項",
  "priority": "high|medium|low"
}
"@

        $geminiAnalysis = & gemini cli $geminiPrompt 2>&1

        $result = @{
            Success = $false
            GeminiAnalysis = $geminiAnalysis
            TestOutput = $testOutput  # 完全なログも記録として残す
            Errors = $conciseErrorOutput  # 抽出したエラーを保存
        }

        $testLogFile = "$LogsDir\gemini\gemini_test_${FeatureName}_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
        $result | ConvertTo-Json -Depth 5 | Out-File -FilePath $testLogFile -Encoding UTF8

        Write-BaketaLog "Geminiによるエラー分析完了"
        return $result
    }
    catch {
        Write-BaketaLog "Geminiテスト実行エラー: $($_.Exception.Message)" "ERROR"
        return @{ Success = $false; Errors = $_.Exception.Message }
    }
}

function Invoke-AutomatedFeedbackLoop {
    param(
        [string]$FeatureName,
        [string]$Description,
        [int]$MaxIterations = $config.maxIterations
    )

    Write-BaketaLog "🔄 自動フィードバックループ開始: $FeatureName (最大$MaxIterations回)"

    for ($iteration = 1; $iteration -le $MaxIterations; $iteration++) {
        Write-BaketaLog "--- 反復 $iteration/$MaxIterations ---"

        if ($iteration -eq 1) {
            $claudeOutputFile = Invoke-ClaudeCodeImplementation -FeatureName $FeatureName -Description $Description
        } else {
            $claudeOutputFile = Invoke-ClaudeCodeFix -FeatureName $FeatureName -PreviousErrors $lastErrors
        }

        if (!$claudeOutputFile) {
            Write-BaketaLog "Claude Code処理失敗、ループ終了" "ERROR"
            return $false
        }

        $buildResult = Test-BaketaBuildWithDetails
        if (!$buildResult.Success) {
            Write-BaketaLog "ビルドエラー検出、Claude Codeに修正依頼" "WARNING"
            $lastErrors = "ビルドエラー:`n$($buildResult.Errors)"
            continue
        }

        $testBuildResult = Test-BaketaTestBuildWithDetails
        if (!$testBuildResult.Success) {
            Write-BaketaLog "テストビルドエラー検出、Claude Codeに修正依頼" "WARNING"
            $lastErrors = "テストビルドエラー:`n$($testBuildResult.Errors)"
            continue
        }

        $testResult = Invoke-GeminiAutomatedTesting -ClaudeOutputFile $claudeOutputFile -FeatureName $FeatureName
        if (!$testResult.Success) {
            Write-BaketaLog "テスト実行エラー検出、Claude Codeに修正依頼" "WARNING"
            $lastErrors = "テスト実行エラー:`n$($testResult.Errors)`n`nGemini分析結果:`n$($testResult.GeminiAnalysis)"
            continue
        }

        Write-BaketaLog "✅ 自動開発成功！反復回数: $iteration"
        return $true
    }

    Write-BaketaLog "❌ 最大反復数到達、手動介入が必要" "ERROR"
    return $false
}

function Start-BaketaFeatureDevelopment {
    param(
        [string]$FeatureName,
        [string]$Description,
        [switch]$AutomatedMode
    )

    Write-BaketaLog "=== Baketa機能開発開始: $FeatureName ==="

    if ($AutomatedMode) {
        $success = Invoke-AutomatedFeedbackLoop -FeatureName $FeatureName -Description $Description
        if ($success) {
            Write-BaketaLog "🎉 自動開発完了: $FeatureName"
        } else {
            Write-BaketaLog "⚠️ 自動開発失敗、手動確認が必要: $FeatureName" "WARNING"
        }
    } else {
        Write-BaketaLog "手動モードは未実装"
    }
}

# メイン処理
switch ($Action.ToLower()) {
    "develop" {
        if (!$FeatureName -or !$Description) {
            Write-Host "使用法: .\baketa-dev.ps1 -Action develop -FeatureName 'OCR最適化' -Description 'OpenCVを使用したOCR精度向上' [-AutomatedMode]"
            exit 1
        }
        Start-BaketaFeatureDevelopment -FeatureName $FeatureName -Description $Description -AutomatedMode:$AutomatedMode
    }
    "auto-develop" {
        if (!$FeatureName -or !$Description) {
            Write-Host "使用法: .\baketa-dev.ps1 -Action auto-develop -FeatureName 'OCR最適化' -Description 'OpenCVを使用したOCR精度向上'"
            exit 1
        }
        Start-BaketaFeatureDevelopment -FeatureName $FeatureName -Description $Description -AutomatedMode
    }
    "build" {
        Test-BaketaBuildWithDetails
    }
    "test" {
        Set-Location $ProjectRoot
        & dotnet test --logger "console;verbosity=detailed"
    }
    "logs" {
        Write-Host "📁 ログディレクトリ: $LogsDir"
        Get-ChildItem -Path $LogsDir -Recurse | Format-Table Name, LastWriteTime, Length
    }
    default {
        Write-Host @"
Baketa開発統合スクリプト

使用法:
  .\baketa-dev.ps1 -Action auto-develop -FeatureName '機能名' -Description '機能説明'
  .\baketa-dev.ps1 -Action build
  .\baketa-dev.ps1 -Action test
  .\baketa-dev.ps1 -Action logs

例:
  .\baketa-dev.ps1 -Action auto-develop -FeatureName 'OCR最適化' -Description 'OpenCVフィルタによるテキスト検出精度向上'
"@
    }
}