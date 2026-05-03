@echo off
set GAMEDIR=LazyPainter
set GAMEDATA=GameData
set RELEASEDIR=.\Builds
set ZIP="c:\Program Files\7-zip\7z.exe"
set VERSIONFILE=%GAMEDATA%\%GAMEDIR%\%GAMEDIR%.version

rem Read version from .version file using PowerShell
for /f "delims=" %%i in ('powershell -Command "(Get-Content '%VERSIONFILE%' | ConvertFrom-Json).VERSION"') do set VERSION=%%i

echo Version: %VERSION%

rem Build the zip file
if not exist %RELEASEDIR% mkdir %RELEASEDIR%
set FILE=%RELEASEDIR%\%GAMEDIR%-%VERSION%.zip
if exist %FILE% del /F %FILE%
%ZIP% a -tzip %FILE% %GAMEDATA%

pause