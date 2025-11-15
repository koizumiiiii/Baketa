@echo off
cd /d "E:\dev\Baketa"
call "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"
echo Building RELEASE version to avoid DEBUG graphics constraints...
msbuild "BaketaCaptureNative\BaketaCaptureNative.sln" /p:Configuration=Release /p:Platform=x64
echo Release build completed at %TIME%
pause