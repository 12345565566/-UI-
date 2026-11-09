@echo off
chcp 65001 >nul
setlocal
rem rsp 里的资源路径是相对路径，csc 按当前目录解析 —— 必须先切到脚本所在目录
cd /d "%~dp0"
title 编译 ZOGOS 创世神启动器

set "SRC=%~dp0"
set "ROOT=%~dp0.."
set "OUT=%ROOT%\ZOGOS创世神启动器.exe"
set "CLIOUT=%ROOT%\管理员工具\zogos-cli.exe"

if not exist "%ROOT%\管理员工具" mkdir "%ROOT%\管理员工具"

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\System.dll" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "CSC=%FW%\csc.exe"
if not exist "%CSC%" (
  echo [错误] 没有找到 C# 编译器 csc.exe
  echo 需要 .NET Framework 4.x, Windows 10 / 11 一般自带.
  pause
  exit /b 1
)

set REFS=/reference:"%FW%\System.dll" /reference:"%FW%\System.Core.dll" /reference:"%FW%\System.Xml.dll" /reference:"%FW%\System.Drawing.dll" /reference:"%FW%\System.Windows.Forms.dll" /reference:"%FW%\System.IO.Compression.dll" /reference:"%FW%\System.IO.Compression.FileSystem.dll" /reference:"%FW%\System.Management.dll" /reference:"%FW%\System.Web.Extensions.dll"

echo 正在编译图形界面版（极简 UI）...
"%CSC%" /nologo /codepage:65001 /target:winexe /main:Zogos.Program /win32icon:"%SRC%app.ico" /resource:"%SRC%logo.png",Zogos.AppLogo.png /resource:"%SRC%launcher_logo.png",Zogos.LauncherLogo.png /resource:"%SRC%topicon.png",Zogos.TopIcon.png /resource:"%SRC%bg1.jpg",Zogos.BG1.jpg /resource:"%SRC%bg2.png",Zogos.BG2.png /resource:"%SRC%party_qr.png",Zogos.PartyQr.png /resource:"%SRC%susu1.jpg",Zogos.susu01.jpg /resource:"%SRC%susu2.jpg",Zogos.susu02.jpg /resource:"%SRC%susu3.jpg",Zogos.susu03.jpg /resource:"%SRC%susu4.jpg",Zogos.susu04.jpg /resource:"%SRC%susu5.jpg",Zogos.susu05.jpg /resource:"%SRC%susu6.jpg",Zogos.susu06.jpg /resource:"%SRC%susu7.jpg",Zogos.susu07.jpg /resource:"%SRC%susu8.jpg",Zogos.susu08.jpg /resource:"%SRC%susu9.jpg",Zogos.susu09.jpg /resource:"%SRC%susu10.jpg",Zogos.susu10.jpg /resource:"%SRC%susu11.jpg",Zogos.susu11.jpg /resource:"%SRC%susu12.jpg",Zogos.susu12.jpg /resource:"%SRC%susu13.jpg",Zogos.susu13.jpg /resource:"%SRC%susu14.jpg",Zogos.susu14.jpg /resource:"%SRC%susu15.jpg",Zogos.susu15.jpg /out:"%OUT%" %REFS% "%SRC%I18n.cs" "%SRC%Edition.cs" "%SRC%Log.cs" "%SRC%Core.cs" "%SRC%Core2.cs" "%SRC%Core3.cs" "%SRC%Core4.cs" "%SRC%Core6.cs" "%SRC%ModAnalyzer.cs" "%SRC%CyberTheme.cs" "%SRC%ExitCodes.cs" "%SRC%Core5.cs" "%SRC%Widgets.cs" "%SRC%CyberHub.cs" "%SRC%GuideData.cs" "%SRC%SusuWindow.cs" "%SRC%SusuForms.cs" "%SRC%SusuDiag.cs" "%SRC%AiClient.cs" "%SRC%AiSetupForm.cs" "%SRC%LoadingOverlay.cs" "%SRC%ModNameZh.cs" "%SRC%PackFeed.cs" "%SRC%PackFeedForm.cs" "%SRC%PackSubmit.cs" "%SRC%WorkshopNet.cs" "%SRC%WorkshopSync.cs" "%SRC%WorkshopClient.cs" "%SRC%WorkshopForm.cs" "%SRC%WorkshopSyncForm.cs" "%SRC%ModGraph.cs" "%SRC%ModClassifier.cs" "%SRC%ModGraphControl.cs" "%SRC%ModGraphForm.cs" "%SRC%ModDetailForm.cs" "%SRC%MinDialog.cs" "%SRC%SusuQrForm.cs" "%SRC%SusuVoice.cs" "%SRC%PackRegistry.cs" "%SRC%PackSwitchForm.cs" "%SRC%MinTheme.cs" "%SRC%MinControls.cs" "%SRC%MinFrameFx.cs" "%SRC%MinSort.cs" "%SRC%MinSettings.cs" "%SRC%MinShare.cs" "%SRC%MinWorkshop.cs" "%SRC%MinFeed.cs" "%SRC%MinGraph.cs" "%SRC%MinSusu.cs" "%SRC%MainForm.cs" "%SRC%Program.cs" @"%SRC%voice.resources.rsp"
if errorlevel 1 goto fail

echo 正在编译命令行版...
"%CSC%" /nologo /codepage:65001 /target:exe /main:Zogos.CliMain /out:"%CLIOUT%" %REFS% "%SRC%Edition.cs" "%SRC%Log.cs" "%SRC%Core.cs" "%SRC%Core2.cs" "%SRC%Core3.cs" "%SRC%Core4.cs" "%SRC%Core5.cs" "%SRC%Core6.cs" "%SRC%ExitCodes.cs" "%SRC%Cli.cs"
if errorlevel 1 goto fail

echo 正在编译图标生成工具（可选）...
"%CSC%" /nologo /codepage:65001 /target:exe /out:"%SRC%makeicon.exe" /reference:"%FW%\System.dll" /reference:"%FW%\System.Drawing.dll" "%SRC%MakeIcon.cs"
if not errorlevel 1 (
  echo   用法: makeicon.exe 源图.png 输出.ico
)

echo.
echo 编译完成:
echo   %OUT%
echo   %CLIOUT%
echo.
pause
exit /b 0

:fail
echo.
echo [错误] 编译失败, 请看上面的提示.
pause
exit /b 1