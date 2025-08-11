# SentencePiece Implementation Verification Script

param(
    [switch]$SkipPythonCheck
)

$ErrorActionPreference = "Stop"

Write-Host "🔍 SentencePiece Implementation Verification" -ForegroundColor Cyan
Write-Host "=" * 60

# スクリプトの場所を基準にプロジェクトルートを特定
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

# パスを絶対パスに変換
$PythonScriptPath = Join-Path $ScriptDir "verify_sentencepiece_golden_data.py"
$TestDataDir = Join-Path $ProjectRoot "tests\test_data"

Write-Host "📂 Paths:"
Write-Host "  Project Root: $ProjectRoot"
Write-Host "  Python Script: $PythonScriptPath"
Write-Host "  Test Data Directory: $TestDataDir"

# 前提条件チェック
if (-not $SkipPythonCheck) {
    Write-Host "`n🔍 Checking prerequisites..."
    
    # Pythonの確認
    try {
        $pythonVersion = python --version 2>&1
        Write-Host "  ✓ Python: $pythonVersion" -ForegroundColor Green
    } catch {
        Write-Host "  ✗ Python not found in PATH" -ForegroundColor Red
        Write-Host "  Please install Python 3.7+ and ensure it's in PATH"
        exit 1
    }
}

# テストデータディレクトリの作成
if (-not (Test-Path $TestDataDir)) {
    Write-Host "  📁 Creating test data directory: $TestDataDir"
    New-Item -ItemType Directory -Path $TestDataDir -Force | Out-Null
}

# Python検証スクリプトの実行
Write-Host "`n🧪 Running Python normalization verification..."
try {
    Set-Location $ProjectRoot
    $result = python $PythonScriptPath
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Python verification completed successfully!" -ForegroundColor Green
        
        # 生成されたファイルの確認
        $verificationFile = Join-Path $TestDataDir "normalization_verification.json"
        if (Test-Path $verificationFile) {
            $fileSize = (Get-Item $verificationFile).Length
            Write-Host "📊 Generated verification file size: $([math]::Round($fileSize / 1KB, 2)) KB"
            
            # JSONファイルの基本検証
            try {
                $jsonContent = Get-Content $verificationFile -Raw | ConvertFrom-Json
                Write-Host "📈 Verification statistics:"
                Write-Host "  - Total test cases: $($jsonContent.statistics.total_cases)"
                Write-Host "  - Categories: $($jsonContent.statistics.categories -join ', ')"
            } catch {
                Write-Warning "Could not parse generated JSON file for statistics"
            }
        }
        
    } else {
        Write-Error "Python verification script failed with exit code: $LASTEXITCODE"
        exit $LASTEXITCODE
    }
    
} catch {
    Write-Error "Failed to execute Python verification script: $_"
    exit 1
} finally {
    Set-Location $ScriptDir
}

# C#テストの実行
Write-Host "`n🔨 Building C# project..."
try {
    Set-Location $ProjectRoot
    dotnet build Baketa.sln --configuration Debug --verbosity quiet
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Build completed successfully!" -ForegroundColor Green
    } else {
        Write-Warning "Build completed with warnings or errors"
    }
    
} catch {
    Write-Error "Failed to build project: $_"
    exit 1
}

# 正規化テストの実行
Write-Host "`n🧪 Running C# normalization tests..."
try {
    dotnet test tests/Baketa.Infrastructure.Tests/ --filter "TestMethod~Normalize OR ClassName~SentencePieceNormalizerTests" --verbosity normal
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ C# normalization tests passed!" -ForegroundColor Green
    } else {
        Write-Warning "Some normalization tests failed"
    }
    
} catch {
    Write-Error "Failed to run C# tests: $_"
    exit 1
} finally {
    Set-Location $ScriptDir
}

# サマリー
Write-Host "`n📋 Verification Summary" -ForegroundColor Yellow
Write-Host "-" * 40

$verificationFile = Join-Path $TestDataDir "normalization_verification.json"
if (Test-Path $verificationFile) {
    Write-Host "📊 Python verification data generated successfully"
    Write-Host "📂 Location: $verificationFile"
} else {
    Write-Warning "Python verification data not found"
}

Write-Host "`n🎯 Next Steps:"
Write-Host "  1. Review verification results in $TestDataDir"
Write-Host "  2. Compare C# implementation with Python expected results"
Write-Host "  3. Address any discrepancies found"
Write-Host "  4. Run full golden test data generation if needed"

Write-Host "`n✅ SentencePiece verification process completed!" -ForegroundColor Green