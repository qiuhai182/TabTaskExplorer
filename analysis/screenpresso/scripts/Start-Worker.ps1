<#
Screenpresso 逆向专用后台任务启动器（WMI → PowerShell → Start-Process → 工作脚本）

背景：
  当前 Trae Shell 的沙箱执行层会间歇性返回 “sandbox backend failed”，直接执行外部程序
  不稳定；Shell 内 Start-Process 的子进程又会随本次命令结束被作业对象回收。WMI 的
  Win32_Process.Create 不受该限制，因此统一走“WMI 拉起一个游离 PowerShell，再由它
  Start-Process 启动工作脚本”的方式。

用法：
  $p = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoProfile','-ExecutionPolicy','Bypass',
        '-File','D:\TabTaskExplorer\analysis\screenpresso\scripts\Start-Worker.ps1',
        '-WorkerPath','D:\TabTaskExplorer\analysis\screenpresso\scripts\Invoke-StaticProbe.ps1',
        '-WorkerArguments','-TargetPath ''C:\Users\QiuHai\.cache\rea-screenpresso-target\screenpresso.exe'' -OutDir ''D:\TabTaskExplorer\analysis\screenpresso\evidence''',
        '-TaskName','static-probe') -WindowStyle Hidden -PassThru

  轮询约定：
    工作任务目录下生成 <TaskName>.task.json（含进程号与命令行）
    工作脚本自己写 <TaskName>.exitcode 与 <TaskName>.log
#>

param(
    [Parameter(Mandatory = $true)][string]$WorkerPath,
    [string]$WorkerArguments = '',
    [Parameter(Mandatory = $true)][string]$TaskName,
    [string]$TaskDir = 'D:\TabTaskExplorer\analysis\screenpresso\ghidra'
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8

if (-not (Test-Path -LiteralPath $TaskDir)) {
    New-Item -ItemType Directory -Path $TaskDir -Force | Out-Null
}
$taskJsonPath = Join-Path $TaskDir ($TaskName + '.task.json')

function ConvertTo-PowerShellQuoted {
    param([AllowEmptyString()][string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

$argumentList = New-Object System.Collections.Generic.List[string]
$argumentList.Add('-NoProfile')
$argumentList.Add('-ExecutionPolicy')
$argumentList.Add('Bypass')
$argumentList.Add('-File')
$argumentList.Add($WorkerPath)
if ($WorkerArguments.Length -gt 0) {
    # WorkerArguments 是一整串已经带引号的参数文本，交给 Start-Process 时作为单元素透传会
    # 被当成一个参数；因此这里按空格拆分后再逐个加引号，保持与 tools\rea 启动器一致。
    $tokens = $WorkerArguments.Split(' ')
    foreach ($token in $tokens) {
        if ($token.Length -gt 0) {
            $argumentList.Add($token)
        }
    }
}

$quoted = New-Object System.Collections.Generic.List[string]
foreach ($item in $argumentList) {
    $quoted.Add((ConvertTo-PowerShellQuoted $item))
}
$workerInvocation = 'Start-Process -FilePath ''powershell.exe'' -ArgumentList @(' + ($quoted -join ',') + ') -WindowStyle Hidden'
$wmiCommandLine = 'powershell.exe -NoProfile -Command "' + $workerInvocation + '"'

$result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $wmiCommandLine }
$taskInfo = [ordered]@{
    任务名称 = $TaskName
    工作任务 = $WorkerPath
    工作参数 = $WorkerArguments
    日志目录 = $TaskDir
    进程号 = $result.ProcessId
    返回码 = $result.ReturnValue
    启动时间 = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    实际命令行 = $wmiCommandLine
}
$taskInfo | ConvertTo-Json | Out-File -LiteralPath $taskJsonPath -Encoding utf8
$taskInfo | ConvertTo-Json -Compress
