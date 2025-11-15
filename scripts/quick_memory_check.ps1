$proc = Get-Process | Where-Object { $_.ProcessName -eq 'Baketa.UI' } | Select-Object -First 1
if ($proc) {
    $ws = [math]::Round($proc.WorkingSet64/1MB,2)
    $pm = [math]::Round($proc.PrivateMemorySize64/1MB,2)
    Write-Host "Process: $($proc.ProcessName) (PID: $($proc.Id))"
    Write-Host "WorkingSet: $ws MB"
    Write-Host "PrivateMemory: $pm MB"
    Write-Host "Threads: $($proc.Threads.Count)"
    Write-Host "Handles: $($proc.HandleCount)"
} else {
    Write-Host "Baketa.UI process not found"
}
