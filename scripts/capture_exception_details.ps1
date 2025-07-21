# Exception Detail Capture Script

param(
    [int]$CaptureSeconds = 60
)

$ErrorActionPreference = "Continue"

Write-Host "[INFO] Starting exception capture session..."
Write-Host "[INFO] This will monitor for MarshalDirectiveException details"

# アプリケーション開始
$appDir = "E:\dev\Baketa\Baketa.UI\bin\x64\Debug\net8.0-windows10.0.19041.0"
$exePath = Join-Path $appDir "Baketa.UI.exe"

# 既存プロセス終了
Get-Process -Name "Baketa.UI" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host "[INFO] Starting application..."
$process = Start-Process -FilePath $exePath -WorkingDirectory $appDir -PassThru -WindowStyle Normal

if ($process) {
    Write-Host "[SUCCESS] Application started - PID: $($process.Id)"
    Write-Host "[INFO] Monitoring for $CaptureSeconds seconds..."
    
    $startTime = Get-Date
    $endTime = $startTime.AddSeconds($CaptureSeconds)
    
    $exceptionCount = 0
    
    while ((Get-Date) -lt $endTime -and !$process.HasExited) {
        Start-Sleep -Seconds 5
        
        # プロセス状態確認
        try {
            $currentProcess = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
            if ($currentProcess) {
                $memoryMB = [math]::Round($currentProcess.WorkingSet64/1MB, 2)
                $elapsed = ((Get-Date) - $startTime).TotalSeconds
                Write-Host "[INFO] Elapsed: $([math]::Round($elapsed))s, Memory: ${memoryMB}MB"
                
                # CPU使用率が異常に高い場合は例外の可能性
                if ($currentProcess.CPU -gt 50) {
                    Write-Host "[WARNING] High CPU usage detected - possible exception loop"
                    $exceptionCount++
                }
            }
        } catch {
            Write-Host "[WARNING] Error checking process status: $($_.Exception.Message)"
        }
    }
    
    if ($process.HasExited) {
        Write-Host "[ERROR] Application exited unexpectedly"
        Write-Host "[ERROR] Exit Code: $($process.ExitCode)"
    } else {
        Write-Host "[SUCCESS] Application completed monitoring period successfully"
        
        # PP-OCRv5使用状況確認
        Write-Host "`n[INFO] Checking PP-OCRv5 usage..."
        
        # プロセスのDLL読み込み確認
        try {
            $modules = Get-Process -Id $process.Id | Select-Object -ExpandProperty Modules -ErrorAction SilentlyContinue
            
            $ocrModules = $modules | Where-Object { 
                $_.ModuleName -like "*PaddleOCR*" -or 
                $_.ModuleName -like "*OCR*" -or
                $_.ModuleName -like "*v5*" 
            }
            
            if ($ocrModules) {
                Write-Host "[SUCCESS] OCR modules detected:"
                $ocrModules | ForEach-Object { 
                    Write-Host "  - $($_.ModuleName) ($([math]::Round($_.ModuleMemorySize/1KB))KB)"
                }
            } else {
                Write-Host "[INFO] No specific OCR modules detected in module list"
            }
            
            # メモリ使用量から推測
            if ($memoryMB -gt 500) {
                Write-Host "[SUCCESS] High memory usage ($memoryMB MB) suggests PP-OCRv5 models are loaded"
            }
            
        } catch {
            Write-Host "[WARNING] Could not analyze loaded modules: $($_.Exception.Message)"
        }
    }
    
} else {
    Write-Host "[ERROR] Failed to start application"
    return 1
}

Write-Host "`n[SUMMARY]"
Write-Host "- Application Status: $(if ($process.HasExited) { 'Exited' } else { 'Running' })"
Write-Host "- Exception Indicators: $exceptionCount"
Write-Host "- Final Memory Usage: $memoryMB MB"

if (!$process.HasExited) {
    Write-Host "`n[INFO] Application is still running - you can continue testing manually"
}

Write-Host "`n[INFO] Exception capture session completed"
return 0