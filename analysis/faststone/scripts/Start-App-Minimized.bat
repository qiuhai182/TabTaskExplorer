@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'D:\TabTaskExplorer\output\TabTaskExplorer.exe' -WindowStyle Minimized; '已提交启动，时间=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | Out-File -LiteralPath 'D:\TabTaskExplorer\output\app-start.txt' -Encoding utf8"
exit /b 0
