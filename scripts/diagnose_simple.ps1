# Simple Capture Diagnostic Script

param(
    [int]$TestDurationSeconds = 90
)

$ErrorActionPreference = "Continue"

Write-Host "=== Baketa Capture & OCR Diagnostic ===" -ForegroundColor Cyan
Write-Host "Testing Duration: $TestDurationSeconds seconds"

# Start application
$appDir = "E:\dev\Baketa\Baketa.UI\bin\x64\Debug\net8.0-windows10.0.19041.0"
$exePath = Join-Path $appDir "Baketa.UI.exe"

# Stop existing processes
Get-Process -Name "Baketa.UI" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host "Starting application..." -ForegroundColor Green
$process = Start-Process -FilePath $exePath -WorkingDirectory $appDir -PassThru -WindowStyle Normal

if (!$process) {
    Write-Host "Failed to start application" -ForegroundColor Red
    return 1
}

Write-Host "Application started - PID: $($process.Id)" -ForegroundColor Green

# Wait for initialization
Write-Host "Waiting for initialization (30 seconds)..." -ForegroundColor Yellow
Start-Sleep -Seconds 30

# Start monitoring
Write-Host "Starting diagnostic monitoring..." -ForegroundColor Cyan
$startTime = Get-Date
$endTime = $startTime.AddSeconds($TestDurationSeconds)

$maxMemoryMB = 0

while ((Get-Date) -lt $endTime -and !$process.HasExited) {
    Start-Sleep -Seconds 10
    
    try {
        $currentProcess = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($currentProcess) {
            $memoryMB = [math]::Round($currentProcess.WorkingSet64/1MB, 2)
            $maxMemoryMB = [math]::Max($maxMemoryMB, $memoryMB)
            
            $elapsed = ((Get-Date) - $startTime).TotalSeconds
            Write-Host "Elapsed: $([math]::Round($elapsed))s | Memory: ${memoryMB}MB" -ForegroundColor White
        }
    } catch {
        Write-Host "Process check error: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# Final report
Write-Host "`n=== DIAGNOSTIC REPORT ===" -ForegroundColor Cyan

if ($process.HasExited) {
    Write-Host "Application Status: EXITED (Code: $($process.ExitCode))" -ForegroundColor Red
} else {
    Write-Host "Application Status: RUNNING" -ForegroundColor Green
    $finalMemory = [math]::Round((Get-Process -Id $process.Id).WorkingSet64/1MB, 2)
    Write-Host "Final Memory: ${finalMemory}MB" -ForegroundColor White
}

Write-Host "Peak Memory: ${maxMemoryMB}MB" -ForegroundColor White
Write-Host "Total Runtime: $([math]::Round(((Get-Date) - $startTime).TotalMinutes, 1)) minutes" -ForegroundColor White

Write-Host "`n=== OBSERVED ISSUES ===" -ForegroundColor Yellow
Write-Host "1. Large window capture failures (2560x1080)"
Write-Host "2. MarshalDirectiveException still occurring"
Write-Host "3. OCR timeouts (TaskCanceledException)"
Write-Host "4. PrintWindow fallback behavior"

Write-Host "`n=== NEXT ACTIONS NEEDED ===" -ForegroundColor Green
Write-Host "1. Debug remaining P/Invoke issues"
Write-Host "2. Test specific window sizes for capture"
Write-Host "3. Adjust OCR timeout settings"
Write-Host "4. Verify Windows Graphics Capture API permissions"

if (!$process.HasExited) {
    Write-Host "`nApplication continues running - manual testing available" -ForegroundColor Green
}

Write-Host "`nDiagnostic completed" -ForegroundColor Cyan
return 0