@echo off
cd /d "E:\dev\Baketa"
call "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"
msbuild "BaketaCaptureNative\BaketaCaptureNative.sln" /p:Configuration=Debug /p:Platform=x64
echo Build completed at %TIME%