@echo off
rem InfiAir Windows uninstaller
rem Usage: uninstall.bat [/purge]
rem   /purge  also delete save data and settings (kept by default)
setlocal

set "APP_DIR=%LOCALAPPDATA%\InfiAir"
set "START_MENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs"
set "USER_DATA=%APPDATA%\Godot\app_userdata\InfiAir"

set "PURGE=0"
if /i "%~1"=="/purge" set "PURGE=1"
if not "%~1"=="" if /i not "%~1"=="/purge" (
    echo Unknown argument: %~1 (only /purge is supported)
    exit /b 2
)

if exist "%APP_DIR%" rmdir /s /q "%APP_DIR%"
if exist "%START_MENU%\InfiAir.lnk" del /f /q "%START_MENU%\InfiAir.lnk"

echo InfiAir uninstalled.
if "%PURGE%"=="1" (
    if exist "%USER_DATA%" rmdir /s /q "%USER_DATA%"
    echo Save data and settings deleted: %USER_DATA%
) else (
    echo Save data and settings kept at %USER_DATA%
    echo ^(run "uninstall.bat /purge" to delete them^)
)
endlocal
