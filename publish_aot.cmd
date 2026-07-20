@echo off
setlocal

rem The ILCompiler targets invoke a bare vswhere.exe to locate the C++ linker,
rem so the VS Installer directory must be on PATH.
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"

dotnet publish "%~dp0CentrED\CentrED.csproj" -r win-x64 -c Release -p:PublishAot=true %*

endlocal
exit /b %ERRORLEVEL%
