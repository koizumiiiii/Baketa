# Baketa URI Scheme Registration Script
# This script registers the 'baketa://' URI scheme for Patreon OAuth callback handling
#
# Usage:
#   .\register-uri-scheme.ps1              # Register with default path (current directory)
#   .\register-uri-scheme.ps1 -ExePath "C:\Path\To\Baketa.exe"  # Register with specific path
#   .\register-uri-scheme.ps1 -Unregister  # Remove the URI scheme registration
#
# Requires: Run as administrator or with permission to write to HKCU\Software\Classes

param(
    [string]$ExePath,
    [switch]$Unregister,
    [switch]$Force
)

$SchemeKey = "HKCU:\Software\Classes\baketa"
$SchemeName = "baketa"
$AppName = "Baketa Translation Overlay"

function Write-Step {
    param([string]$Message)
    Write-Host "[*] $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[+] $Message" -ForegroundColor Green
}

function Write-Error {
    param([string]$Message)
    Write-Host "[-] $Message" -ForegroundColor Red
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[!] $Message" -ForegroundColor Yellow
}

# Unregister the URI scheme
if ($Unregister) {
    Write-Step "Unregistering '$SchemeName://' URI scheme..."

    if (Test-Path $SchemeKey) {
        try {
            Remove-Item -Path $SchemeKey -Recurse -Force
            Write-Success "URI scheme '$SchemeName://' has been unregistered."
        }
        catch {
            Write-Error "Failed to unregister URI scheme: $_"
            exit 1
        }
    }
    else {
        Write-Warning "URI scheme '$SchemeName://' is not registered."
    }
    exit 0
}

# Determine the executable path
if ([string]::IsNullOrEmpty($ExePath)) {
    # Try to find Baketa.exe in common locations
    $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ProjectRoot = Split-Path -Parent $ScriptDir

    $PossiblePaths = @(
        # Development paths
        "$ProjectRoot\Baketa.UI\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Baketa.exe",
        "$ProjectRoot\Baketa.UI\bin\Release\net8.0-windows10.0.19041.0\win-x64\Baketa.exe",
        # Release package path
        "$ProjectRoot\release\Baketa.exe",
        # Current directory
        "$PWD\Baketa.exe",
        # Same directory as script
        "$ScriptDir\Baketa.exe"
    )

    foreach ($Path in $PossiblePaths) {
        if (Test-Path $Path) {
            $ExePath = $Path
            break
        }
    }

    if ([string]::IsNullOrEmpty($ExePath)) {
        Write-Error "Could not find Baketa.exe. Please specify the path with -ExePath parameter."
        Write-Host ""
        Write-Host "Usage examples:" -ForegroundColor White
        Write-Host '  .\register-uri-scheme.ps1 -ExePath "C:\Program Files\Baketa\Baketa.exe"'
        Write-Host '  .\register-uri-scheme.ps1 -ExePath ".\Baketa.exe"'
        exit 1
    }
}

# Resolve the full path
$ExePath = Resolve-Path $ExePath -ErrorAction SilentlyContinue
if (-not $ExePath) {
    Write-Error "Specified executable path does not exist: $ExePath"
    exit 1
}

$ExeFullPath = $ExePath.Path

Write-Host ""
Write-Host "========================================" -ForegroundColor White
Write-Host " Baketa URI Scheme Registration" -ForegroundColor White
Write-Host "========================================" -ForegroundColor White
Write-Host ""
Write-Host "Executable: $ExeFullPath" -ForegroundColor White
Write-Host "URI Scheme: $SchemeName://" -ForegroundColor White
Write-Host ""

# Check if already registered
if ((Test-Path $SchemeKey) -and -not $Force) {
    $CurrentCommand = (Get-ItemProperty "$SchemeKey\shell\open\command" -ErrorAction SilentlyContinue).'(default)'
    Write-Warning "URI scheme '$SchemeName://' is already registered."
    Write-Host "Current command: $CurrentCommand" -ForegroundColor Gray
    Write-Host ""

    $response = Read-Host "Do you want to update the registration? (y/N)"
    if ($response -ne 'y' -and $response -ne 'Y') {
        Write-Host "Registration cancelled."
        exit 0
    }
}

# Register the URI scheme
Write-Step "Registering '$SchemeName://' URI scheme..."

try {
    # Create the main scheme key
    if (-not (Test-Path $SchemeKey)) {
        New-Item -Path $SchemeKey -Force | Out-Null
    }

    # Set the default value (description)
    Set-ItemProperty -Path $SchemeKey -Name "(default)" -Value "URL:$AppName Protocol"

    # Set the URL Protocol (empty string indicates this is a protocol handler)
    Set-ItemProperty -Path $SchemeKey -Name "URL Protocol" -Value ""

    # Create DefaultIcon key (optional, but nice to have)
    $IconKey = "$SchemeKey\DefaultIcon"
    if (-not (Test-Path $IconKey)) {
        New-Item -Path $IconKey -Force | Out-Null
    }
    Set-ItemProperty -Path $IconKey -Name "(default)" -Value "`"$ExeFullPath`",0"

    # Create shell\open\command key
    $ShellKey = "$SchemeKey\shell"
    $OpenKey = "$ShellKey\open"
    $CommandKey = "$OpenKey\command"

    if (-not (Test-Path $ShellKey)) {
        New-Item -Path $ShellKey -Force | Out-Null
    }
    if (-not (Test-Path $OpenKey)) {
        New-Item -Path $OpenKey -Force | Out-Null
    }
    if (-not (Test-Path $CommandKey)) {
        New-Item -Path $CommandKey -Force | Out-Null
    }

    # Set the command to execute
    # %1 will be replaced with the full URI (e.g., baketa://patreon/callback?code=xxx&state=yyy)
    $Command = "`"$ExeFullPath`" `"%1`""
    Set-ItemProperty -Path $CommandKey -Name "(default)" -Value $Command

    Write-Success "URI scheme registered successfully!"
    Write-Host ""
    Write-Host "Registry entries created:" -ForegroundColor White
    Write-Host "  Key: $SchemeKey" -ForegroundColor Gray
    Write-Host "  URL Protocol: (empty)" -ForegroundColor Gray
    Write-Host "  Command: $Command" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Test the registration by opening this URL in a browser:" -ForegroundColor White
    Write-Host "  baketa://test" -ForegroundColor Yellow
    Write-Host ""
    Write-Success "Patreon OAuth callback will now work correctly."
}
catch {
    Write-Error "Failed to register URI scheme: $_"
    exit 1
}
