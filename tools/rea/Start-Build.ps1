<#
项目构建启动器（WMI → PowerShell → Start-Process → Run-Build.ps1）

用法：
  $starter = 'D:\TabTaskExplorer\tools\rea\Start-Build.ps1'
  $p = Start-Process -FilePath 'powershell.exe' `
      -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',$starter,
                      '-LogPath','D:\TabTaskExplorer\output\build-latest.txt') `
      -Wait -PassThru -WindowStyle Hidden
  # 构建日志与 .exitcode 由工作脚本写入
#>

param(
    [string]$SolutionPath = 'D:\TabTaskExplorer\TabTaskExplorer.sln',
    [string]$Verbosity = 'm',
    [string]$LogPath = ''
)

$ErrorActionPreference = 'Continue'
Set-Location -LiteralPath 'D:\TabTaskExplorer'
$workerPath = 'D:\TabTaskExplorer\tools\rea\Run-Build.ps1'
if ($LogPath.Length -eq 0) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $LogPath = 'D:\TabTaskExplorer\output\build-' + $stamp + '.txt'
}
Remove-Item -LiteralPath ($LogPath + '.exitcode') -Force -ErrorAction SilentlyContinue

function ConvertTo-PowerShellQuoted {
    param([AllowEmptyString()][string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

$workerArguments = New-Object System.Collections.Generic.List[string]
$workerArguments.Add('-NoProfile')
$workerArguments.Add('-ExecutionPolicy')
$workerArguments.Add('Bypass')
$workerArguments.Add('-File')
$workerArguments.Add($workerPath)
$workerArguments.Add('-SolutionPath')
$workerArguments.Add($SolutionPath)
$workerArguments.Add('-Verbosity')
$workerArguments.Add($Verbosity)
$workerArguments.Add('-LogPath')
$workerArguments.Add($LogPath)

$quotedArguments = New-Object System.Collections.Generic.List[string]
foreach ($argument in $workerArguments) {
    $quotedArguments.Add((ConvertTo-PowerShellQuoted $argument))
}
$workerInvocation = 'Start-Process -FilePath ''powershell.exe'' -ArgumentList @(' + ($quotedArguments -join ',') + ') -WindowStyle Hidden'
$wmiCommandLine = 'powershell.exe -NoProfile -Command "' + $workerInvocation + '"'

$result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $wmiCommandLine }
$taskInfo = [ordered]@{
    LogPath = $LogPath
    ProcessId = $result.ProcessId
    ReturnValue = $result.ReturnValue
    SubmittedAt = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
}
$taskInfo | ConvertTo-Json | Out-File -LiteralPath ($LogPath + '.task.json') -Encoding utf8
$taskInfo | ConvertTo-Json -Compress
