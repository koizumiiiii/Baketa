# Baketa 実装完了チェックスクリプト

param(
    [switch]$SkipTests = $false,
    [switch]$Detailed = $false,
    [string]$TestFilter = ""
)

Write-Host "=== Baketa 実装完了チェック ===" -ForegroundColor Green

# Baketaプロジェクトディレクトリに移動
Set-Location "E:\dev\Baketa"

$errors = @()
$warnings = @()
$testFailures = @()

# 1. コンパイルエラーチェック
Write-Host "`n🔍 1. コンパイルエラーチェック..." -ForegroundColor Yellow

try {
    $buildOutput = & ".\scripts\run_build.ps1" -Verbosity normal 2>&1
    $buildExitCode = $LASTEXITCODE
    
    if ($buildExitCode -eq 0) {
        Write-Host "✅ コンパイルエラー: なし" -ForegroundColor Green
    } else {
        Write-Host "❌ コンパイルエラー: あり（終了コード: $buildExitCode）" -ForegroundColor Red
        $errors += "ビルドエラー（終了コード: $buildExitCode）"
        
        # エラー詳細を抽出
        $errorLines = $buildOutput | Where-Object { $_ -match "error" -and $_ -notmatch "0 Error" }
        if ($errorLines) {
            Write-Host "エラー詳細:" -ForegroundColor Red
            $errorLines | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        }
    }
}
catch {
    Write-Host "❌ ビルド実行エラー: $($_.Exception.Message)" -ForegroundColor Red
    $errors += "ビルド実行エラー: $($_.Exception.Message)"
}

# 2. Code Analysis警告チェック
Write-Host "`n⚠️ 2. Code Analysis警告チェック..." -ForegroundColor Yellow

try {
    $warningOutput = dotnet build --verbosity normal 2>&1 | Where-Object { $_ -match "warning" }
    
    if (-not $warningOutput) {
        Write-Host "✅ Code Analysis警告: なし" -ForegroundColor Green
    } else {
        $warningCount = ($warningOutput | Measure-Object).Count
        Write-Host "⚠️ Code Analysis警告: $warningCount 件" -ForegroundColor Yellow
        $warnings += $warningOutput
        
        if ($Detailed) {
            Write-Host "警告詳細:" -ForegroundColor Yellow
            $warningOutput | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        } else {
            Write-Host "警告の詳細表示: .\scripts\check_implementation.ps1 -Detailed" -ForegroundColor Cyan
        }
    }
}
catch {
    Write-Host "❌ 警告チェック実行エラー: $($_.Exception.Message)" -ForegroundColor Red
    $errors += "警告チェック実行エラー: $($_.Exception.Message)"
}

# 3. テスト実行（オプション）
if (-not $SkipTests) {
    Write-Host "`n🧪 3. テスト実行..." -ForegroundColor Yellow
    
    try {
        $testParams = @("-Verbosity", "minimal")
        if ($TestFilter) {
            $testParams += "-Filter", $TestFilter
        }
        
        $testOutput = & ".\scripts\run_tests.ps1" @testParams 2>&1
        $testExitCode = $LASTEXITCODE
        
        if ($testExitCode -eq 0) {
            Write-Host "✅ テスト結果: 成功" -ForegroundColor Green
        } else {
            Write-Host "❌ テスト結果: 失敗（終了コード: $testExitCode）" -ForegroundColor Red
            $testFailures += "テスト失敗（終了コード: $testExitCode）"
            
            # 失敗テスト詳細を抽出
            $failedTests = $testOutput | Where-Object { $_ -match "Failed|Error" -and $_ -notmatch "0 Failed" }
            if ($failedTests -and $Detailed) {
                Write-Host "失敗テスト詳細:" -ForegroundColor Red
                $failedTests | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
            }
        }
    }
    catch {
        Write-Host "❌ テスト実行エラー: $($_.Exception.Message)" -ForegroundColor Red
        $testFailures += "テスト実行エラー: $($_.Exception.Message)"
    }
} else {
    Write-Host "⏭️ 3. テスト実行: スキップ" -ForegroundColor Gray
}

# 4. Git状態チェック（参考情報）
Write-Host "`n📝 4. Git状態確認..." -ForegroundColor Yellow

try {
    $gitStatus = git status --porcelain 2>$null
    if ($gitStatus) {
        $changedFiles = ($gitStatus | Measure-Object).Count
        Write-Host "ℹ️ 未コミット変更: $changedFiles ファイル" -ForegroundColor Cyan
    } else {
        Write-Host "✅ Git状態: クリーン" -ForegroundColor Green
    }
}
catch {
    Write-Host "ℹ️ Git状態確認: スキップ（Gitなしまたはエラー）" -ForegroundColor Gray
}

# 5. 結果サマリー
Write-Host "`n📊 実装完了チェック結果:" -ForegroundColor Cyan
Write-Host "==============================" -ForegroundColor Cyan

$totalErrors = $errors.Count
$totalWarnings = $warnings.Count
$totalTestFailures = $testFailures.Count

if ($totalErrors -eq 0) {
    Write-Host "✅ コンパイルエラー: なし" -ForegroundColor Green
} else {
    Write-Host "❌ コンパイルエラー: $totalErrors 件" -ForegroundColor Red
}

if ($totalWarnings -eq 0) {
    Write-Host "✅ Code Analysis警告: なし" -ForegroundColor Green
} else {
    Write-Host "⚠️ Code Analysis警告: $totalWarnings 件" -ForegroundColor Yellow
}

if (-not $SkipTests) {
    if ($totalTestFailures -eq 0) {
        Write-Host "✅ テスト結果: 成功" -ForegroundColor Green
    } else {
        Write-Host "❌ テスト結果: 失敗 $totalTestFailures 件" -ForegroundColor Red
    }
}

# 6. Claude Code用レポート出力
Write-Host "`n📋 Claude Code実装完了レポート:" -ForegroundColor Magenta
Write-Host "======================================" -ForegroundColor Magenta

$reportStatus = if ($totalErrors -eq 0 -and ($SkipTests -or $totalTestFailures -eq 0)) { "✅" } else { "❌" }

Write-Host "$reportStatus 実装完了チェック結果:" -ForegroundColor White
Write-Host "- コンパイルエラー: $(if ($totalErrors -eq 0) { "なし" } else { "$totalErrors 件" })" -ForegroundColor $(if ($totalErrors -eq 0) { "Green" } else { "Red" })
Write-Host "- Code Analysis警告: $(if ($totalWarnings -eq 0) { "なし" } else { "$totalWarnings 件" })" -ForegroundColor $(if ($totalWarnings -eq 0) { "Green" } else { "Yellow" })
if (-not $SkipTests) {
    Write-Host "- テスト結果: $(if ($totalTestFailures -eq 0) { "成功" } else { "失敗 $totalTestFailures 件" })" -ForegroundColor $(if ($totalTestFailures -eq 0) { "Green" } else { "Red" })
}

# 7. 推奨アクション
if ($totalErrors -gt 0 -or $totalTestFailures -gt 0) {
    Write-Host "`n🚨 要対応項目:" -ForegroundColor Red
    if ($totalErrors -gt 0) {
        Write-Host "- コンパイルエラーを修正してください" -ForegroundColor Red
    }
    if ($totalTestFailures -gt 0) {
        Write-Host "- 失敗したテストを確認・修正してください" -ForegroundColor Red
    }
    Write-Host "`n実装は完了していません。エラーを修正後に再実行してください。" -ForegroundColor Red
} elseif ($totalWarnings -gt 0) {
    Write-Host "`n💡 推奨改善:" -ForegroundColor Yellow
    Write-Host "- $totalWarnings 件の警告があります。根本原因を確認して対処することを推奨します。" -ForegroundColor Yellow
    Write-Host "`n実装は完了していますが、コード品質向上のため警告対応を検討してください。" -ForegroundColor Yellow
} else {
    Write-Host "`n🎉 実装完了！" -ForegroundColor Green
    Write-Host "すべてのチェックに合格しました。実装は正常に完了しています。" -ForegroundColor Green
}

# 8. 便利なコマンド表示
Write-Host "`n🔧 便利なコマンド:" -ForegroundColor Cyan
Write-Host "- 詳細チェック: .\scripts\check_implementation.ps1 -Detailed" -ForegroundColor Gray
Write-Host "- テストスキップ: .\scripts\check_implementation.ps1 -SkipTests" -ForegroundColor Gray
Write-Host "- 特定テスト: .\scripts\check_implementation.ps1 -TestFilter 'TestName'" -ForegroundColor Gray

# 9. 終了コード設定
if ($totalErrors -gt 0 -or $totalTestFailures -gt 0) {
    exit 1  # エラーあり
} elseif ($totalWarnings -gt 0) {
    exit 2  # 警告あり
} else {
    exit 0  # 成功
}