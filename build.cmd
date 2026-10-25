@echo off
rem Builds Harbor.exe with the C# compiler that ships with Windows (.NET Framework 4.8). No installs needed.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist bin mkdir bin
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /debug- /codepage:65001 /out:bin\Harbor.exe /win32manifest:app.manifest /win32icon:assets\Harbor.ico ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Runtime.dll /r:System.Runtime.InteropServices.WindowsRuntime.dll ^
  /r:%WINDIR%\System32\WinMetadata\Windows.Foundation.winmd /r:%WINDIR%\System32\WinMetadata\Windows.Media.winmd /r:%WINDIR%\System32\WinMetadata\Windows.Devices.winmd /r:%WINDIR%\System32\WinMetadata\Windows.Storage.winmd ^
  /r:%WINDIR%\System32\WinMetadata\Windows.UI.winmd /r:%WINDIR%\System32\WinMetadata\Windows.ApplicationModel.winmd /r:%WINDIR%\System32\WinMetadata\Windows.Data.winmd src\*.cs
if errorlevel 1 exit /b 1
copy /y App.config bin\Harbor.exe.config >nul
echo Built bin\Harbor.exe
