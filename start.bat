@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem TabTaskExplorer 启动脚本：启动 output 目录下的主程序
rem 已在运行时直接提示，不重复启动
rem ============================================================

set EXE=%~dp0output\TabTaskExplorer.exe

if not exist "%EXE%" (
    echo 未找到 %EXE%，请先运行 build.bat 编译。
    exit /b 1
)

tasklist /FI "IMAGENAME eq TabTaskExplorer.exe" 2>nul | find /I "TabTaskExplorer.exe" >nul
if not errorlevel 1 (
    echo TabTaskExplorer 已在运行，无需重复启动。
    exit /b 0
)

start "" "%EXE%"
echo TabTaskExplorer 已启动。
endlocal
