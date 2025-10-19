$proc = Get-Process -Id 30136
$ws = [math]::Round($proc.WorkingSet64/1MB,2)
$pm = [math]::Round($proc.PrivateMemorySize64/1MB,2)
Write-Host "=== Measurement 1: After Startup ==="
Write-Host "Process: $($proc.ProcessName) (PID: $($proc.Id))"
Write-Host "WorkingSet: $ws MB"
Write-Host "PrivateMemory: $pm MB"
Write-Host "Threads: $($proc.Threads.Count)"
Write-Host "Handles: $($proc.HandleCount)"
Write-Host ""
Write-Host "Save this data for comparison after translation tests."
