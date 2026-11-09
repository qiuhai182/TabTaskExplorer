<#
项目构建入口（可复用的独立脚本）

用法（在 Trae Shell 中用 Start-Process 启动，脚本内部直接调用 dotnet）：
  $runner = 'D:\TabTaskExplorer\tools\rea\Run-Build.ps1'
  $p = Start-Process -FilePath 'powershell.exe' `
      -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',$runner) `
      -WindowStyle Hidden -PassThru
  # 日志形如 output\build-20261103-HHmmss.txt，退出码写在同名 .exitcode 文件
#>

param(
    [string]$SolutionPath = 'D:\TabTaskExplorer\TabTaskExplorer.sln',
    [string]$Verbosity = 'm',
    [string]$LogPath = ''
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Set-Location -LiteralPath 'D:\TabTaskExplorer'

if ($LogPath.Length -eq 0) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $LogPath = 'D:\TabTaskExplorer\output\build-' + $stamp + '.txt'
}
$exitCodePath = $LogPath + '.exitcode'
Remove-Item -LiteralPath $exitCodePath -Force -ErrorAction SilentlyContinue

$dotnetCommand = Get-Command -Name 'dotnet' -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    $dotnet = 'C:\Program Files\dotnet\dotnet.exe'
} else {
    $dotnet = $dotnetCommand.Source
}

'开始时间=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | Out-File -LiteralPath $LogPath -Encoding utf8
'命令=' + $dotnet + ' build ' + $SolutionPath + ' -v ' + $Verbosity | Out-File -LiteralPath $LogPath -Encoding utf8 -Append

& $dotnet build $SolutionPath -v $Verbosity 2>&1 | Out-File -LiteralPath $LogPath -Encoding utf8 -Append
$exitCode = $LASTEXITCODE
if ($null -eq $exitCode) { $exitCode = 0 }
'结束时间=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | Out-File -LiteralPath $LogPath -Encoding utf8 -Append
'exit=' + $exitCode | Out-File -LiteralPath $LogPath -Encoding utf8 -Append
Set-Content -LiteralPath $exitCodePath -Value $exitCode -Encoding utf8
exit $exitCode
