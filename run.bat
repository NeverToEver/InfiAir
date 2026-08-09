@echo off
REM InfiAir 双击启动（Windows）
REM 需要 Godot 4.6+ .NET 版（含 C# 工程，标准版无法打开；开发需 .NET 8 SDK）：https://godotengine.org/download
setlocal
cd /d "%~dp0"

REM 引擎探测：.NET 版优先（godot-mono）→ PATH（godot / godot4）→ 常见安装位置（mono 命名优先）
set "GODOT="
where /q godot-mono 2>nul && set "GODOT=godot-mono"
if not defined GODOT (
    where /q godot 2>nul && set "GODOT=godot"
)
if not defined GODOT (
    where /q godot4 2>nul && set "GODOT=godot4"
)
if not defined GODOT (
    if exist "%LOCALAPPDATA%\Godot\Godot.exe" set "GODOT=%LOCALAPPDATA%\Godot\Godot.exe"
)
if not defined GODOT (
    if exist "%USERPROFILE%\Godot\Godot.exe" set "GODOT=%USERPROFILE%\Godot\Godot.exe"
)
if not defined GODOT (
    for /f "delims=" %%F in ('dir /b /s "%USERPROFILE%\Downloads\Godot_v4*mono*.exe" 2^>nul') do (
        if not defined GODOT set "GODOT=%%F"
    )
)
if not defined GODOT (
    for /f "delims=" %%F in ('dir /b /s "%USERPROFILE%\Downloads\Godot_v4*.exe" 2^>nul') do (
        if not defined GODOT set "GODOT=%%F"
    )
)
if not defined GODOT (
    echo [InfiAir] 未找到 Godot 引擎（需要 4.6+ .NET 版——含 C# 工程，标准版无法打开；开发需 .NET 8 SDK）。
    echo           下载：https://godotengine.org/download（选 .NET 版本）
    echo           或将 godot-mono / godot / godot4 加入 PATH 环境变量后重试。
    pause
    exit /b 1
)

echo [InfiAir] 使用引擎：%GODOT%

REM R07：版本判定（L 系列工具链登记遗留）——探测版本 <4.6 仅警告继续（对齐 run.sh 口径）
for /f "tokens=1,2 delims=. " %%a in ('"%GODOT%" --version 2^>nul') do (
    if %%a geq 4 if %%b geq 6 set "GVER_OK=1"
)
if not defined GVER_OK (
    echo [InfiAir] 警告：检测到 Godot 版本低于 4.6（需要 4.6+ .NET 版），可能无法正常运行。
)

REM R07：保留真实退出码——原 `if errorlevel 1 pause` 使 pause 把退出码归零，脚本恒返回 0
"%GODOT%" --path . %*
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" pause
endlocal & exit /b %EXIT_CODE%
