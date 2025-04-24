# Baketa プロジェクト開発環境チェックスクリプト
# 
# 使用方法: このスクリプトを実行して開発環境がBaketaプロジェクトの要件を満たしているか確認します。
# PowerShell 7以降での実行を推奨しますが、Windows PowerShell 5.1でも動作します。

# 結果表示用の色定義
$Colors = @{
    Success = [ConsoleColor]::Green
    Warning = [ConsoleColor]::Yellow
    Error = [ConsoleColor]::Red
    Info = [ConsoleColor]::Cyan
}

# タイトル表示
function Show-Title {
    Write-Host "`n----------------------------------------" -ForegroundColor $Colors.Info
    Write-Host "    Baketa プロジェクト開発環境チェック    " -ForegroundColor $Colors.Info
    Write-Host "----------------------------------------`n" -ForegroundColor $Colors.Info
}

# 結果表示用関数
function Show-Result {
    param (
        [string]$Category,
        [string]$Item,
        [string]$Current,
        [string]$Required,
        [string]$Status
    )
    
    $StatusColor = switch ($Status) {
        "OK" { $Colors.Success }
        "警告" { $Colors.Warning }
        "エラー" { $Colors.Error }
        default { $Colors.Info }
    }
    
    Write-Host "[$Category] " -ForegroundColor $Colors.Info -NoNewline
    Write-Host "$Item: " -NoNewline
    Write-Host "$Current " -NoNewline
    
    if ($Required) {
        Write-Host "(要: $Required) " -NoNewline
    }
    
    Write-Host "[$Status]" -ForegroundColor $StatusColor
}

# .NET SDKチェック
function Check-DotNetSDK {
    Write-Host "◆ .NET SDK バージョン確認中..." -ForegroundColor $Colors.Info
    
    try {
        $sdkInfo = dotnet --list-sdks
        $net8Sdk = $sdkInfo | Where-Object { $_ -like "8.0*" }
        
        if ($net8Sdk) {
            $sdkVersion = ($net8Sdk -split " ")[0]
            $minVersion = [Version]"8.0.100"
            $recommendedVersion = [Version]"8.0.200"
            $currentVersion = [Version]$sdkVersion
            
            if ($currentVersion -ge $recommendedVersion) {
                Show-Result ".NET SDK" "バージョン" $sdkVersion "8.0.200+" "OK"
            } 
            elseif ($currentVersion -ge $minVersion) {
                Show-Result ".NET SDK" "バージョン" $sdkVersion "8.0.200+" "警告"
                Write-Host "  → 推奨バージョン (8.0.200+) へのアップデートを検討してください" -ForegroundColor $Colors.Warning
            }
            else {
                Show-Result ".NET SDK" "バージョン" $sdkVersion "8.0.100+" "エラー"
                Write-Host "  → .NET 8 SDK (8.0.100+) のインストールが必要です" -ForegroundColor $Colors.Error
            }
        }
        else {
            Show-Result ".NET SDK" "バージョン" "未検出" "8.0.100+" "エラー"
            Write-Host "  → .NET 8 SDK (8.0.100+) のインストールが必要です" -ForegroundColor $Colors.Error
        }
    }
    catch {
        Show-Result ".NET SDK" "状態" "確認失敗" "" "エラー"
        Write-Host "  → dotnet コマンドが見つかりません。.NET SDKをインストールしてください" -ForegroundColor $Colors.Error
    }
}

# Visual Studioチェック
function Check-VisualStudio {
    Write-Host "`n◆ Visual Studio 確認中..." -ForegroundColor $Colors.Info
    
    # Visual Studioの検出を試みる
    $vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    
    if (Test-Path $vsWhere) {
        $vsInfo = & $vsWhere -latest -format json | ConvertFrom-Json
        
        if ($vsInfo) {
            $version = $vsInfo.installationVersion
            $displayName = $vsInfo.displayName
            
            # バージョン解析
            $versionObj = [Version]$version
            $majorVersion = $versionObj.Major
            $minorVersion = $versionObj.Minor
            
            if ($majorVersion -ge 17 -and $minorVersion -ge 8) {
                if ($majorVersion -ge 17 -and $minorVersion -ge 9) {
                    Show-Result "Visual Studio" "バージョン" "$displayName ($version)" "17.9+" "OK"
                } 
                else {
                    Show-Result "Visual Studio" "バージョン" "$displayName ($version)" "17.9+" "警告"
                    Write-Host "  → 推奨バージョン (17.9+) へのアップデートを検討してください" -ForegroundColor $Colors.Warning
                }
            } 
            else {
                Show-Result "Visual Studio" "バージョン" "$displayName ($version)" "17.8+" "エラー"
                Write-Host "  → Visual Studio 17.8以上へのアップデートが必要です" -ForegroundColor $Colors.Error
            }
            
            # インストール済みのワークロードを確認
            $workloads = & $vsWhere -latest -property workloads
            $hasNetDesktop = $workloads -match "Microsoft.VisualStudio.Workload.NetDesktop"
            
            if ($hasNetDesktop) {
                Show-Result "Visual Studio" ".NET デスクトップ開発" "インストール済み" "必須" "OK"
            } 
            else {
                Show-Result "Visual Studio" ".NET デスクトップ開発" "未インストール" "必須" "エラー"
                Write-Host "  → [ワークロードの変更] から .NET デスクトップ開発をインストールしてください" -ForegroundColor $Colors.Error
            }
        }
        else {
            Show-Result "Visual Studio" "状態" "未検出" "17.8+" "エラー"
            Write-Host "  → Visual Studio 17.8以上のインストールが必要です" -ForegroundColor $Colors.Error
        }
    }
    else {
        Show-Result "Visual Studio" "状態" "未検出" "17.8+" "エラー"
        Write-Host "  → Visual Studio 17.8以上のインストールが必要です" -ForegroundColor $Colors.Error
    }
}

# PowerShellバージョンチェック
function Check-PowerShell {
    Write-Host "`n◆ PowerShell バージョン確認中..." -ForegroundColor $Colors.Info
    
    $psVersion = $PSVersionTable.PSVersion
    $minVersion = [Version]"5.1"
    $recommendedVersion = [Version]"7.2"
    
    if ($psVersion -ge $recommendedVersion) {
        Show-Result "PowerShell" "バージョン" "$psVersion" "7.2+" "OK"
    } 
    elseif ($psVersion -ge $minVersion) {
        Show-Result "PowerShell" "バージョン" "$psVersion" "7.2+" "警告"
        Write-Host "  → PowerShell 7.2+へのアップデートを推奨します" -ForegroundColor $Colors.Warning
    } 
    else {
        Show-Result "PowerShell" "バージョン" "$psVersion" "5.1+" "エラー"
        Write-Host "  → PowerShell 5.1以上が必要です" -ForegroundColor $Colors.Error
    }
}

# Gitバージョンチェック
function Check-Git {
    Write-Host "`n◆ Git バージョン確認中..." -ForegroundColor $Colors.Info
    
    try {
        $gitVersion = (git --version) -replace "git version ", ""
        $minVersion = [Version]::Parse(($gitVersion -split "-")[0])
        $requiredMinVersion = [Version]"2.30.0"
        $recommendedVersion = [Version]"2.40.0"
        
        if ($minVersion -ge $recommendedVersion) {
            Show-Result "Git" "バージョン" "$gitVersion" "2.40.0+" "OK"
        } 
        elseif ($minVersion -ge $requiredMinVersion) {
            Show-Result "Git" "バージョン" "$gitVersion" "2.40.0+" "警告"
            Write-Host "  → Git 2.40.0+へのアップデートを推奨します" -ForegroundColor $Colors.Warning
        } 
        else {
            Show-Result "Git" "バージョン" "$gitVersion" "2.30.0+" "エラー"
            Write-Host "  → Git 2.30.0以上へのアップデートが必要です" -ForegroundColor $Colors.Error
        }
        
        # Gitの設定チェック
        $autocrlf = git config --get core.autocrlf
        
        if ($autocrlf -eq "true") {
            Show-Result "Git" "core.autocrlf" "$autocrlf" "true" "OK"
        } 
        else {
            Show-Result "Git" "core.autocrlf" "$autocrlf" "true" "警告"
            Write-Host "  → 'git config --global core.autocrlf true' の実行を推奨します" -ForegroundColor $Colors.Warning
        }
    }
    catch {
        Show-Result "Git" "状態" "未検出" "2.30.0+" "エラー"
        Write-Host "  → Git 2.30.0以上のインストールが必要です" -ForegroundColor $Colors.Error
    }
}

# プロジェクト設定チェック
function Check-ProjectSettings {
    Write-Host "`n◆ プロジェクト設定確認中..." -ForegroundColor $Colors.Info
    
    $buildProps = "E:\dev\Baketa\Directory.Build.props"
    
    if (Test-Path $buildProps) {
        $content = Get-Content $buildProps -Raw
        
        # LangVersion設定の確認
        if ($content -match "<LangVersion>(\d+\.\d+)</LangVersion>") {
            $langVersion = $matches[1]
            
            if ($langVersion -eq "12.0") {
                Show-Result "プロジェクト" "LangVersion" "$langVersion" "12.0" "OK"
            } 
            else {
                Show-Result "プロジェクト" "LangVersion" "$langVersion" "12.0" "警告"
                Write-Host "  → Directory.Build.propsファイルでLangVersionを12.0に設定してください" -ForegroundColor $Colors.Warning
            }
        } 
        else {
            Show-Result "プロジェクト" "LangVersion" "未設定" "12.0" "エラー"
            Write-Host "  → Directory.Build.propsファイルにLangVersion設定を追加してください" -ForegroundColor $Colors.Error
        }
        
        # その他の設定を確認
        $hasAnalysisLevel = $content -match "<AnalysisLevel>"
        $hasEnforceCodeStyle = $content -match "<EnforceCodeStyleInBuild>"
        $hasEnableAnalyzers = $content -match "<EnableNETAnalyzers>"
        
        if ($hasAnalysisLevel -and $hasEnforceCodeStyle -and $hasEnableAnalyzers) {
            Show-Result "プロジェクト" "分析設定" "設定済み" "必須" "OK"
        } 
        else {
            Show-Result "プロジェクト" "分析設定" "一部未設定" "必須" "警告"
            Write-Host "  → 分析設定を完全に構成することを推奨します" -ForegroundColor $Colors.Warning
        }

        # MSBuildの構造チェック
        $propertyGroupWithItemGroup = $content -match "<PropertyGroup>[\s\S]*?<ItemGroup>"
        
        if ($propertyGroupWithItemGroup) {
            Show-Result "プロジェクト" "MSBuild構造" "不正な構造" "適切な構造" "エラー"
            Write-Host "  → <ItemGroup>を<PropertyGroup>の外に移動してください" -ForegroundColor $Colors.Error
        } 
        else {
            Show-Result "プロジェクト" "MSBuild構造" "適切な構造" "適切な構造" "OK"
        }
    } 
    else {
        Show-Result "プロジェクト" "Directory.Build.props" "未検出" "必須" "エラー"
        Write-Host "  → Directory.Build.propsファイルが見つかりません" -ForegroundColor $Colors.Error
    }
}

# コア.NETライブラリチェック
function Check-CoreLibraries {
    Write-Host "`n◆ コアライブラリ確認中..." -ForegroundColor $Colors.Info
    
    try {
        # プロジェクトのビルドを試行
        $buildOutput = dotnet build "E:\dev\Baketa\Baketa.Core\Baketa.Core.csproj" -nologo 2>&1
        $hasErrors = $buildOutput -match "error CS"
        
        if (-not $hasErrors) {
            Show-Result "ビルド" "Baketa.Core" "成功" "" "OK"
        } 
        else {
            $errorCount = ($buildOutput | Where-Object { $_ -match "error CS" }).Count
            Show-Result "ビルド" "Baketa.Core" "失敗 ($errorCount エラー)" "" "エラー"
            Write-Host "  → コンパイルエラーを修正してください" -ForegroundColor $Colors.Error
        }
        
        # C# 12固有の警告を確認
        $hasIDE0300 = $buildOutput -match "IDE0300"
        $hasCA1825 = $buildOutput -match "CA1825"
        
        if ($hasIDE0300 -or $hasCA1825) {
            Show-Result "静的解析" "C# 12関連の警告" "検出" "修正推奨" "警告"
            Write-Host "  → IDE0300/CA1825の警告はC# 12のコレクション式を使用して解決できます" -ForegroundColor $Colors.Warning
        } 
        else {
            Show-Result "静的解析" "C# 12関連の警告" "なし" "" "OK"
        }
    }
    catch {
        Show-Result "ビルド" "Baketa.Core" "失敗" "" "エラー"
        Write-Host "  → ビルドプロセスでエラーが発生しました: $_" -ForegroundColor $Colors.Error
    }
}

# 結果サマリー
function Show-Summary {
    Write-Host "`n◆ チェック結果サマリー" -ForegroundColor $Colors.Info
    Write-Host "----------------------------------------" -ForegroundColor $Colors.Info
    Write-Host "環境チェックが完了しました。結果に基づいてC# 12を活用できる準備を整えてください。"
    Write-Host "問題がある場合は、以下のリソースを参照してください："
    Write-Host "・ドキュメント: E:\dev\Baketa\docs\2-development\language-features\csharp-12-support.md"
    Write-Host "・ガイドライン: E:\dev\Baketa\docs\2-development\team-guidelines\development-environment.md"
    Write-Host "----------------------------------------`n" -ForegroundColor $Colors.Info
}

# メイン実行フロー
function Main {
    Show-Title
    Check-DotNetSDK
    Check-VisualStudio
    Check-PowerShell
    Check-Git
    Check-ProjectSettings
    Check-CoreLibraries
    Show-Summary
}

# スクリプト実行
Main