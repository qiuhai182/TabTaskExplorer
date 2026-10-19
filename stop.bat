@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem TabTaskExplorer 停止脚本
rem 应用关闭窗口时默认最小化到托盘，因此这里直接强制结束进程
rem ============================================================

taskkill /IM TabTaskExplorer.exe /F >nul 2>&1
if errorlevel 1 (
    echo TabTaskExplorer 未在运行。
    exit /b 0
)

echo TabTaskExplorer 已停止。
endlocal
