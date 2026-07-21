@echo off
REM InfiAir 双击启动（Windows）
REM 需要 Godot 4.6+（标准版，无需 .NET）：https://godotengine.org/download
setlocal
cd /d "%~dp0"

REM 引擎探测：PATH → 常见安装位置
set "GODOT="
where /q godot 2>nul && set "GODOT=godot"
if not defined GODOT (
    if exist "%LOCALAPPDATA%\Godot\Godot.exe" set "GODOT=%LOCALAPPDATA%\Godot\Godot.exe"
)
if not defined GODOT (
    if exist "%USERPROFILE%\Godot\Godot.exe" set "GODOT=%USERPROFILE%\Godot\Godot.exe"
)
if not defined GODOT (
    for /f "delims=" %%F in ('dir /b /s "%USERPROFILE%\Downloads\Godot_v4*.exe" 2^>nul') do (
        if not defined GODOT set "GODOT=%%F"
    )
)
if not defined GODOT (
    echo [InfiAir] 未找到 Godot 引擎（需要 4.6+，标准版即可）。
    echo           下载：https://godotengine.org/download
    echo           或将 godot 加入 PATH 环境变量后重试。
    pause
    exit /b 1
)

echo [InfiAir] 使用引擎：%GODOT%
"%GODOT%" --path . %*
if errorlevel 1 pause
endlocal
