@echo off
chcp 65001 >nul
setlocal
rem rsp 资源路径是相对路径，csc 按当前目录解析，必须先切到脚本所在目录
cd /d "%~dp0"
title 打包 Astra（两版启动器 + 命令行 + 两版安装包）

set "SRC=%~dp0"
set "ROOT=%~dp0.."
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\System.dll" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "CSC=%FW%\csc.exe"

set REFS=/reference:"%FW%\System.dll" /reference:"%FW%\System.Core.dll" /reference:"%FW%\System.Xml.dll" /reference:"%FW%\System.Drawing.dll" /reference:"%FW%\System.Windows.Forms.dll" /reference:"%FW%\System.IO.Compression.dll" /reference:"%FW%\System.IO.Compression.FileSystem.dll" /reference:"%FW%\System.Management.dll" /reference:"%FW%\System.Web.Extensions.dll"

set COMMON="%SRC%I18n.cs" "%SRC%Edition.cs" "%SRC%Log.cs" "%SRC%Core.cs" "%SRC%Core2.cs" "%SRC%Core3.cs" "%SRC%Core4.cs" "%SRC%Core6.cs" "%SRC%ModAnalyzer.cs" "%SRC%CyberTheme.cs" "%SRC%ExitCodes.cs"
set GUI_FILES=%COMMON% "%SRC%Core5.cs" "%SRC%Widgets.cs" "%SRC%CyberHub.cs" "%SRC%GuideData.cs" "%SRC%SusuWindow.cs" "%SRC%SusuForms.cs" "%SRC%SusuDiag.cs" "%SRC%AiClient.cs" "%SRC%AiSetupForm.cs" "%SRC%LoadingOverlay.cs" "%SRC%ModNameZh.cs" "%SRC%PackFeed.cs" "%SRC%PackFeedForm.cs" "%SRC%PackSubmit.cs" "%SRC%WorkshopNet.cs" "%SRC%WorkshopSync.cs" "%SRC%WorkshopClient.cs" "%SRC%WorkshopForm.cs" "%SRC%WorkshopSyncForm.cs" "%SRC%ModGraph.cs" "%SRC%ModClassifier.cs" "%SRC%ModGraphControl.cs" "%SRC%ModGraphForm.cs" "%SRC%ModDetailForm.cs" "%SRC%MinDialog.cs" "%SRC%SusuQrForm.cs" "%SRC%SusuVoice.cs" "%SRC%PackRegistry.cs" "%SRC%PackSwitchForm.cs" "%SRC%MinTheme.cs" "%SRC%MinControls.cs" "%SRC%MinFrameFx.cs" "%SRC%MinSort.cs" "%SRC%MinSettings.cs" "%SRC%MinShare.cs" "%SRC%MinWorkshop.cs" "%SRC%MinFeed.cs" "%SRC%MinGraph.cs" "%SRC%MinSusu.cs" "%SRC%MainForm.cs" "%SRC%Program.cs"
set CLI_FILES=%COMMON% "%SRC%Core5.cs" "%SRC%PackRegistry.cs" "%SRC%Cli.cs"
set SETUP_FILES=%COMMON% "%SRC%Widgets.cs" "%SRC%SetupIntro.cs" "%SRC%Setup.cs"

set RES=/resource:"%SRC%logo.png",Zogos.AppLogo.png /resource:"%SRC%launcher_logo.png",Zogos.LauncherLogo.png /resource:"%SRC%topicon.png",Zogos.TopIcon.png /resource:"%SRC%bg1.jpg",Zogos.BG1.jpg /resource:"%SRC%bg2.png",Zogos.BG2.png /resource:"%SRC%party_qr.png",Zogos.PartyQr.png /resource:"%SRC%susu1.jpg",Zogos.susu01.jpg /resource:"%SRC%susu2.jpg",Zogos.susu02.jpg /resource:"%SRC%susu3.jpg",Zogos.susu03.jpg /resource:"%SRC%susu4.jpg",Zogos.susu04.jpg /resource:"%SRC%susu5.jpg",Zogos.susu05.jpg /resource:"%SRC%susu6.jpg",Zogos.susu06.jpg /resource:"%SRC%susu7.jpg",Zogos.susu07.jpg /resource:"%SRC%susu8.jpg",Zogos.susu08.jpg /resource:"%SRC%susu9.jpg",Zogos.susu09.jpg /resource:"%SRC%susu10.jpg",Zogos.susu10.jpg /resource:"%SRC%susu11.jpg",Zogos.susu11.jpg /resource:"%SRC%susu12.jpg",Zogos.susu12.jpg /resource:"%SRC%susu13.jpg",Zogos.susu13.jpg /resource:"%SRC%susu14.jpg",Zogos.susu14.jpg /resource:"%SRC%susu15.jpg",Zogos.susu15.jpg

set NORMAL_EXE=%ROOT%\Astra创世神协议.exe
set SPONSOR_EXE=%ROOT%\Astra创世神协议_赞助版.exe
set NORMAL_SETUP=%ROOT%\Astra创世神协议_安装包_普通版.exe
set SPONSOR_SETUP=%ROOT%\Astra创世神协议_安装包_赞助版.exe
set CLI_OUT=%ROOT%\管理员工具\zogos-cli.exe
set README=%ROOT%\使用说明.txt

echo [1/5] 普通版启动器...
"%CSC%" /nologo /codepage:65001 /target:winexe /main:Zogos.Program /win32icon:"%SRC%app.ico" %RES% /out:"%NORMAL_EXE%" %REFS% %GUI_FILES% @"%SRC%voice.resources.rsp"
if errorlevel 1 goto fail

echo [2/5] 赞助版启动器（含外观自定义）...
"%CSC%" /nologo /codepage:65001 /define:SPONSOR /target:winexe /main:Zogos.Program /win32icon:"%SRC%app.ico" %RES% /out:"%SPONSOR_EXE%" %REFS% %GUI_FILES% @"%SRC%voice.resources.rsp"
if errorlevel 1 goto fail

echo [3/5] 命令行工具...
"%CSC%" /nologo /codepage:65001 /target:exe /main:Zogos.CliMain /out:"%CLI_OUT%" %REFS% %CLI_FILES%
if errorlevel 1 goto fail

echo [4/5] 普通版安装包...
"%CSC%" /nologo /codepage:65001 /target:winexe /main:Zogos.SetupMain /win32icon:"%SRC%app.ico" /resource:"%NORMAL_EXE%",Zogos.Setup.Payload.exe /resource:"%README%",Zogos.Setup.Readme.txt /resource:"%CLI_OUT%",Zogos.Setup.Cli.exe /resource:"%SRC%tools\ffmpeg.exe",Zogos.Setup.FFmpeg.exe /out:"%NORMAL_SETUP%" %REFS% %SETUP_FILES%
if errorlevel 1 goto fail

echo [5/5] 赞助版安装包...
"%CSC%" /nologo /codepage:65001 /define:SPONSOR /target:winexe /main:Zogos.SetupMain /win32icon:"%SRC%app.ico" /resource:"%SPONSOR_EXE%",Zogos.Setup.Payload.exe /resource:"%README%",Zogos.Setup.Readme.txt /resource:"%CLI_OUT%",Zogos.Setup.Cli.exe /resource:"%SRC%tools\ffmpeg.exe",Zogos.Setup.FFmpeg.exe /out:"%SPONSOR_SETUP%" %REFS% %SETUP_FILES%
if errorlevel 1 goto fail

echo.
echo 全部完成：
echo   普通版启动器 : %NORMAL_EXE%
echo   赞助版启动器 : %SPONSOR_EXE%
echo   命令行工具   : %CLI_OUT%
echo   普通版安装包 : %NORMAL_SETUP%
echo   赞助版安装包 : %SPONSOR_SETUP%
echo.
pause
exit /b 0

:fail
echo.
echo [错误] 打包失败，请看上面的提示。
pause
exit /b 1
