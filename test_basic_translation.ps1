#!/usr/bin/env pwsh

# 基本的な翻訳テストの実行と結果取得
Write-Host "=== 基本翻訳テスト開始 ===" -ForegroundColor Green

cd "E:\dev\Baketa"

# テスト実行と出力の取得
$output = dotnet test tests/Baketa.Infrastructure.Tests/ --filter "SimpleTranslationDebugTest" --verbosity normal --logger "console;verbosity=detailed" 2>&1

# 結果をファイルに保存
$output | Out-File -FilePath "test_output.txt" -Encoding UTF8

# 翻訳結果の部分だけを抽出
Write-Host "=== 翻訳結果の抽出 ===" -ForegroundColor Yellow

$lines = $output -split "`n"
$inResults = $false

foreach ($line in $lines) {
    if ($line -match "=== テスト開始") {
        $inResults = $true
        Write-Host $line -ForegroundColor Cyan
    } elseif ($line -match "入力:" -or $line -match "結果:" -or $line -match "→") {
        Write-Host $line -ForegroundColor White
    } elseif ($line -match "===============") {
        Write-Host $line -ForegroundColor Cyan
        $inResults = $false
    }
}

Write-Host "=== テスト完了 ===" -ForegroundColor Green