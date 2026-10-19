@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem TabTaskExplorer 重启脚本：先停止再启动
rem ============================================================

call "%~dp0stop.bat"
timeout /t 1 /nobreak >nul
call "%~dp0start.bat"
endlocal
