@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem TabTaskExplorer 停止脚本
rem 应用关闭窗口时默认最小化到托盘，因此这里直接强制结束进程；
rem 结束后校验进程与输出文件是否仍被占用，避免"已停止"与事实不符
rem ============================================================

powershell -NoProfile -ExecutionPolicy Bypass -Command "$target = '%~dp0output\TabTaskExplorer.exe'; $procs = @(Get-Process -Name TabTaskExplorer -ErrorAction SilentlyContinue); $hadRunning = $procs.Count; if ($hadRunning -gt 0) { foreach ($x in $procs) { try { Stop-Process -Id $x.Id -Force -ErrorAction Stop } catch { } }; Start-Sleep -Milliseconds 800; $left = @(Get-Process -Name TabTaskExplorer -ErrorAction SilentlyContinue); if ($left.Count -gt 0) { Write-Host ('ERROR: TabTaskExplorer.exe 正在运行（进程号=' + (($left | ForEach-Object { $_.Id }) -join ',') + '），未能结束，请手动退出后再试。'); exit 1 } }; if (Test-Path $target) { try { $stream = [System.IO.File]::Open($target, 'Open', 'ReadWrite', 'None'); $stream.Close() } catch { Write-Host ('ERROR: TabTaskExplorer.exe 正在运行或被占用，请先退出。输出文件：' + $target); exit 1 } }; if ($hadRunning -gt 0) { Write-Host 'TabTaskExplorer 已停止。' } else { Write-Host 'TabTaskExplorer 未在运行。' }; exit 0"
set STOP_RESULT=%errorlevel%

if not "%STOP_RESULT%"=="0" (
    echo.
    echo ================== 停止失败 ==================
    echo 失败原因见上方提示：TabTaskExplorer.exe 正在运行，请先退出
    echo ==============================================
    endlocal
    exit /b 1
)

endlocal
exit /b 0
