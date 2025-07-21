# Baketa.UI Debug Log Runner

param(
    [int]$WaitSeconds = 30
)

$ErrorActionPreference = "Stop"

# 既存プロセス終了
Write-Host "[INFO] Stopping existing Baketa.UI processes..."
Get-Process -Name "Baketa.UI" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

# アプリケーションディレクトリ
$appDir = "E:\dev\Baketa\Baketa.UI\bin\x64\Debug\net8.0-windows10.0.19041.0"
$exePath = Join-Path $appDir "Baketa.UI.exe"
$logFile = Join-Path $appDir "detailed_debug.log"

if (Test-Path $logFile) {
    Remove-Item $logFile -Force
}

Write-Host "[INFO] Starting Baketa.UI.exe with detailed logging..."
Write-Host "[INFO] Exe Path: $exePath"
Write-Host "[INFO] Log File: $logFile"

try {
    # プロセス開始
    $process = Start-Process -FilePath $exePath -WorkingDirectory $appDir -PassThru -WindowStyle Normal
    
    Write-Host "[INFO] Process started with PID: $($process.Id)"
    Write-Host "[INFO] Waiting $WaitSeconds seconds for application to initialize..."
    
    Start-Sleep -Seconds $WaitSeconds
    
    # プロセス状態確認
    if (!$process.HasExited) {
        Write-Host "[SUCCESS] Application is running normally"
        Write-Host "[INFO] Process Status: Running"
        Write-Host "[INFO] Memory Usage: $([math]::Round($process.WorkingSet64/1MB, 2)) MB"
    } else {
        Write-Host "[WARNING] Application exited early"
        Write-Host "[INFO] Exit Code: $($process.ExitCode)"
    }
    
    # ログ確認
    if (Test-Path $logFile) {
        Write-Host "`n[LOG] Debug log contents (last 50 lines):"
        Write-Host "=" * 60
        Get-Content $logFile -Tail 50 | ForEach-Object { Write-Host $_ }
        Write-Host "=" * 60
    } else {
        Write-Host "`n[INFO] No debug log file found - using Visual Studio output window or console"
    }
    
} catch {
    Write-Host "[ERROR] Failed to start application: $($_.Exception.Message)"
    return 1
}

Write-Host "`n[INFO] Debug session completed"
return 0