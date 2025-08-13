# Baketa プロジェクト簡易コードレビュースクリプト
# ripgrepを使用した基本的な静的解析

param(
    [string]$Path = ".",
    [string]$Target = "all",
    [switch]$Detailed,
    [string]$OutputFormat = "console"
)

# スクリプトの場所を基準にプロジェクトルートを決定
$ScriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptPath

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
        [string]$Description
    )
    
    $script:Issues += [PSCustomObject]@{
        File = $File
        Line = $Line
        Severity = $Severity
        Category = $Category
        Description = $Description
    }
    
    switch ($Severity) {
        "Error" { $script:ErrorCount++ }
        "Warning" { $script:WarningCount++ }
        "Info" { $script:InfoCount++ }
    }
}

function Write-ReviewHeader {
    Write-Host "`n================================================" -ForegroundColor Cyan
    Write-Host "  Baketa プロジェクト 簡易コードレビュー" -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "対象: $Target" -ForegroundColor White
    Write-Host "実行時刻: $(Get-Date)" -ForegroundColor White
    Write-Host ""
}

function Test-BasicPatterns {
    Write-Host "🔍 基本パターンチェック..." -ForegroundColor Yellow
    
    # 旧Interfacesネームスペースの使用チェック
    try {
        $result = rg --type cs "using Baketa\.Core\.Interfaces" $ProjectRoot 2>$null
        if ($result) {
            foreach ($line in $result) {
                $parts = $line -split ":"
                if ($parts.Length -ge 2) {
                    Add-Issue -File $parts[0] -Line $parts[1] -Severity "Warning" -Category "Architecture" `
                        -Description "旧Interfacesネームスペースを使用しています。Abstractionsに移行してください"
                }
            }
        }
    } catch {
        Write-Host "警告: 旧Interfacesネームスペースチェックでエラー: $_" -ForegroundColor Yellow
    }
    
    # ConfigureAwait(false)の不足チェック（簡易版）
    try {
        $result = rg --type cs "await\s+" $ProjectRoot 2>$null
        if ($result) {
            foreach ($line in $result) {
                if ($line -notmatch "ConfigureAwait" -and $line -notmatch "tests") {
                    $parts = $line -split ":"
                    if ($parts.Length -ge 2) {
                        Add-Issue -File $parts[0] -Line $parts[1] -Severity "Info" -Category "Async" `
                            -Description "ConfigureAwait(false)の使用を検討してください"
                    }
                }
            }
        }
    } catch {
        Write-Host "警告: ConfigureAwaitチェックでエラー: $_" -ForegroundColor Yellow
    }
    
    # ファイルスコープ名前空間チェック
    try {
        $result = rg --type cs "^namespace\s+\w+\s*\{" $ProjectRoot 2>$null
        if ($result) {
            foreach ($line in $result) {
                if ($line -notmatch "\.generated\.cs") {
                    $parts = $line -split ":"
                    if ($parts.Length -ge 2) {
                        Add-Issue -File $parts[0] -Line $parts[1] -Severity "Info" -Category "Modernization" `
                            -Description "ファイルスコープ名前空間の使用を検討してください"
                    }
                }
            }
        }
    } catch {
        Write-Host "警告: 名前空間チェックでエラー: $_" -ForegroundColor Yellow
    }
}

function Test-ArchitectureBasic {
    Write-Host "🏗️ 基本アーキテクチャチェック..." -ForegroundColor Yellow
    
    # UI層がInfrastructure層を直接参照
    try {
        $uiPath = Join-Path $ProjectRoot "Baketa.UI"
        if (Test-Path $uiPath) {
            $result = rg --type cs "using Baketa\.Infrastructure" $uiPath 2>$null
            if ($result) {
                foreach ($line in $result) {
                    $parts = $line -split ":"
                    if ($parts.Length -ge 2) {
                        Add-Issue -File $parts[0] -Line $parts[1] -Severity "Error" -Category "Architecture" `
                            -Description "UI層がInfrastructure層を直接参照しています（クリーンアーキテクチャ違反）"
                    }
                }
            }
        }
    } catch {
        Write-Host "警告: UIアーキテクチャチェックでエラー: $_" -ForegroundColor Yellow
    }
    
    # Core層でのプラットフォーム依存
    try {
        $corePath = Join-Path $ProjectRoot "Baketa.Core"
        if (Test-Path $corePath) {
            $result = rg --type cs "DllImport|PInvoke" $corePath 2>$null
            if ($result) {
                foreach ($line in $result) {
                    $parts = $line -split ":"
                    if ($parts.Length -ge 2) {
                        Add-Issue -File $parts[0] -Line $parts[1] -Severity "Error" -Category "Architecture" `
                            -Description "Core層にプラットフォーム依存コードが含まれています"
                    }
                }
            }
        }
    } catch {
        Write-Host "警告: Coreアーキテクチャチェックでエラー: $_" -ForegroundColor Yellow
    }
}

function Test-BaketaSpecific {
    Write-Host "🎮 Baketa固有チェック..." -ForegroundColor Yellow
    
    # ReactiveUI ViewModelBase継承チェック
    try {
        $uiPath = Join-Path $ProjectRoot "Baketa.UI"
        if (Test-Path $uiPath) {
            $result = rg --type cs "class.*ViewModel.*:" $uiPath 2>$null
            if ($result) {
                foreach ($line in $result) {
                    if ($line -notmatch "ViewModelBase") {
                        $parts = $line -split ":"
                        if ($parts.Length -ge 2) {
                            Add-Issue -File $parts[0] -Line $parts[1] -Severity "Warning" -Category "UI Pattern" `
                                -Description "ViewModelはViewModelBaseを継承する必要があります"
                        }
                    }
                }
            }
        }
    } catch {
        Write-Host "警告: ViewModelチェックでエラー: $_" -ForegroundColor Yellow
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
        }
    }
}

# メイン実行
try {
    Write-ReviewHeader
    
    # ripgrepの存在確認
    if (-not (Get-Command rg -ErrorAction SilentlyContinue)) {
        Write-Error "ripgrep (rg) がインストールされていません。winget install BurntSushi.ripgrep.MSVC でインストールしてください。"
        exit 1
    }
    
    if ($Target -eq "all" -or $Target -eq "basic") {
        Test-BasicPatterns
    }
    
    if ($Target -eq "all" -or $Target -eq "architecture") {
        Test-ArchitectureBasic
    }
    
    if ($Target -eq "all" -or $Target -eq "baketa") {
        Test-BaketaSpecific
    }
    
    Write-Summary
    
    if ($Detailed) {
        Write-DetailedResults
    }
    
    # JSON出力
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
    Write-Host "`n使用例:" -ForegroundColor Cyan
    Write-Host "  .\code-review-simple.ps1                    # 全体レビュー" -ForegroundColor White
    Write-Host "  .\code-review-simple.ps1 -Target basic      # 基本チェックのみ" -ForegroundColor White
    Write-Host "  .\code-review-simple.ps1 -Detailed          # 詳細表示" -ForegroundColor White
    Write-Host "  .\code-review-simple.ps1 -OutputFormat json # JSON output" -ForegroundColor White
}