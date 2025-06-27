# Baketa Claude Code 完全セットアップスクリプト

param(
    [switch]$AddToProfile = $false,
    [switch]$SkipValidation = $false
)

Write-Host "=== Baketa Claude Code 完全セットアップ ===" -ForegroundColor Green

# 1. プロジェクトディレクトリ確認
$projectDir = "E:\dev\Baketa"
if (-not (Test-Path $projectDir)) {
    Write-Host "❌ Baketaプロジェクトディレクトリが見つかりません: $projectDir" -ForegroundColor Red
    exit 1
}

Set-Location $projectDir
Write-Host "✅ プロジェクトディレクトリ確認: $projectDir" -ForegroundColor Green

# 2. .claude ディレクトリ確認
$claudeDir = ".claude"
if (Test-Path $claudeDir) {
    Write-Host "✅ Claude設定ディレクトリ存在: $claudeDir" -ForegroundColor Green
} else {
    Write-Host "❌ Claude設定ディレクトリなし: $claudeDir" -ForegroundColor Red
    exit 1
}

# 3. 必須設定ファイル確認
$requiredFiles = @(
    ".claude\project.json",
    ".claude\instructions.md",
    ".claude\context.md",
    "scripts\run_build.ps1",
    "scripts\run_tests.ps1",
    "scripts\run_app.ps1",
    "scripts\baketa_functions.ps1"
)

$missingFiles = @()
foreach ($file in $requiredFiles) {
    if (Test-Path $file) {
        Write-Host "✅ $file" -ForegroundColor Green
    } else {
        Write-Host "❌ $file" -ForegroundColor Red
        $missingFiles += $file
    }
}

if ($missingFiles.Count -gt 0 -and -not $SkipValidation) {
    Write-Host "❌ 必須ファイルが不足しています。セットアップを完了してからもう一度実行してください。" -ForegroundColor Red
    exit 1
}

# 4. dotnet CLI確認
$dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetPath) {
    Write-Host "✅ dotnet CLI確認: $($dotnetPath.Source)" -ForegroundColor Green
} else {
    Write-Host "⚠️ dotnet CLIが見つかりません。フルパス使用に切り替えます。" -ForegroundColor Yellow
    $fullDotnetPath = "C:\Program Files\dotnet\dotnet.exe"
    if (Test-Path $fullDotnetPath) {
        Write-Host "✅ dotnet CLI確認（フルパス）: $fullDotnetPath" -ForegroundColor Green
    } else {
        Write-Host "❌ dotnet CLIが見つかりません: $fullDotnetPath" -ForegroundColor Red
    }
}

# 5. スクリプト実行ポリシー確認
try {
    $executionPolicy = Get-ExecutionPolicy
    if ($executionPolicy -eq "Restricted") {
        Write-Host "⚠️ PowerShell実行ポリシーがRestrictedです。スクリプト実行にはポリシー変更が必要です。" -ForegroundColor Yellow
        Write-Host "実行してください: Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser" -ForegroundColor Cyan
    } else {
        Write-Host "✅ PowerShell実行ポリシー: $executionPolicy" -ForegroundColor Green
    }
}
catch {
    Write-Host "⚠️ PowerShell実行ポリシーの確認に失敗しました" -ForegroundColor Yellow
}

# 6. Git確認
$gitPath = Get-Command git -ErrorAction SilentlyContinue
if ($gitPath) {
    Write-Host "✅ Git確認: $($gitPath.Source)" -ForegroundColor Green
    
    # Git状態確認
    try {
        $gitStatus = git status --porcelain
        if ($gitStatus) {
            Write-Host "ℹ️ 未コミットの変更があります。安全のため定期的にコミットしてください。" -ForegroundColor Cyan
        } else {
            Write-Host "✅ Gitリポジトリはクリーンです" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "⚠️ Gitリポジトリ状態の確認に失敗しました" -ForegroundColor Yellow
    }
} else {
    Write-Host "⚠️ Gitが見つかりません。バージョン管理のためGitの使用を推奨します。" -ForegroundColor Yellow
}

# 7. PowerShellプロファイルへの関数追加
if ($AddToProfile) {
    try {
        $profilePath = $PROFILE
        if (-not (Test-Path $profilePath)) {
            New-Item -ItemType File -Path $profilePath -Force | Out-Null
            Write-Host "✅ PowerShellプロファイルを作成しました: $profilePath" -ForegroundColor Green
        }
        
        $functionLoadLine = ". `"$projectDir\scripts\baketa_functions.ps1`""
        $profileContent = Get-Content $profilePath -ErrorAction SilentlyContinue
        
        if ($profileContent -notcontains $functionLoadLine) {
            Add-Content -Path $profilePath -Value $functionLoadLine
            Write-Host "✅ Baketa便利関数をPowerShellプロファイルに追加しました" -ForegroundColor Green
            Write-Host "新しいPowerShellセッションで cb, ct, cr, ca コマンドが使用可能になります" -ForegroundColor Cyan
        } else {
            Write-Host "ℹ️ Baketa便利関数は既にPowerShellプロファイルに追加済みです" -ForegroundColor Cyan
        }
    }
    catch {
        Write-Host "❌ PowerShellプロファイルへの追加に失敗しました: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# 8. テストビルド実行
Write-Host "`n🔧 テストビルドを実行しています..." -ForegroundColor Yellow
try {
    & ".\scripts\run_build.ps1" -Verbosity minimal
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ テストビルド成功！" -ForegroundColor Green
    } else {
        Write-Host "⚠️ テストビルドで警告またはエラーがありました" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "❌ テストビルドに失敗しました: $($_.Exception.Message)" -ForegroundColor Red
}

# 9. セットアップ完了とガイド表示
Write-Host "`n🎉 Baketa Claude Code セットアップ完了！" -ForegroundColor Green
Write-Host "`n📋 使用方法:" -ForegroundColor Cyan
Write-Host "1. Claude Code基本使用:" -ForegroundColor Yellow
Write-Host "   claude `"【日本語必須・自動承認】PowerShellで以下を実行してください: .\scripts\run_build.ps1`"" -ForegroundColor Gray
Write-Host "`n2. 自動承認設定:" -ForegroundColor Yellow
Write-Host "   Claude Codeの確認ダイアログで Shift + Tab を押下" -ForegroundColor Gray
Write-Host "`n3. 実装完了チェック（必須）:" -ForegroundColor Red
Write-Host "   claude `"【実装完了・エラーチェック必須】PowerShellで以下を実行: .\scripts\check_implementation.ps1`"" -ForegroundColor Gray
Write-Host "`n4. 便利なエイリアス（PowerShellプロファイル追加済みの場合）:" -ForegroundColor Yellow
Write-Host "   cb           # ビルド" -ForegroundColor Gray
Write-Host "   ct           # テスト" -ForegroundColor Gray
Write-Host "   cr           # アプリ実行" -ForegroundColor Gray
Write-Host "   cc           # エラーチェック" -ForegroundColor Red
Write-Host "   ccomplete    # 実装完了チェック" -ForegroundColor Red
Write-Host "   cfix 'タスク'  # 自動修正+チェック" -ForegroundColor Gray
Write-Host "   bhelp        # ヘルプ表示" -ForegroundColor Gray

Write-Host "`n📚 詳細ガイド:" -ForegroundColor Cyan
Write-Host "   docs\claude_code_complete_guide.md - 完全使用ガイド" -ForegroundColor Gray
Write-Host "   docs\claude_code_japanese_setup.md - 日本語設定ガイド" -ForegroundColor Gray
Write-Host "   docs\claude_code_mcp_setup.md - MCP設定ガイド" -ForegroundColor Gray

if (-not $AddToProfile) {
    Write-Host "`n💡 便利な機能を有効にするには:" -ForegroundColor Cyan
    Write-Host "   .\scripts\setup_claude_code.ps1 -AddToProfile" -ForegroundColor Gray
}

Write-Host "`n🚀 Claude Codeでの効率的なBaketa開発をお楽しみください！" -ForegroundColor Magenta