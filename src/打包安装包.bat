@echo off
chcp 65001 >nul
setlocal
title 打包 ZOGOS 创世神启动器安装包

set "SRC=%~dp0"
set "ROOT=%~dp0.."
set "OUT=%ROOT%\ZOGOS创世神启动器_安装包.exe"
set "PAYLOAD=%ROOT%\ZOGOS创世神启动器.exe"
set "README=%ROOT%\使用说明.txt"
set "CLI=%ROOT%\管理员工具\zogos-cli.exe"

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\System.dll" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "CSC=%FW%\csc.exe"

if not exist "%PAYLOAD%" (
  echo [错误] 没找到 %PAYLOAD%
  echo 请先运行 源码\编译.bat 编译出启动器，再来打包安装包。
  pause
  exit /b 1
)

set REFS=/reference:"%FW%\System.dll" /reference:"%FW%\System.Core.dll" /reference:"%FW%\System.Xml.dll" /reference:"%FW%\System.Drawing.dll" /reference:"%FW%\System.Windows.Forms.dll" /reference:"%FW%\System.IO.Compression.dll" /reference:"%FW%\System.IO.Compression.FileSystem.dll"

echo 正在打包安装包...
"%CSC%" /nologo /codepage:65001 /target:winexe /main:Zogos.SetupMain /win32icon:"%SRC%app.ico" /resource:"%PAYLOAD%",Zogos.Setup.Payload.exe /resource:"%README%",Zogos.Setup.Readme.txt /resource:"%CLI%",Zogos.Setup.Cli.exe /out:"%OUT%" %REFS% "%SRC%Edition.cs" "%SRC%Log.cs" "%SRC%Core.cs" "%SRC%Core2.cs" "%SRC%Core3.cs" "%SRC%Core4.cs" "%SRC%Core6.cs" "%SRC%ExitCodes.cs" "%SRC%Widgets.cs" "%SRC%SetupIntro.cs" "%SRC%Setup.cs"
if errorlevel 1 goto fail

echo.
echo 打包完成:
echo   %OUT%
echo.
echo 小技巧：把安装包放进整合包文件夹再运行，它会自动把 RimSort 也一起装好。
echo.
pause
exit /b 0

:fail
echo.
echo [错误] 打包失败, 请看上面的提示.
pause
exit /b 1