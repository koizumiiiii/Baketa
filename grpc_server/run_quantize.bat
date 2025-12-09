@echo off
cd /d %~dp0
cd ..
echo Current directory: %CD%
echo Looking for Python...

REM Try pyenv Python first
for /f "delims=" %%i in ('where python 2^>nul') do (
    echo Found: %%i
    "%%i" grpc_server\export_surya_onnx.py --quantize
    goto :eof
)

echo Python not found!
pause
