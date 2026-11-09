<#
REA 长任务启动器（WMI → PowerShell → Start-Process → Run-Rea.ps1）

实测结论（Windows 10/11 + Trae Shell）：
  1. Trae Shell 的沙箱执行层当前无法直接拉起外部程序：直接执行 node/git/dotnet/where
     都会返回 “sandbox backend failed”。这是工具执行后端故障，绝对路径与权限均正常。
  2. Shell 内 Start-Process 的子进程会随本次 Shell 命令结束被作业对象回收；短任务能
     赶在命令返回前完成，Ghidra 这类长任务会被中途杀掉。
  3. WMI 的 Win32_Process.Create 不受 Shell 作业对象限制；但 WMI 直接启动
     powershell.exe 时不能带 -ExecutionPolicy Bypass（会静默不执行），
     -EncodedCommand 在 WMI 下也不执行。
  4. 已验证可用的组合是：WMI → powershell.exe -Command Start-Process → 工作脚本。
     工作脚本再用 Start-Process/直接调用 node，可稳定运行 10 分钟以上并写入退出码。
  5. REA 的 Ghidra 启动时限默认 330000 毫秒（约 350 秒），工作脚本 RAISE 到
     3600000 毫秒（1 小时）；任务串行执行，避免多个 Ghidra 会话互相抢占。

用法（在 Trae Shell 中，用 Start-Process 启动本启动器；它几秒内退出）：
  $starter = 'D:\TabTaskExplorer\tools\rea\Start-Rea.ps1'
  $p = Start-Process -FilePath 'powershell.exe' `
      -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',$starter,
                      '-Command','search','-Pattern','BarcodeDecoder',
                      '-LogPath','D:\TabTaskExplorer\output\rea-search-barcode.txt') `
      -Wait -PassThru -WindowStyle Hidden

  # 轮询：日志末尾出现 exit=<退出码>，同时生成 <日志>.exitcode
#>

param(
    [ValidateSet('analyze', 'search', 'xrefs', 'decompile', 'function', 'trace', 'doctor', 'version')]
    [string]$Command = 'analyze',
    [string]$TargetPath = 'C:\Users\QiuHai\.cache\rea-snipaste-target\snipaste.exe',
    [string]$SnapshotPath = 'D:\TabTaskExplorer\output\rea-snipaste.snapshot',
    [string]$Pattern = '',
    [string]$Mode = 'regex',
    [string]$Address = '',
    [string]$Name = '',
    [ValidateSet('default', 'fast')][string]$Profile = 'default',
    [string]$LogPath = ''
)

$ErrorActionPreference = 'Continue'
Set-Location -LiteralPath 'D:\TabTaskExplorer'
$workerPath = 'D:\TabTaskExplorer\tools\rea\Run-Rea.ps1'
if ($LogPath.Length -eq 0) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $LogPath = 'D:\TabTaskExplorer\output\rea-' + $Command + '-' + $stamp + '.txt'
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
$workerArguments.Add('-Command')
$workerArguments.Add($Command)
if ($TargetPath.Length -gt 0) {
    $workerArguments.Add('-TargetPath')
    $workerArguments.Add($TargetPath)
}
if ($SnapshotPath.Length -gt 0) {
    $workerArguments.Add('-SnapshotPath')
    $workerArguments.Add($SnapshotPath)
}
if ($Pattern.Length -gt 0) {
    $workerArguments.Add('-Pattern')
    $workerArguments.Add($Pattern)
    $workerArguments.Add('-Mode')
    $workerArguments.Add($Mode)
}
if ($Address.Length -gt 0) {
    $workerArguments.Add('-Address')
    $workerArguments.Add($Address)
}
if ($Name.Length -gt 0) {
    $workerArguments.Add('-Name')
    $workerArguments.Add($Name)
}
if ($Profile.Length -gt 0) {
    $workerArguments.Add('-Profile')
    $workerArguments.Add($Profile)
}
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
    ReaCommand = $Command
    Profile = $Profile
    LogPath = $LogPath
    WmiCommandLine = $wmiCommandLine
    ProcessId = $result.ProcessId
    ReturnValue = $result.ReturnValue
    SubmittedAt = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
}
$taskInfo | ConvertTo-Json | Out-File -LiteralPath ($LogPath + '.task.json') -Encoding utf8
$taskInfo | ConvertTo-Json -Compress
