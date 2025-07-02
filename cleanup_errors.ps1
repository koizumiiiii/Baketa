# Baketaプロジェクトのビルド環境クリーンアップスクリプト

Write-Host "=== Baketa プロジェクト エラー修正スクリプト ===" -ForegroundColor Yellow

# 1. バックアップファイルの完全削除
Write-Host "1. バックアップファイルを削除中..." -ForegroundColor Cyan
$projectRoot = "E:\dev\Baketa"
Get-ChildItem -Path $projectRoot -Recurse -Include "*.backup*", "*.old*", "*removed*", "*.deleted" | ForEach-Object {
    Write-Host "削除: $($_.FullName)" -ForegroundColor Red
    Remove-Item $_.FullName -Force
}

# 2. ビルドキャッシュのクリーンアップ
Write-Host "2. ビルドキャッシュをクリーンアップ中..." -ForegroundColor Cyan
$binDirs = Get-ChildItem -Path $projectRoot -Recurse -Directory -Name "bin" | ForEach-Object { Join-Path $projectRoot $_ }
$objDirs = Get-ChildItem -Path $projectRoot -Recurse -Directory -Name "obj" | ForEach-Object { Join-Path $projectRoot $_ }

foreach ($dir in $binDirs + $objDirs) {
    if (Test-Path $dir) {
        Write-Host "削除: $dir" -ForegroundColor Red
        Remove-Item $dir -Recurse -Force
    }
}

# 3. Visual Studio キャッシュクリア
Write-Host "3. Visual Studio関連ファイルをクリーンアップ中..." -ForegroundColor Cyan
$vsDir = Join-Path $projectRoot ".vs"
if (Test-Path $vsDir) {
    Write-Host "削除: $vsDir" -ForegroundColor Red
    Remove-Item $vsDir -Recurse -Force
}

# 4. NuGetキャッシュクリア（オプション）
Write-Host "4. NuGetパッケージキャッシュをクリア中..." -ForegroundColor Cyan
& dotnet nuget locals all --clear

# 5. ソリューションの段階的ビルド
Write-Host "5. ソリューションを段階的にビルド中..." -ForegroundColor Cyan
Set-Location $projectRoot

Write-Host "5.1 Core プロジェクトビルド..." -ForegroundColor Yellow
& dotnet build "Baketa.Core\Baketa.Core.csproj" --verbosity minimal

Write-Host "5.2 Infrastructure プロジェクトビルド..." -ForegroundColor Yellow  
& dotnet build "Baketa.Infrastructure\Baketa.Infrastructure.csproj" --verbosity minimal

Write-Host "5.3 全体ソリューションビルド..." -ForegroundColor Yellow
& dotnet build "Baketa.sln" --verbosity minimal

Write-Host "=== 修正完了 ===" -ForegroundColor Green
Write-Host "エラーが解決されているかどうかを確認してください。" -ForegroundColor Yellow
