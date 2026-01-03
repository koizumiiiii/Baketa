<#
.SYNOPSIS
    Issue #249: NetSparkle Ed25519 キーペア生成スクリプト

.DESCRIPTION
    自動アップデート用のEd25519キーペアを生成します。
    - 公開鍵: UpdateService.cs に埋め込み
    - 秘密鍵: GitHub Secrets に登録 (NETSPARKLE_ED25519_PRIVATE_KEY)

.NOTES
    このスクリプトは開発者が一度だけ実行します。
    生成されたキーは安全に保管してください。
#>

param(
    [switch]$ExportOnly,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

if ($Help) {
    Write-Host @"
Usage: .\generate-update-keys.ps1 [options]

Options:
    -ExportOnly    既存のキーを表示するのみ（新規生成しない）
    -Help          このヘルプを表示

Examples:
    .\generate-update-keys.ps1              # 新規キーペア生成
    .\generate-update-keys.ps1 -ExportOnly  # 既存キーを表示
"@
    exit 0
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " NetSparkle Ed25519 Key Generator" -ForegroundColor Cyan
Write-Host " Issue #249 Auto-Update Feature" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if the tool is installed
$toolInstalled = $null
try {
    $toolInstalled = Get-Command "netsparkle-generate-appcast" -ErrorAction SilentlyContinue
} catch {
    $toolInstalled = $null
}

if (-not $toolInstalled) {
    Write-Host "[Step 1/3] Installing NetSparkleUpdater.Tools.AppCastGenerator..." -ForegroundColor Yellow
    dotnet tool install --global NetSparkleUpdater.Tools.AppCastGenerator

    # Refresh PATH
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

    Write-Host "Tool installed successfully." -ForegroundColor Green
} else {
    Write-Host "[Step 1/3] NetSparkle AppCast Generator already installed." -ForegroundColor Green
}

Write-Host ""

# Key storage location
$keyPath = Join-Path $env:LOCALAPPDATA "netsparkle"
$privateKeyFile = Join-Path $keyPath "NetSparkle_Ed25519.priv"
$publicKeyFile = Join-Path $keyPath "NetSparkle_Ed25519.pub"

$keysExist = (Test-Path $privateKeyFile) -and (Test-Path $publicKeyFile)

if ($ExportOnly) {
    if (-not $keysExist) {
        Write-Host "ERROR: No keys found at $keyPath" -ForegroundColor Red
        Write-Host "Run this script without -ExportOnly to generate keys." -ForegroundColor Yellow
        exit 1
    }
} else {
    if ($keysExist) {
        Write-Host "[Warning] Keys already exist at:" -ForegroundColor Yellow
        Write-Host "  Private: $privateKeyFile" -ForegroundColor DarkYellow
        Write-Host "  Public:  $publicKeyFile" -ForegroundColor DarkYellow
        Write-Host ""

        $response = Read-Host "Overwrite existing keys? (y/N)"
        if ($response -ne "y" -and $response -ne "Y") {
            Write-Host "Using existing keys." -ForegroundColor Cyan
        } else {
            Write-Host "[Step 2/3] Generating new Ed25519 key pair..." -ForegroundColor Yellow
            netsparkle-generate-appcast --generate-keys
            Write-Host "New keys generated." -ForegroundColor Green
        }
    } else {
        Write-Host "[Step 2/3] Generating Ed25519 key pair..." -ForegroundColor Yellow
        netsparkle-generate-appcast --generate-keys
        Write-Host "Keys generated successfully." -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "[Step 3/3] Exporting keys..." -ForegroundColor Yellow
Write-Host ""

# Export keys to console
$exportOutput = netsparkle-generate-appcast --export 2>&1

# Parse the output to extract keys
$publicKey = ""
$privateKey = ""

foreach ($line in $exportOutput -split "`n") {
    if ($line -match "Public Key:\s*(.+)$") {
        $publicKey = $matches[1].Trim()
    }
    if ($line -match "Private Key:\s*(.+)$") {
        $privateKey = $matches[1].Trim()
    }
}

# Alternative: Read from files if export didn't work
if ([string]::IsNullOrWhiteSpace($publicKey) -and (Test-Path $publicKeyFile)) {
    $publicKey = (Get-Content $publicKeyFile -Raw).Trim()
}
if ([string]::IsNullOrWhiteSpace($privateKey) -and (Test-Path $privateKeyFile)) {
    $privateKey = (Get-Content $privateKeyFile -Raw).Trim()
}

Write-Host "========================================" -ForegroundColor Green
Write-Host " KEY GENERATION COMPLETE" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

if (-not [string]::IsNullOrWhiteSpace($publicKey)) {
    Write-Host "PUBLIC KEY (embed in UpdateService.cs):" -ForegroundColor Cyan
    Write-Host "----------------------------------------" -ForegroundColor DarkGray
    Write-Host $publicKey -ForegroundColor White
    Write-Host ""
}

if (-not [string]::IsNullOrWhiteSpace($privateKey)) {
    Write-Host "PRIVATE KEY (add to GitHub Secrets):" -ForegroundColor Magenta
    Write-Host "----------------------------------------" -ForegroundColor DarkGray
    Write-Host $privateKey -ForegroundColor White
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Yellow
Write-Host " NEXT STEPS" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Copy the PUBLIC KEY above and update:" -ForegroundColor White
Write-Host "   Baketa.UI/Services/UpdateService.cs" -ForegroundColor DarkGray
Write-Host "   -> Ed25519PublicKey constant" -ForegroundColor DarkGray
Write-Host ""
Write-Host "2. Copy the PRIVATE KEY above and add to GitHub:" -ForegroundColor White
Write-Host "   Settings -> Secrets and variables -> Actions" -ForegroundColor DarkGray
Write-Host "   -> New repository secret" -ForegroundColor DarkGray
Write-Host "   Name: NETSPARKLE_ED25519_PRIVATE_KEY" -ForegroundColor DarkGray
Write-Host ""
Write-Host "SECURITY WARNING:" -ForegroundColor Red
Write-Host "  - NEVER commit the private key to the repository" -ForegroundColor Red
Write-Host "  - Store the private key securely" -ForegroundColor Red
Write-Host "  - The private key file is at: $privateKeyFile" -ForegroundColor DarkRed
Write-Host ""
