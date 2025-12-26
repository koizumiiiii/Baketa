@echo off
REM Baketa URI Scheme Registration Script Wrapper
REM This batch file runs the PowerShell script to register the baketa:// URI scheme
REM
REM Usage:
REM   register-uri-scheme.bat              - Register with auto-detected path
REM   register-uri-scheme.bat unregister   - Remove the registration

setlocal

cd /d "%~dp0"

if /i "%1"=="unregister" (
    echo Unregistering baketa:// URI scheme...
    powershell -ExecutionPolicy Bypass -File "%~dp0register-uri-scheme.ps1" -Unregister
) else (
    echo Registering baketa:// URI scheme...
    powershell -ExecutionPolicy Bypass -File "%~dp0register-uri-scheme.ps1" %*
)

pause
