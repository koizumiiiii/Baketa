# Baketa Resource Monitoring Script
# Monitors memory and handle usage for Baketa.UI and Python translation server

param(
    [int]$IntervalSeconds = 5,
    [string]$OutputFile = "baketa_resource_monitor.log"
)

$logPath = Join-Path (Get-Location) $OutputFile

Write-Host "[MONITOR] Baketa Resource Monitoring Started"
Write-Host "Log File: $logPath"
Write-Host "Interval: $IntervalSeconds seconds"
Write-Host "Stop: Ctrl+C"
Write-Host ""

# Initialize log file
$header = "Timestamp,Baketa_WorkingSet_MB,Baketa_PrivateBytes_MB,Baketa_HandleCount,Baketa_ThreadCount,Python_WorkingSet_MB,Python_PrivateBytes_MB,Python_HandleCount,Python_ThreadCount"
Set-Content -Path $logPath -Value $header

$startTime = Get-Date

while ($true) {
    try {
        $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        $elapsed = ((Get-Date) - $startTime).TotalMinutes

        # Get Baketa process
        $baketaProcess = Get-Process -Name "Baketa.UI" -ErrorAction SilentlyContinue

        # Get Python process (translation server)
        $pythonProcess = Get-Process -Name "python" -ErrorAction SilentlyContinue | Select-Object -First 1

        if ($baketaProcess) {
            $baketaWS = [math]::Round($baketaProcess.WorkingSet64 / 1MB, 2)
            $baketaPB = [math]::Round($baketaProcess.PrivateMemorySize64 / 1MB, 2)
            $baketaHandles = $baketaProcess.HandleCount
            $baketaThreads = $baketaProcess.Threads.Count
        } else {
            $baketaWS = 0
            $baketaPB = 0
            $baketaHandles = 0
            $baketaThreads = 0
        }

        if ($pythonProcess) {
            $pythonWS = [math]::Round($pythonProcess.WorkingSet64 / 1MB, 2)
            $pythonPB = [math]::Round($pythonProcess.PrivateMemorySize64 / 1MB, 2)
            $pythonHandles = $pythonProcess.HandleCount
            $pythonThreads = $pythonProcess.Threads.Count
        } else {
            $pythonWS = 0
            $pythonPB = 0
            $pythonHandles = 0
            $pythonThreads = 0
        }

        # Write to log file
        $logLine = "$timestamp,$baketaWS,$baketaPB,$baketaHandles,$baketaThreads,$pythonWS,$pythonPB,$pythonHandles,$pythonThreads"
        Add-Content -Path $logPath -Value $logLine

        # Console output
        Write-Host "[$timestamp] Elapsed: $([math]::Round($elapsed, 1)) min"
        Write-Host "  Baketa.UI : RAM $baketaWS MB (Private $baketaPB MB), Handles: $baketaHandles, Threads: $baketaThreads"
        Write-Host "  Python    : RAM $pythonWS MB (Private $pythonPB MB), Handles: $pythonHandles, Threads: $pythonThreads"
        Write-Host ""

    } catch {
        $errorMsg = $_.Exception.Message
        Write-Host "WARNING - Error: $errorMsg"
    }

    Start-Sleep -Seconds $IntervalSeconds
}
