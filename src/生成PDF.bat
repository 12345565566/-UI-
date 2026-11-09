@echo off
chcp 65001 >nul
setlocal
title 生成报错解决方案 PDF
set "SRC=%~dp0"
set "OUT=%~dp0..\ZOGOS整合包报错解决方案.pdf"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\System.dll" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
echo 正在生成 PDF...
"%FW%\csc.exe" /nologo /codepage:65001 /target:exe /main:Zogos.PdfMain /out:"%TEMP%\makepdf.exe" /reference:"%FW%\System.dll" /reference:"%FW%\System.Drawing.dll" "%SRC%PdfDoc.cs" "%SRC%MakePdf.cs"
if errorlevel 1 goto fail
"%TEMP%\makepdf.exe" "%OUT%"
echo.
echo 完成: %OUT%
pause
exit /b 0
:fail
echo [错误] 生成失败
pause
exit /b 1