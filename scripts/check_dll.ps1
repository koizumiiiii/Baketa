# DLL Check Script

$dllPath = 'E:\dev\Baketa\Baketa.UI\bin\x64\Debug\net8.0-windows10.0.19041.0\BaketaCaptureNative.dll'

if (Test-Path $dllPath) {
    $file = Get-Item $dllPath
    Write-Host "File exists: $($file.Length) bytes"
    Write-Host "Last Modified: $($file.LastWriteTime)"
    
    # Try to get file version info
    try {
        $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath)
        Write-Host "File Description: $($info.FileDescription)"
        Write-Host "Product Name: $($info.ProductName)"
        Write-Host "File Version: $($info.FileVersion)"
    } catch {
        Write-Host "Version info error: $($_.Exception.Message)"
    }
    
    # Check if it's a managed assembly
    try {
        $assembly = [System.Reflection.Assembly]::LoadFile($dllPath)
        Write-Host "Managed Assembly: Yes"
    } catch {
        Write-Host "Native DLL (expected): $($_.Exception.Message)"
    }
    
    # Use dumpbin to check exports
    try {
        & "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\14.41.34120\bin\Hostx64\x64\dumpbin.exe" /exports $dllPath
    } catch {
        Write-Host "dumpbin not available"
    }
    
} else {
    Write-Host "File not found: $dllPath"
}