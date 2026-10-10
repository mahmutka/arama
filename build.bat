@echo off
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist bin mkdir bin
set COMMON=/nologo /codepage:65001 /platform:x64 /optimize+ /warn:4 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll /r:System.Xml.Linq.dll
"%CSC%" %COMMON% /target:winexe /out:bin\Arama.exe /win32manifest:src\app.manifest /recurse:src\*.cs
if errorlevel 1 exit /b 1
"%CSC%" %COMMON% /target:exe /out:bin\Tests.exe /main:Arama.Tests.TestRunner /recurse:src\*.cs /recurse:tests\*.cs
if errorlevel 1 exit /b 1
echo Derleme basarili.
