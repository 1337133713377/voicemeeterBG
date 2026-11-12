@echo off
setlocal
rem Compile VoicemeeterBG.exe avec le compilateur C# livre avec Windows.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Compilateur C# introuvable : .NET Framework 4 est requis.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /unsafe /codepage:65001 /platform:anycpu /out:"%~dp0VoicemeeterBG.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0src\Program.cs"
if errorlevel 1 exit /b 1
echo OK : VoicemeeterBG.exe
