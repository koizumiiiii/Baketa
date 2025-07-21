# Baketa Complete Build Script
# Windows Graphics Capture API ネイティブDLL + .NET ソリューション
# 実行順序を自動化し、エラーハンドリングを追加

param(
    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [Parameter()]
    [switch]$SkipNative,
    
    [Parameter()]
    [switch]$SkipDotNet,
    
    [Parameter()]
    [switch]$Verbose
)

# スクリプト設定
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# パス設定
$RootPath = Split-Path $PSScriptRoot -Parent
$NativeSolutionPath = Join-Path $RootPath "BaketaCaptureNative\BaketaCaptureNative.sln"
$DotNetSolutionPath = Join-Path $RootPath "Baketa.sln"
$NativeDllPath = Join-Path $RootPath "BaketaCaptureNative\bin\$Configuration\BaketaCaptureNative.dll"
$DotNetOutputPath = Join-Path $RootPath "Baketa.UI\bin\x64\$Configuration\net8.0-windows10.0.19041.0"

# ログ関数
function Write-BuildLog {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "ERROR" { "Red" }
        "WARN"  { "Yellow" }
        "SUCCESS" { "Green" }
        default { "White" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

# Visual Studio 2022環境確認
function Test-VisualStudio2022 {
    $vsPath = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"
    if (-not (Test-Path $vsPath)) {
        $vsPath = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\Tools\VsDevCmd.bat"
    }
    if (-not (Test-Path $vsPath)) {
        $vsPath = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\Common7\Tools\VsDevCmd.bat"
    }
    
    if (-not (Test-Path $vsPath)) {
        throw "Visual Studio 2022 が見つかりません。C++/WinRT開発環境が必要です。"
    }
    
    return $vsPath
}

# メイン処理開始
Write-BuildLog "[START] Baketa Complete Build Script Starting..." "SUCCESS"
Write-BuildLog "Configuration: $Configuration"
Write-BuildLog "Root Path: $RootPath"

try {
    # 1. ネイティブDLLビルド
    if (-not $SkipNative) {
        Write-BuildLog "📦 Step 1: Building Native DLL (BaketaCaptureNative)" "SUCCESS"
        
        # Visual Studio 2022確認
        $vsDevCmd = Test-VisualStudio2022
        Write-BuildLog "Visual Studio Found: $vsDevCmd"
        
        # ネイティブソリューション確認
        if (-not (Test-Path $NativeSolutionPath)) {
            throw "ネイティブソリューションが見つかりません: $NativeSolutionPath"
        }
        
        # MSBuildでネイティブDLLビルド
        Write-BuildLog "Building C++/WinRT project..."
        $buildCommand = "cmd /c `"call `"$vsDevCmd`" ; msbuild `"$NativeSolutionPath`" /p:Configuration=$Configuration /p:Platform=x64 /v:minimal`""
        
        if ($Verbose) {
            Write-BuildLog "Execute: $buildCommand"
        }
        
        $buildResult = Invoke-Expression $buildCommand
        
        if ($LASTEXITCODE -ne 0) {
            throw "ネイティブDLLビルドに失敗しました。終了コード: $LASTEXITCODE"
        }
        
        # DLL生成確認
        if (-not (Test-Path $NativeDllPath)) {
            throw "ネイティブDLLが生成されませんでした: $NativeDllPath"
        }
        
        $dllInfo = Get-Item $NativeDllPath
        Write-BuildLog "✅ Native DLL Built: $($dllInfo.Name) ($($dllInfo.Length) bytes)" "SUCCESS"
        
        # 2. DLL自動コピー
        Write-BuildLog "📋 Step 2: Copying Native DLL to Output Directory" "SUCCESS"
        
        # 出力ディレクトリ確認・作成
        if (-not (Test-Path $DotNetOutputPath)) {
            Write-BuildLog "Creating output directory: $DotNetOutputPath"
            New-Item -ItemType Directory -Path $DotNetOutputPath -Force | Out-Null
        }
        
        $targetDllPath = Join-Path $DotNetOutputPath "BaketaCaptureNative.dll"
        Copy-Item $NativeDllPath $targetDllPath -Force
        
        Write-BuildLog "✅ DLL Copied: $targetDllPath" "SUCCESS"
        
        # PDBファイルもコピー（デバッグ用）
        $nativePdbPath = Join-Path $RootPath "BaketaCaptureNative\bin\$Configuration\BaketaCaptureNative.pdb"
        if (Test-Path $nativePdbPath) {
            $targetPdbPath = Join-Path $DotNetOutputPath "BaketaCaptureNative.pdb"
            Copy-Item $nativePdbPath $targetPdbPath -Force
            Write-BuildLog "✅ PDB Copied: $targetPdbPath" "SUCCESS"
        }
    } else {
        Write-BuildLog "⏭️ Skipping Native DLL Build" "WARN"
    }
    
    # 3. .NET ソリューションビルド
    if (-not $SkipDotNet) {
        Write-BuildLog "🔷 Step 3: Building .NET Solution" "SUCCESS"
        
        # .NETソリューション確認
        if (-not (Test-Path $DotNetSolutionPath)) {
            throw ".NETソリューションが見つかりません: $DotNetSolutionPath"
        }
        
        # .NET SDKバージョン確認
        $dotnetVersion = dotnet --version
        Write-BuildLog ".NET SDK Version: $dotnetVersion"
        
        # .NETプロジェクトビルド
        Push-Location $RootPath
        try {
            if ($Verbose) {
                dotnet build $DotNetSolutionPath --configuration $Configuration --verbosity normal
            } else {
                dotnet build $DotNetSolutionPath --configuration $Configuration --verbosity minimal
            }
            
            if ($LASTEXITCODE -ne 0) {
                throw ".NETソリューションビルドに失敗しました。終了コード: $LASTEXITCODE"
            }
            
            Write-BuildLog "✅ .NET Solution Built Successfully" "SUCCESS"
        }
        finally {
            Pop-Location
        }
    } else {
        Write-BuildLog "⏭️ Skipping .NET Solution Build" "WARN"
    }
    
    # 4. ビルド結果確認
    Write-BuildLog "🔍 Step 4: Verifying Build Results" "SUCCESS"
    
    # 主要ファイル確認
    $mainExePath = Join-Path $DotNetOutputPath "Baketa.UI.exe"
    $nativeDllInOutput = Join-Path $DotNetOutputPath "BaketaCaptureNative.dll"
    
    $verificationResults = @()
    
    if (Test-Path $mainExePath) {
        $exeInfo = Get-Item $mainExePath
        $verificationResults += "✅ Baketa.UI.exe: $($exeInfo.Length) bytes, Modified: $($exeInfo.LastWriteTime)"
    } else {
        $verificationResults += "❌ Baketa.UI.exe: NOT FOUND"
    }
    
    if (Test-Path $nativeDllInOutput) {
        $dllInfo = Get-Item $nativeDllInOutput
        $verificationResults += "✅ BaketaCaptureNative.dll: $($dllInfo.Length) bytes, Modified: $($dllInfo.LastWriteTime)"
    } else {
        $verificationResults += "❌ BaketaCaptureNative.dll: NOT FOUND"
    }
    
    foreach ($result in $verificationResults) {
        Write-BuildLog $result
    }
    
    # 5. 実行準備完了
    Write-BuildLog "🎯 Build Complete! Ready to Run" "SUCCESS"
    Write-BuildLog "Execute: dotnet run --project Baketa.UI --configuration $Configuration"
    Write-BuildLog "Or run directly: $mainExePath"
    
    # 実行可能性チェック
    if ((Test-Path $mainExePath) -and (Test-Path $nativeDllInOutput)) {
        Write-BuildLog "🚀 All components ready for execution!" "SUCCESS"
        return 0
    } else {
        Write-BuildLog "⚠️ Some components missing. Check build logs." "WARN"
        return 1
    }
    
} catch {
    Write-BuildLog "💥 Build Failed: $($_.Exception.Message)" "ERROR"
    Write-BuildLog "Stack Trace: $($_.ScriptStackTrace)" "ERROR"
    return 1
}