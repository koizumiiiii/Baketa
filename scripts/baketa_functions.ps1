# Baketa 開発用便利関数 - PowerShell プロファイル追加用

# Claude Code 便利関数
function Claude-Build {
    param([string]$Project = "", [switch]$Clean, [string]$Config = "Debug")
    
    $scriptPath = "E:\dev\Baketa\scripts\run_build.ps1"
    $params = @()
    
    if ($Project) { $params += "-Project", $Project }
    if ($Clean) { $params += "-Clean" }
    if ($Config -ne "Debug") { $params += "-Configuration", $Config }
    
    & $scriptPath @params
}

function Claude-Test {
    param([string]$Project = "", [string]$Filter = "", [string]$Verbosity = "minimal")
    
    $scriptPath = "E:\dev\Baketa\scripts\run_tests.ps1"
    $params = @()
    
    if ($Project) { $params += "-Project", $Project }
    if ($Filter) { $params += "-Filter", $Filter }
    if ($Verbosity -ne "minimal") { $params += "-Verbosity", $Verbosity }
    
    & $scriptPath @params
}

function Claude-Run {
    param([string]$Project = "Baketa.UI", [switch]$Watch, [string]$Config = "Debug")
    
    $scriptPath = "E:\dev\Baketa\scripts\run_app.ps1"
    $params = @("-Project", $Project)
    
    if ($Watch) { $params += "-Watch" }
    if ($Config -ne "Debug") { $params += "-Configuration", $Config }
    
    & $scriptPath @params
}

function Claude-Check {
    param([switch]$Detailed, [switch]$SkipTests, [string]$TestFilter = "")
    
    $scriptPath = "E:\dev\Baketa\scripts\check_implementation.ps1"
    $params = @()
    
    if ($Detailed) { $params += "-Detailed" }
    if ($SkipTests) { $params += "-SkipTests" }
    if ($TestFilter) { $params += "-TestFilter", $TestFilter }
    
    & $scriptPath @params
}

function Claude-Complete {
    param([string]$Description = "")
    
    Write-Host "=== 実装完了チェック開始 ===" -ForegroundColor Green
    if ($Description) {
        Write-Host "実装内容: $Description" -ForegroundColor Cyan
    }
    
    # 自動チェック実行
    $result = Claude-Check
    
    Write-Host "`n📋 実装完了レポート" -ForegroundColor Magenta
    Write-Host "==============================" -ForegroundColor Magenta
    
    if ($result -eq 0) {
        Write-Host "✅ 実装完了: すべてのチェックに合格" -ForegroundColor Green
    } elseif ($result -eq 2) {
        Write-Host "⚠️ 実装完了: 警告あり（対応推奨）" -ForegroundColor Yellow
    } else {
        Write-Host "❌ 実装未完了: エラーあり（修正必要）" -ForegroundColor Red
    }
    
    if ($Description) {
        Write-Host "実装内容: $Description" -ForegroundColor Cyan
    }
    
    return $result
}

function Claude-Fix {
    param([string]$Task)
    
    Write-Host "=== エラー修正タスク ===" -ForegroundColor Yellow
    Write-Host "タスク: $Task" -ForegroundColor Cyan
    
    # Claude Code実行
    $command = "claude `"【自動承認・日本語回答・エラーチェック必須】PowerShellで以下を実行してください: $Task`""
    Write-Host "実行コマンド: $command" -ForegroundColor Gray
    Invoke-Expression $command
    
    # 修正後のチェック
    Write-Host "`n修正後のチェックを実行します..." -ForegroundColor Yellow
    Claude-Check
}

# Baketa 専用エイリアス
Set-Alias -Name cb -Value Claude-Build
Set-Alias -Name ct -Value Claude-Test  
Set-Alias -Name cr -Value Claude-Run
Set-Alias -Name cc -Value Claude-Check
Set-Alias -Name ccomplete -Value Claude-Complete
Set-Alias -Name cfix -Value Claude-Fix

# 旧エイリアスの保持（互換性）
Set-Alias -Name ca -Value Claude-Fix

# 使用例表示
function Show-BaketaHelp {
    Write-Host "=== Baketa 開発用便利関数 ===" -ForegroundColor Green
    Write-Host ""
    Write-Host "Claude-Build (cb):" -ForegroundColor Cyan
    Write-Host "  cb                    # 通常ビルド" -ForegroundColor Gray
    Write-Host "  cb -Clean             # クリーンビルド" -ForegroundColor Gray
    Write-Host "  cb -Project Baketa.UI # UIプロジェクトビルド" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Claude-Test (ct):" -ForegroundColor Cyan
    Write-Host "  ct                               # 全テスト" -ForegroundColor Gray
    Write-Host "  ct -Project tests/Baketa.UI.Tests # UIテスト" -ForegroundColor Gray
    Write-Host "  ct -Filter 'TestMethodName'      # 特定テスト" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Claude-Run (cr):" -ForegroundColor Cyan
    Write-Host "  cr           # UI実行" -ForegroundColor Gray
    Write-Host "  cr -Watch    # ファイル監視モード" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Claude-Check (cc):" -ForegroundColor Cyan
    Write-Host "  cc           # 標準エラーチェック" -ForegroundColor Gray
    Write-Host "  cc -Detailed # 詳細エラーチェック" -ForegroundColor Gray
    Write-Host "  cc -SkipTests # テストスキップ" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Claude-Complete (ccomplete):" -ForegroundColor Cyan
    Write-Host "  ccomplete '実装内容'  # 実装完了チェック" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Claude-Fix (cfix/ca):" -ForegroundColor Cyan
    Write-Host "  cfix 'エラーを修正して'     # 自動修正+チェック" -ForegroundColor Gray
    Write-Host "  ca '新機能を実装して'     # 旧エイリアス（互換性）" -ForegroundColor Gray
}
}

# ヘルプエイリアス
Set-Alias -Name bhelp -Value Show-BaketaHelp

Write-Host "Baketa 開発用便利関数が読み込まれました！" -ForegroundColor Green
Write-Host "使用方法: bhelp で詳細表示" -ForegroundColor Yellow