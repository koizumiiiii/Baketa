# Gemini API code review wrapper script
# Falls back to static analysis (code-review-simple.ps1) if Gemini API is unavailable

param(
    [Parameter(Position=0, ValueFromRemainingArguments=$true)]
    [string[]]$PromptArgs
)

# Determine project root based on script location
$ScriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptPath

# Join prompt arguments
$Prompt = $PromptArgs -join " "

Write-Host "`n🤖 Gemini API Code Review..." -ForegroundColor Cyan
Write-Host "Prompt: $Prompt" -ForegroundColor White
Write-Host ""

# Call Gemini API (via Python gemini-cli)
# Requires GEMINI_API_KEY environment variable
if ($env:GEMINI_API_KEY) {
    Write-Host "✅ GEMINI_API_KEY is set. Calling Gemini API..." -ForegroundColor Green

    try {
        # Call Gemini API using custom Python script (supports latest models)
        $GeminiScript = Join-Path $ScriptPath "gemini-review.py"
        $Output = & python $GeminiScript $Prompt 2>&1
        Write-Host $Output

        if ($LASTEXITCODE -eq 0) {
            Write-Host "`n✅ Gemini API review completed successfully" -ForegroundColor Green
            exit 0
        } else {
            Write-Host "`n⚠️ Gemini API call failed (exit code: $LASTEXITCODE)" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "`n⚠️ Error during Gemini API call: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "⚠️ GEMINI_API_KEY environment variable is not set." -ForegroundColor Yellow
    Write-Host "Falling back to static analysis..." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "🔍 Static analysis fallback is disabled (code-review scripts have encoding issues)" -ForegroundColor Yellow
Write-Host "💡 Hint: To use Gemini API, set the GEMINI_API_KEY environment variable." -ForegroundColor Cyan
Write-Host "Setup: `$env:GEMINI_API_KEY = 'your-api-key-here'" -ForegroundColor White
