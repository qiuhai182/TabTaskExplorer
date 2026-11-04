@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$log='D:\TabTaskExplorer\output\build-capture-platform.log'; '开始时间=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | Out-File -LiteralPath $log -Encoding utf8; & 'C:\Program Files\dotnet\dotnet.exe' build 'D:\TabTaskExplorer\src\TabTaskExplorer.Platform.Windows\TabTaskExplorer.Platform.Windows.csproj' -c Release -v m 2>&1 | Out-File -LiteralPath $log -Encoding utf8 -Append; $code=$LASTEXITCODE; 'exit=' + $code | Out-File -LiteralPath $log -Encoding utf8 -Append"
exit /b %errorlevel%
