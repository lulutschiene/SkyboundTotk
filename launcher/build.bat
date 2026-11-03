@echo off
rem Builds launcher\Skybound.exe with the C# compiler that ships with Windows (.NET Framework 4); build_game.py
rem copies it next to the versions\ folder of dist\Skybound.
setlocal
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /out:Skybound.exe ^
  /win32icon:..\branding\skybound.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll ^
  /r:Microsoft.VisualBasic.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll *.cs
exit /b %ERRORLEVEL%
