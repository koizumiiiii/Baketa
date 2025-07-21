# Baketa Complete Build Script - Simple Version
# Windows Graphics Capture API Native DLL + .NET Solution

param(
    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [Parameter()]
    [switch]$SkipNative,
    
    [Parameter()]
    [switch]$SkipDotNet,
    
    [Parameter()]
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"

# Path settings
$RootPath = Split-Path $PSScriptRoot -Parent
$NativeSolutionPath = Join-Path $RootPath "BaketaCaptureNative\BaketaCaptureNative.sln"
$DotNetSolutionPath = Join-Path $RootPath "Baketa.sln"
$NativeDllPath = Join-Path $RootPath "BaketaCaptureNative\bin\$Configuration\BaketaCaptureNative.dll"
$DotNetOutputPath = Join-Path $RootPath "Baketa.UI\bin\x64\$Configuration\net8.0-windows10.0.19041.0"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "ERROR" { "Red" }
        "WARN"  { "Yellow" }
        "SUCCESS" { "Green" }
        default { "White" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

function Test-VisualStudio2022 {
    $vsPaths = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\Tools\VsDevCmd.bat",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\Common7\Tools\VsDevCmd.bat"
    )
    
    foreach ($path in $vsPaths) {
        if (Test-Path $path) {
            return $path
        }
    }
    
    throw "Visual Studio 2022 not found. C++/WinRT development environment required."
}

Write-Log "[START] Baketa Complete Build Script" "SUCCESS"
Write-Log "Configuration: $Configuration"
Write-Log "Root Path: $RootPath"

try {
    # Step 1: Build Native DLL
    if (-not $SkipNative) {
        Write-Log "[STEP 1] Building Native DLL (BaketaCaptureNative)" "SUCCESS"
        
        $vsDevCmd = Test-VisualStudio2022
        Write-Log "Visual Studio Found: $vsDevCmd"
        
        if (-not (Test-Path $NativeSolutionPath)) {
            throw "Native solution not found: $NativeSolutionPath"
        }
        
        Write-Log "Building C++/WinRT project..."
        & cmd /c "`"$vsDevCmd`" && msbuild `"$NativeSolutionPath`" /p:Configuration=$Configuration /p:Platform=x64 /v:minimal"
        
        if ($LASTEXITCODE -ne 0) {
            throw "Native DLL build failed. Exit code: $LASTEXITCODE"
        }
        
        if (-not (Test-Path $NativeDllPath)) {
            throw "Native DLL not generated: $NativeDllPath"
        }
        
        $dllInfo = Get-Item $NativeDllPath
        Write-Log "[SUCCESS] Native DLL Built: $($dllInfo.Name) ($($dllInfo.Length) bytes)" "SUCCESS"
        
        # Step 2: Copy DLL
        Write-Log "[STEP 2] Copying Native DLL to Output Directory" "SUCCESS"
        
        if (-not (Test-Path $DotNetOutputPath)) {
            Write-Log "Creating output directory: $DotNetOutputPath"
            New-Item -ItemType Directory -Path $DotNetOutputPath -Force | Out-Null
        }
        
        $targetDllPath = Join-Path $DotNetOutputPath "BaketaCaptureNative.dll"
        Copy-Item $NativeDllPath $targetDllPath -Force
        Write-Log "[SUCCESS] DLL Copied: $targetDllPath" "SUCCESS"
        
        # Copy PDB file for debugging
        $nativePdbPath = Join-Path $RootPath "BaketaCaptureNative\bin\$Configuration\BaketaCaptureNative.pdb"
        if (Test-Path $nativePdbPath) {
            $targetPdbPath = Join-Path $DotNetOutputPath "BaketaCaptureNative.pdb"
            Copy-Item $nativePdbPath $targetPdbPath -Force
            Write-Log "[SUCCESS] PDB Copied: $targetPdbPath" "SUCCESS"
        }
    } else {
        Write-Log "[SKIP] Native DLL Build" "WARN"
    }
    
    # Step 3: Build .NET Solution
    if (-not $SkipDotNet) {
        Write-Log "[STEP 3] Building .NET Solution" "SUCCESS"
        
        if (-not (Test-Path $DotNetSolutionPath)) {
            throw ".NET solution not found: $DotNetSolutionPath"
        }
        
        $dotnetVersion = dotnet --version
        Write-Log ".NET SDK Version: $dotnetVersion"
        
        Push-Location $RootPath
        try {
            if ($VerboseOutput) {
                dotnet build $DotNetSolutionPath --configuration $Configuration --verbosity normal
            } else {
                dotnet build $DotNetSolutionPath --configuration $Configuration --verbosity minimal
            }
            
            if ($LASTEXITCODE -ne 0) {
                throw ".NET solution build failed. Exit code: $LASTEXITCODE"
            }
            
            Write-Log "[SUCCESS] .NET Solution Built Successfully" "SUCCESS"
        }
        finally {
            Pop-Location
        }
    } else {
        Write-Log "[SKIP] .NET Solution Build" "WARN"
    }
    
    # Step 4: Verify Build Results
    Write-Log "[STEP 4] Verifying Build Results" "SUCCESS"
    
    $mainExePath = Join-Path $DotNetOutputPath "Baketa.UI.exe"
    $nativeDllInOutput = Join-Path $DotNetOutputPath "BaketaCaptureNative.dll"
    
    if (Test-Path $mainExePath) {
        $exeInfo = Get-Item $mainExePath
        Write-Log "[OK] Baketa.UI.exe: $($exeInfo.Length) bytes, Modified: $($exeInfo.LastWriteTime)"
    } else {
        Write-Log "[ERROR] Baketa.UI.exe: NOT FOUND" "ERROR"
    }
    
    if (Test-Path $nativeDllInOutput) {
        $dllInfo = Get-Item $nativeDllInOutput
        Write-Log "[OK] BaketaCaptureNative.dll: $($dllInfo.Length) bytes, Modified: $($dllInfo.LastWriteTime)"
    } else {
        Write-Log "[ERROR] BaketaCaptureNative.dll: NOT FOUND" "ERROR"
    }
    
    # Final Status
    Write-Log "[COMPLETE] Build Complete! Ready to Run" "SUCCESS"
    Write-Log "Execute: dotnet run --project Baketa.UI --configuration $Configuration"
    Write-Log "Or run directly: $mainExePath"
    
    if ((Test-Path $mainExePath) -and (Test-Path $nativeDllInOutput)) {
        Write-Log "[READY] All components ready for execution!" "SUCCESS"
        return 0
    } else {
        Write-Log "[WARNING] Some components missing. Check build logs." "WARN"
        return 1
    }
    
} catch {
    Write-Log "[FAILED] Build Failed: $($_.Exception.Message)" "ERROR"
    if ($VerboseOutput) {
        Write-Log "Stack Trace: $($_.ScriptStackTrace)" "ERROR"
    }
    return 1
}