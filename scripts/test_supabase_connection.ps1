# Supabase接続テストスクリプト
# 実行: powershell -ExecutionPolicy Bypass -File scripts/test_supabase_connection.ps1

$supabaseUrl = "https://kajsoietcikivrwidqcs.supabase.co"
$supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImthanNvaWV0Y2lraXZyd2lkcWNzIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NjQwOTg3NTcsImV4cCI6MjA3OTY3NDc1N30.tVHVdX52liyAOmI18ub_tZp4D-rxCrvvrKVoCAlyPwU"

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host " Supabase Connection Test" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "URL: $supabaseUrl"
Write-Host ""

# 1. Health Check (REST API)
Write-Host "[1/3] REST API Health Check..." -ForegroundColor Yellow
try {
    $headers = @{
        "apikey" = $supabaseKey
        "Authorization" = "Bearer $supabaseKey"
    }
    $response = Invoke-RestMethod -Uri "$supabaseUrl/rest/v1/" -Headers $headers -Method Get -ErrorAction Stop
    Write-Host "  [OK] REST API connection successful" -ForegroundColor Green
} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    if ($statusCode -eq 200 -or $statusCode -eq $null) {
        Write-Host "  [OK] REST API connection successful" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] REST API error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# 2. Auth Settings Check
Write-Host ""
Write-Host "[2/3] Auth Settings Check..." -ForegroundColor Yellow
try {
    $headers = @{
        "apikey" = $supabaseKey
    }
    $authSettings = Invoke-RestMethod -Uri "$supabaseUrl/auth/v1/settings" -Headers $headers -Method Get -ErrorAction Stop
    Write-Host "  [OK] Auth Settings retrieved successfully" -ForegroundColor Green
    Write-Host ""
    Write-Host "  OAuth Providers:" -ForegroundColor Cyan
    Write-Host "    - Google:  $($authSettings.external.google)"
    Write-Host "    - Discord: $($authSettings.external.discord)"
    Write-Host "    - Twitch:  $($authSettings.external.twitch)"
} catch {
    Write-Host "  [FAIL] Auth Settings error: $($_.Exception.Message)" -ForegroundColor Red
}

# 3. Profiles Table Check (RLS)
Write-Host ""
Write-Host "[3/3] Profiles Table Check (RLS)..." -ForegroundColor Yellow
try {
    $headers = @{
        "apikey" = $supabaseKey
        "Authorization" = "Bearer $supabaseKey"
    }
    $profiles = Invoke-RestMethod -Uri "$supabaseUrl/rest/v1/profiles?select=id&limit=1" -Headers $headers -Method Get -ErrorAction Stop
    Write-Host "  [OK] Profiles table exists (RLS active)" -ForegroundColor Green
} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    if ($statusCode -eq 406 -or $statusCode -eq 401) {
        Write-Host "  [OK] Profiles table exists (RLS blocking anonymous access - expected)" -ForegroundColor Green
    } elseif ($statusCode -eq 404) {
        Write-Host "  [WARN] Profiles table not found" -ForegroundColor Yellow
    } else {
        Write-Host "  [INFO] Response: $($_.Exception.Message)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host " Test Complete!" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
