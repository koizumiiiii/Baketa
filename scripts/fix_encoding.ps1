# C++ Files Encoding Fix Script
# C4819 warning resolution - Convert to UTF-8 with BOM

$ErrorActionPreference = "Stop"

$files = @(
    "E:\dev\Baketa\BaketaCaptureNative\src\pch.cpp",
    "E:\dev\Baketa\BaketaCaptureNative\src\BaketaCaptureNative.cpp", 
    "E:\dev\Baketa\BaketaCaptureNative\src\WindowsCaptureSession.cpp",
    "E:\dev\Baketa\BaketaCaptureNative\include\BaketaCaptureNative.h",
    "E:\dev\Baketa\BaketaCaptureNative\src\pch.h",
    "E:\dev\Baketa\BaketaCaptureNative\src\WindowsCaptureSession.h"
)

Write-Host "[START] Fixing C++ file encoding (C4819 warning resolution)" -ForegroundColor Green

foreach ($file in $files) {
    if (Test-Path $file) {
        try {
            # Read file content as UTF-8
            $content = Get-Content -Path $file -Raw -Encoding UTF8
            
            # Create UTF-8 with BOM encoder
            $utf8WithBom = New-Object System.Text.UTF8Encoding($true)
            
            # Write back with UTF-8 BOM
            [System.IO.File]::WriteAllText($file, $content, $utf8WithBom)
            
            Write-Host "[FIXED] $file -> UTF-8 with BOM" -ForegroundColor Yellow
        }
        catch {
            Write-Host "[ERROR] Failed to fix $file : $($_.Exception.Message)" -ForegroundColor Red
        }
    } else {
        Write-Host "[SKIP] File not found: $file" -ForegroundColor Gray
    }
}

Write-Host "[COMPLETE] Encoding fix completed" -ForegroundColor Green