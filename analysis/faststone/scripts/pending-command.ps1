[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
Set-Location -LiteralPath 'D:\TabTaskExplorer'

# 先释放 output 目录占用：托盘常驻实例会锁住 TabTaskExplorer.exe
Get-Process -Name 'TabTaskExplorer' -ErrorAction SilentlyContinue | ForEach-Object {
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 800

$logPath = 'D:\TabTaskExplorer\output\build-capture-clipboard.log'
'开始时间=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | Out-File -LiteralPath $logPath -Encoding utf8
'命令=dotnet publish 配置=Release 运行时=win-x64 单文件=是 输出目录=D:\TabTaskExplorer\output' | Out-File -LiteralPath $logPath -Encoding utf8 -Append

& 'C:\Program Files\dotnet\dotnet.exe' publish 'D:\TabTaskExplorer\src\TabTaskExplorer.App\TabTaskExplorer.App.csproj' `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=none `
    -o 'D:\TabTaskExplorer\output' 2>&1 | Out-File -LiteralPath $logPath -Encoding utf8 -Append

$exitCode = $LASTEXITCODE
if ($null -eq $exitCode) { $exitCode = 0 }
'结束时间=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | Out-File -LiteralPath $logPath -Encoding utf8 -Append
'exit=' + $exitCode | Out-File -LiteralPath $logPath -Encoding utf8 -Append
exit $exitCode
