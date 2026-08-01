@echo off
rem InfiAir Windows installer (per-user, no admin required)
rem Usage: install.bat
setlocal

set "APP_DIR=%LOCALAPPDATA%\InfiAir"
set "START_MENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs"

if not exist "%~dp0InfiAir.exe" (
    echo Error: InfiAir.exe not found next to this script.
    exit /b 1
)

if not exist "%APP_DIR%" mkdir "%APP_DIR%"
copy /y "%~dp0InfiAir.exe" "%APP_DIR%\InfiAir.exe" >nul
if errorlevel 1 (
    echo Error: failed to copy InfiAir.exe. Is the game currently running?
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $sc = $ws.CreateShortcut('%START_MENU%\InfiAir.lnk'); $sc.TargetPath = '%APP_DIR%\InfiAir.exe'; $sc.WorkingDirectory = '%APP_DIR%'; $sc.Description = 'InfiAir - 2D top-down arcade air combat'; $sc.Save()"
if errorlevel 1 (
    echo Warning: failed to create Start Menu shortcut. The game itself is installed.
)

echo.
echo InfiAir installed:
echo   Program     %APP_DIR%\InfiAir.exe
echo   Start Menu  %START_MENU%\InfiAir.lnk
echo To uninstall, run uninstall.bat from this package.
endlocal
