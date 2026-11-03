<#
Screenpresso 逆向一键取证入口（后台启动 + 轮询日志）

用法（在 Trae Shell 中）：
  -Foreground 参数不使用；本脚本通过 WMI 启动游离进程，避免 Shell 作业对象回收。

推荐调用：
  powershell -NoProfile -ExecutionPolicy Bypass -File Invoke-All.ps1 -Mode probe
  powershell -NoProfile -ExecutionPolicy Bypass -File Invoke-All.ps1 -Mode rea-analyze
  powershell -NoProfile -ExecutionPolicy Bypass -File Invoke-All.ps1 -Mode rea-search -Pattern 'ScreenpressoCapture'

轮询：
  analysis\screenpresso\evidence\static-probe.exitcode
  analysis\screenpresso\ghidra\rea-*.log 与同名 .exitcode
#>

param(
    [ValidateSet('probe', 'rea-analyze', 'rea-search', 'rea-doctor')]
    [string]$Mode = 'probe',
    [string]$Pattern = '',
    [string]$TargetPath = 'C:\Users\QiuHai\.cache\rea-screenpresso-target\screenpresso.exe'
)

$ErrorActionPreference = 'Stop'
$scriptRoot = 'D:\TabTaskExplorer\analysis\screenpresso\scripts'
$starter = Join-Path $scriptRoot 'Start-Worker.ps1'
$probeWorker = Join-Path $scriptRoot 'Invoke-StaticProbe.ps1'
$reaWorker = Join-Path $scriptRoot 'Run-Rea-Screenpresso.ps1'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

function Start-ViaWmi {
    param([string]$WorkerPath, [string]$WorkerArguments, [string]$TaskName, [string]$TaskDir)
    $quotedWorker = "'" + $WorkerPath.Replace("'", "''") + "'"
    $quotedArgs = "'" + $WorkerArguments.Replace("'", "''") + "'"
    $quotedName = "'" + $TaskName.Replace("'", "''") + "'"
    $quotedDir = "'" + ($TaskDir.Replace("'", "''")) + "'"
    $commandLine = 'powershell.exe -NoProfile -Command "Start-Process -FilePath ''powershell.exe'' -ArgumentList @(''
        -NoProfile'','' -ExecutionPolicy'','' Bypass'', ''-File'', ' + $quotedWorker + ', ''-WorkerArguments'', ' + $quotedArgs + ', ''-TaskName'', ' + $quotedName + ', ''-TaskDir'', ' + $quotedDir + ') -WindowStyle Hidden"'
    $result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $commandLine }
    return $result
}

if ($Mode -eq 'probe') {
    $taskDir = 'D:\TabTaskExplorer\analysis\screenpresso\evidence'
    $arguments = "-TargetPath '$TargetPath' -OutDir '$taskDir'"
    # 直接使用 Start-Worker，保证参数拼接逻辑只有一份
    & 'powershell.exe' -NoProfile -ExecutionPolicy Bypass -File $starter -WorkerPath $probeWorker -WorkerArguments $arguments -TaskName ('static-probe-' + $stamp) -TaskDir $taskDir
    exit 0
}

if ($Mode -eq 'rea-analyze') {
    $taskDir = 'D:\TabTaskExplorer\analysis\screenpresso\ghidra'
    & 'powershell.exe' -NoProfile -ExecutionPolicy Bypass -File $starter -WorkerPath $reaWorker -WorkerArguments '-Command analyze -Profile default' -TaskName ('rea-analyze-' + $stamp) -TaskDir $taskDir
    exit 0
}

if ($Mode -eq 'rea-search') {
    if ($Pattern.Length -eq 0) { throw 'rea-search 模式必须提供 Pattern 参数' }
    $taskDir = 'D:\TabTaskExplorer\analysis\screenpresso\ghidra'
    & 'powershell.exe' -NoProfile -ExecutionPolicy Bypass -File $starter -WorkerPath $reaWorker -WorkerArguments ('-Command search -Pattern ''' + $Pattern + ''' -Mode regex') -TaskName ('rea-search-' + $stamp) -TaskDir $taskDir
    exit 0
}

if ($Mode -eq 'rea-doctor') {
    $taskDir = 'D:\TabTaskExplorer\analysis\screenpresso\ghidra'
    & 'powershell.exe' -NoProfile -ExecutionPolicy Bypass -File $starter -WorkerPath $reaWorker -WorkerArguments '-Command doctor' -TaskName ('rea-doctor-' + $stamp) -TaskDir $taskDir
    exit 0
}
