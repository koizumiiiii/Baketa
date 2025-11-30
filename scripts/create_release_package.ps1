# Baketa Release Package Creation Script
param(
    [string]$Version = "beta-0.1.0"
)

$ErrorActionPreference = "Stop"

$releaseDir = "E:\dev\Baketa\Baketa.UI\bin\Release\net8.0-windows10.0.19041.0\win-x64"
$pyinstallerDir = "E:\dev\Baketa\grpc_server\dist\BaketaTranslationServer"
$packageDir = "E:\dev\Baketa\release_package"
$packageName = "Baketa-$Version-win-x64"

Write-Host "Creating Baketa Release Package: $packageName" -ForegroundColor Cyan

# 1. Cleanup
Write-Host "Cleaning up existing package directory..."
if (Test-Path $packageDir) {
    Remove-Item -Recurse -Force $packageDir
}
New-Item -ItemType Directory -Path $packageDir | Out-Null

# 2. Copy .NET Release build
Write-Host "Copying .NET Release build..."
if (-not (Test-Path $releaseDir)) {
    Write-Error "Release build not found: $releaseDir"
    exit 1
}
Copy-Item -Recurse -Path "$releaseDir\*" -Destination $packageDir

# 3. Copy PyInstaller exe
Write-Host "Copying PyInstaller translation server..."
if (-not (Test-Path $pyinstallerDir)) {
    Write-Error "PyInstaller build not found: $pyinstallerDir"
    exit 1
}
$targetGrpcDir = Join-Path $packageDir "grpc_server\BaketaTranslationServer"
New-Item -ItemType Directory -Path $targetGrpcDir -Force | Out-Null
Copy-Item -Recurse -Path "$pyinstallerDir\*" -Destination $targetGrpcDir

# 4. Calculate size
Write-Host "Calculating package size..."
$totalSize = (Get-ChildItem -Recurse $packageDir | Measure-Object -Property Length -Sum).Sum / 1GB
Write-Host "Total: $([math]::Round($totalSize, 2)) GB" -ForegroundColor Green

# 5. Done
Write-Host ""
Write-Host "Release package created successfully!" -ForegroundColor Green
Write-Host "Location: $packageDir" -ForegroundColor Yellow
