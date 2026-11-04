@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "D:\TabTaskExplorer\analysis\faststone\scripts\pending-command.ps1"
exit /b %errorlevel%
