# Baketa プロジェクト静的コードレビュースクリプト
# Gemini API代替として、ripgrepを使用した静的解析によるコードレビューを実行

param(
    [string]$Path = ".",
    [string]$Target = "all",
    [switch]$Detailed,
    [switch]$ArchitectureOnly,
    [switch]$PerformanceOnly,
    [switch]$SecurityOnly,
    [string]$OutputFormat = "console"
)

# スクリプトの場所を基準にプロジェクトルートを決定
$ScriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptPath

# ripgrepの存在確認
if (-not (Get-Command rg -ErrorAction SilentlyContinue)) {
    Write-Error "ripgrep (rg) がインストールされていません。https://github.com/BurntSushi/ripgrep からインストールしてください。"
    exit 1
}

# レビュー結果格納
$Issues = @()
$WarningCount = 0
$ErrorCount = 0
$InfoCount = 0

function Add-Issue {
    param(
        [string]$File,
        [int]$Line = 0,
        [string]$Severity,
        [string]$Category,
        [string]$Description,
        [string]$Code = ""
    )
    
    $script:Issues += [PSCustomObject]@{
        File = $File
        Line = $Line
        Severity = $Severity
        Category = $Category
        Description = $Description
        Code = $Code
    }
    
    switch ($Severity) {
        "Error" { $script:ErrorCount++ }
        "Warning" { $script:WarningCount++ }
        "Info" { $script:InfoCount++ }
    }
}

function Write-ReviewHeader {
    Write-Host "`n================================================" -ForegroundColor Cyan
    Write-Host "  Baketa プロジェクト コードレビュー" -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "対象: $Target" -ForegroundColor White
    Write-Host "パス: $Path" -ForegroundColor White
    Write-Host "実行時刻: $(Get-Date)" -ForegroundColor White
    Write-Host ""
}

function Test-ArchitectureCompliance {
    Write-Host "🏗️  アーキテクチャ準拠性チェック..." -ForegroundColor Yellow
    
    # クリーンアーキテクチャ依存関係チェック
    $UIToInfrastructure = rg --type cs "using Baketa\.Infrastructure" "$ProjectRoot\Baketa.UI" 2>$null
    if ($UIToInfrastructure) {
        $UIToInfrastructure | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Error" -Category "Architecture" `
                -Description "UI層がInfrastructure層を直接参照しています（クリーンアーキテクチャ違反）"
        }
    }
    
    # 旧Interfacesネームスペースの使用チェック
    $OldInterfaces = rg --type cs "using Baketa\.Core\.Interfaces" $ProjectRoot 2>$null
    if ($OldInterfaces) {
        $OldInterfaces | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Warning" -Category "Architecture" `
                -Description "旧Interfacesネームスペースを使用しています。Abstractionsに移行してください"
        }
    }
    
    # Core層でのプラットフォーム依存チェック
    $CorePlatformDeps = rg --type cs "using System\.Windows|using Microsoft\.Win32|PInvoke|DllImport" "$ProjectRoot\Baketa.Core" 2>$null
    if ($CorePlatformDeps) {
        $CorePlatformDeps | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Error" -Category "Architecture" `
                -Description "Core層にプラットフォーム依存コードが含まれています"
        }
    }
    
    # 循環依存チェック（簡易）
    $PotentialCircular = rg --type cs "Baketa\.Application.*using.*Baketa\.UI|Baketa\.Infrastructure.*using.*Baketa\.Application" $ProjectRoot 2>$null
    if ($PotentialCircular) {
        $PotentialCircular | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Error" -Category "Architecture" `
                -Description "潜在的な循環依存が検出されました"
        }
    }
}

function Test-CSharp12Compliance {
    Write-Host "🔧 C# 12 / .NET 8 準拠性チェック..." -ForegroundColor Yellow
    
    # ファイルスコープ名前空間チェック（新規ファイルで旧形式使用）
    $OldNamespaceStyle = rg --type cs "^namespace\s+\w+\s*\{" $ProjectRoot 2>$null | Where-Object { $_ -notmatch "\.generated\.cs" }
    if ($OldNamespaceStyle) {
        $OldNamespaceStyle | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Info" -Category "Modernization" `
                -Description "ファイルスコープ名前空間の使用を検討してください"
        }
    }
    
    # ConfigureAwait(false)の不足チェック
    $MissingConfigureAwait = rg --type cs "\.Wait\(\)|\.Result|await\s+" $ProjectRoot 2>$null | Where-Object { $_ -notmatch "tests" }
    if ($MissingConfigureAwait) {
        $MissingConfigureAwait | ForEach-Object {
            if ($_ -notmatch "ConfigureAwait") {
                $file = ($_ -split ":")[0]
                $line = ($_ -split ":")[1]
                Add-Issue -File $file -Line $line -Severity "Warning" -Category "Async" `
                    -Description "ライブラリコードでConfigureAwait(false)の使用を検討してください"
            }
        }
    }
    
    # 旧コレクション初期化構文チェック
    $OldCollectionSyntax = rg --type cs "new List<.*>\(\)" $ProjectRoot 2>$null
    if ($OldCollectionSyntax) {
        $OldCollectionSyntax | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Info" -Category "Modernization" `
                -Description "C# 12のコレクション式（[]構文）の使用を検討してください"
        }
    }
}

function Test-BaketaSpecificPatterns {
    Write-Host "🎮 Baketa固有パターンチェック..." -ForegroundColor Yellow
    
    # IDisposableパターンの確認
    $DisposableWithoutUsing = rg --type cs "new.*Capture.*\(|new.*Image.*\(|new.*Bitmap.*\(" $ProjectRoot 2>$null | Where-Object { $_ -notmatch "tests" }
    if ($DisposableWithoutUsing) {
        $DisposableWithoutUsing | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            # usingステートメントがあるかチェック
            $contextLines = rg --type cs -A 3 -B 3 $line "$file" 2>$null
            if ($contextLines -notmatch "using\s*\(") {
                Add-Issue -File $file -Line $line -Severity "Warning" -Category "Resource Management" `
                    -Description "IDisposableオブジェクトのusingステートメント使用を検討してください"
            }
        }
    }
    
    # P/Invoke安全性チェック
    $UnsafePInvoke = rg --type cs "\[DllImport.*\]" $ProjectRoot 2>$null | Where-Object { $_ -notmatch "SetLastError" }
    if ($UnsafePInvoke) {
        $UnsafePInvoke | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Info" -Category "Interop" `
                -Description "P/InvokeでSetLastError=trueの使用を検討してください"
        }
    }
    
    # ReactiveUI ViewModelBase継承チェック
    $ViewModelWithoutBase = rg --type cs "class.*ViewModel.*:" "$ProjectRoot\Baketa.UI" 2>$null
    if ($ViewModelWithoutBase) {
        $ViewModelWithoutBase | ForEach-Object {
            if ($_ -notmatch "ViewModelBase") {
                $file = ($_ -split ":")[0]
                $line = ($_ -split ":")[1]
                Add-Issue -File $file -Line $line -Severity "Warning" -Category "UI Pattern" `
                    -Description "ViewModelはViewModelBaseを継承する必要があります"
            }
        }
    }
    
    # EventAggregator直接参照チェック
    $DirectEventUsage = rg --type cs "new.*Event\(|\.Invoke\(.*Event" $ProjectRoot 2>$null | Where-Object { $_ -notmatch "tests" }
    if ($DirectEventUsage) {
        $DirectEventUsage | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Info" -Category "Event System" `
                -Description "EventAggregatorを通じたイベント発行を検討してください"
        }
    }
}

function Test-Performance {
    Write-Host "⚡ パフォーマンスチェック..." -ForegroundColor Yellow
    
    # 文字列連結パフォーマンス
    $StringConcatenation = rg --type cs '\+.*".*".*\+|String\.Concat' "$ProjectRoot" 2>$null
    if ($StringConcatenation) {
        $StringConcatenation | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Info" -Category "Performance" `
                -Description "StringBuilder または文字列補間の使用を検討してください"
        }
    }
    
    # LINQ in loops
    $LinqInLoops = rg --type cs -A 5 -B 2 "for.*\{|foreach.*\{|while.*\{" "$ProjectRoot" | rg "\.Where\(|\.Select\(|\.OrderBy\(" 2>$null
    if ($LinqInLoops) {
        $LinqInLoops | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Warning" -Category "Performance" `
                -Description "ループ内でのLINQ使用はパフォーマンスに影響する可能性があります"
        }
    }
    
    # 同期ブロッキング呼び出し
    $SyncBlocking = rg --type cs "\.Wait\(\)|\.Result" "$ProjectRoot" --exclude-dir tests 2>$null
    if ($SyncBlocking) {
        $SyncBlocking | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Warning" -Category "Performance" `
                -Description "同期ブロッキング呼び出しはデッドロックの原因になる可能性があります"
        }
    }
}

function Test-Security {
    Write-Host "🔒 セキュリティチェック..." -ForegroundColor Yellow
    
    # SQLインジェクション可能性
    $SqlInjection = rg --type cs '".*SELECT.*\+|".*INSERT.*\+|".*UPDATE.*\+|".*DELETE.*\+' "$ProjectRoot" 2>$null
    if ($SqlInjection) {
        $SqlInjection | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Error" -Category "Security" `
                -Description "SQLインジェクション脆弱性の可能性があります。パラメータ化クエリを使用してください"
        }
    }
    
    # ハードコードされた機密情報
    $HardcodedSecrets = rg --type cs 'password.*=.*".*"|apikey.*=.*".*"|secret.*=.*".*"' -i "$ProjectRoot" 2>$null
    if ($HardcodedSecrets) {
        $HardcodedSecrets | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Error" -Category "Security" `
                -Description "機密情報がハードコードされている可能性があります"
        }
    }
    
    # パスインジェクション
    $PathInjection = rg --type cs 'Path\.Combine.*\+|\.\./' "$ProjectRoot" 2>$null
    if ($PathInjection) {
        $PathInjection | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Warning" -Category "Security" `
                -Description "パスインジェクション脆弱性の可能性があります。入力検証を行ってください"
        }
    }
    
    # unsafe codeブロック
    $UnsafeCode = rg --type cs 'unsafe\s*\{' "$ProjectRoot" 2>$null
    if ($UnsafeCode) {
        $UnsafeCode | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Info" -Category "Security" `
                -Description "unsafeコードが使用されています。必要性を再確認してください"
        }
    }
}

function Test-TestCompliance {
    Write-Host "🧪 テスト品質チェック..." -ForegroundColor Yellow
    
    # テストメソッド命名
    $BadTestNames = rg --type cs '\[Test\]|\[Fact\]' -A 1 "$ProjectRoot/tests" | rg "void Test\d+\(" 2>$null
    if ($BadTestNames) {
        $BadTestNames | ForEach-Object {
            $file = ($_ -split ":")[0]
            $line = ($_ -split ":")[1]
            Add-Issue -File $file -Line $line -Severity "Warning" -Category "Test Quality" `
                -Description "テストメソッド名は動作を説明する名前にしてください"
        }
    }
    
    # AssertなしのTestメソッド
    $NoAssert = rg --type cs -A 10 '\[Test\]|\[Fact\]' "$ProjectRoot/tests" | rg -v "Assert\.|Should\." 2>$null
    if ($NoAssert) {
        $lines = $NoAssert -split "`n"
        foreach ($line in $lines) {
            if ($line -match '\[Test\]|\[Fact\]') {
                $file = ($line -split ":")[0]
                $lineNum = ($line -split ":")[1]
                Add-Issue -File $file -Line $lineNum -Severity "Warning" -Category "Test Quality" `
                    -Description "テストメソッドにアサーションが含まれていない可能性があります"
            }
        }
    }
}

function Write-Summary {
    Write-Host "`n================================================" -ForegroundColor Cyan
    Write-Host "  レビュー結果サマリー" -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "🔴 エラー: $ErrorCount" -ForegroundColor Red
    Write-Host "🟡 警告: $WarningCount" -ForegroundColor Yellow
    Write-Host "🔵 情報: $InfoCount" -ForegroundColor Blue
    Write-Host "📊 総問題数: $($Issues.Count)" -ForegroundColor White
    Write-Host ""
}

function Write-DetailedResults {
    if ($Issues.Count -eq 0) {
        Write-Host "✅ 問題は検出されませんでした！" -ForegroundColor Green
        return
    }
    
    # 重要度別にグループ化
    $groupedIssues = $Issues | Group-Object Severity
    
    foreach ($group in $groupedIssues) {
        $color = switch ($group.Name) {
            "Error" { "Red" }
            "Warning" { "Yellow" }
            "Info" { "Cyan" }
        }
        
        Write-Host "`n[$($group.Name)] $($group.Count) 件の問題" -ForegroundColor $color
        Write-Host ("=" * 50) -ForegroundColor $color
        
        foreach ($issue in $group.Group) {
            $relativePath = $issue.File -replace [regex]::Escape($ProjectRoot), ""
            $relativePath = $relativePath.TrimStart("\")
            
            Write-Host "`n📄 $relativePath" -ForegroundColor White
            if ($issue.Line -gt 0) {
                Write-Host "   行 $($issue.Line)" -ForegroundColor Gray
            }
            Write-Host "   🏷️  $($issue.Category)" -ForegroundColor Gray
            Write-Host "   📝 $($issue.Description)" -ForegroundColor $color
            if ($issue.Code) {
                Write-Host "   💻 $($issue.Code)" -ForegroundColor Gray
            }
        }
    }
}

function Export-Results {
    if ($OutputFormat -eq "json") {
        $output = @{
            Timestamp = Get-Date
            Summary = @{
                Errors = $ErrorCount
                Warnings = $WarningCount
                Info = $InfoCount
                Total = $Issues.Count
            }
            Issues = $Issues
        }
        
        $outputPath = Join-Path $ProjectRoot "code-review-results.json"
        $output | ConvertTo-Json -Depth 3 | Out-File $outputPath -Encoding UTF8
        Write-Host "`n📄 結果をJSONで出力しました: $outputPath" -ForegroundColor Green
    }
}

# メイン実行
try {
    Write-ReviewHeader
    
    # チェック対象の決定
    if ($Target -eq "all" -or $ArchitectureOnly) {
        Test-ArchitectureCompliance
    }
    
    if ($Target -eq "all" -or $Target -eq "csharp") {
        Test-CSharp12Compliance
    }
    
    if ($Target -eq "all" -or $Target -eq "baketa") {
        Test-BaketaSpecificPatterns
    }
    
    if ($Target -eq "all" -or $PerformanceOnly) {
        Test-Performance
    }
    
    if ($Target -eq "all" -or $SecurityOnly) {
        Test-Security
    }
    
    if ($Target -eq "all" -or $Target -eq "test") {
        Test-TestCompliance
    }
    
    Write-Summary
    
    if ($Detailed) {
        Write-DetailedResults
    }
    
    Export-Results
    
    # 終了コード設定
    if ($ErrorCount -gt 0) {
        exit 1
    } elseif ($WarningCount -gt 0) {
        exit 2
    } else {
        exit 0
    }
    
} catch {
    Write-Error "レビューの実行中にエラーが発生しました: $_"
    exit 1
}

# 使用例の表示
if ($Issues.Count -eq 0) {
    Write-Host "`n📋 使用例:" -ForegroundColor Cyan
    Write-Host "  .\code-review.ps1                    # 全体レビュー"
    Write-Host "  .\code-review.ps1 -ArchitectureOnly  # アーキテクチャのみ"
    Write-Host "  .\code-review.ps1 -Detailed          # 詳細表示"
    Write-Host "  .\code-review.ps1 -OutputFormat json # JSON出力"
}