# Claude Code Notification Test Script

Write-Host "=== Claude Code Notification Test ===" -ForegroundColor Green

# 1. Check settings file
$settingsFile = "E:\dev\Baketa\.claude\settings.json"
if (Test-Path $settingsFile) {
    Write-Host "Settings file found: $settingsFile" -ForegroundColor Green
    
    try {
        $settings = Get-Content $settingsFile | ConvertFrom-Json
        Write-Host "Settings content:" -ForegroundColor Cyan
        $settings | ConvertTo-Json -Depth 3
        
        if ($settings.preferredNotifChannel) {
            Write-Host "Notification channel: $($settings.preferredNotifChannel)" -ForegroundColor Green
        } else {
            Write-Host "No notification channel configured" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "Error reading settings: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "Settings file not found: $settingsFile" -ForegroundColor Red
}

# 2. Terminal bell test
Write-Host "`n*** Terminal Bell Test ***" -ForegroundColor Yellow
Write-Host "Testing bell sound..." -ForegroundColor Cyan

# Ring the bell
[Console]::Beep()
Write-Host "`a" -NoNewline  # ASCII bell character

Write-Host "`nBell test completed" -ForegroundColor Green

# 3. Claude Code test instructions
Write-Host "`n*** Claude Code Test Instructions ***" -ForegroundColor Cyan
Write-Host "1. Run this command:" -ForegroundColor Gray
Write-Host "   claude 'Notification test - please ring bell when this completes'" -ForegroundColor Gray
Write-Host "2. Check if bell rings when task completes" -ForegroundColor Gray
Write-Host "3. If no sound, restart Claude Code and retest" -ForegroundColor Gray

Write-Host "`n*** Notification Test Complete ***" -ForegroundColor Green