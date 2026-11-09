@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title 打包 Astra 手机版导出工具

set "SRC=%~dp0"
set "ROOT=%~dp0.."
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\System.dll" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "CSC=%FW%\csc.exe"

set REFS=/reference:"%FW%\System.dll" /reference:"%FW%\System.Core.dll" /reference:"%FW%\System.Xml.dll" /reference:"%FW%\System.Drawing.dll" /reference:"%FW%\System.Windows.Forms.dll" /reference:"%FW%\System.IO.Compression.dll" /reference:"%FW%\System.IO.Compression.FileSystem.dll"

set "TOOL=%ROOT%\Astra手机版工具.exe"
set "LAUNCHER=%ROOT%\Astra创世神协议.exe"

if not exist "%LAUNCHER%" (
  echo [错误] 先打包启动器：缺 %LAUNCHER%
  pause
  exit /b 1
)

echo 编译手机版导出工具...
"%CSC%" /nologo /codepage:65001 /target:winexe /main:Zogos.MobileExportMain /win32icon:"%SRC%app.ico" /out:"%TOOL%" %REFS% /reference:"%LAUNCHER%" "%SRC%MobileExport.cs" "%SRC%MobileExportForm.cs"
if errorlevel 1 goto fail

echo.
echo 全部完成：%TOOL%
echo.
pause
exit /b 0

:fail
echo.
echo [错误] 编译失败。
pause
exit /b 1
