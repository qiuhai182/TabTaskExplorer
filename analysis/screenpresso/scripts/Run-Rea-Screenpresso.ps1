<#
Screenpresso 专用 REA 工作脚本（独立于 tools\rea，避免影响 Snipaste 逆向线）

与 tools\rea\Run-Rea.ps1 的差异：
  1. 默认目标指向 Screenpresso 工作副本，默认快照放在 analysis\screenpresso\ghidra
  2. 独立锁文件，避免与 Snipaste 分析互相阻塞
  3. 日志全部落在 analysis\screenpresso\ghidra，不污染 output 根目录
  4. Ghidra 启动时限提高到 3600000 毫秒（1 小时），堆上限 4G（18MB Delphi 程序较大）

用法：
  Start-Worker.ps1 -WorkerPath '...\Run-Rea-Screenpresso.ps1' -TaskName 'rea-analyze'
    -WorkerArguments '-Command analyze -Profile default'
  -Command analyze | search | xrefs | decompile | function | doctor | version
#>

param(
    [ValidateSet('analyze', 'search', 'xrefs', 'decompile', 'function', 'trace', 'doctor', 'version')]
    [string]$Command = 'analyze',
    [string]$TargetPath = 'C:\Users\QiuHai\.cache\rea-screenpresso-target\screenpresso.exe',
    [string]$SnapshotPath = 'D:\TabTaskExplorer\analysis\screenpresso\ghidra\screenpresso.snapshot',
    [string]$Pattern = '',
    [string]$Mode = 'regex',
    [string]$Address = '',
    [string]$Name = '',
    [ValidateSet('default', 'fast')][string]$Profile = 'default',
    [string]$LogPath = '',
    [switch]$ForceFresh
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
Set-Location -LiteralPath 'D:\TabTaskExplorer'

$nodeExecutable = 'C:\Program Files\nodejs\node.exe'
$reaScript = 'C:\Users\QiuHai\AppData\Roaming\npm\node_modules\rea-agents\scripts\rea.mjs'
$lockPath = 'D:\TabTaskExplorer\analysis\screenpresso\ghidra\rea.lock'

if ($LogPath.Length -eq 0) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $LogPath = 'D:\TabTaskExplorer\analysis\screenpresso\ghidra\rea-' + $Command + '-' + $stamp + '.log'
}
$exitCodePath = $LogPath + '.exitcode'
Remove-Item -LiteralPath $exitCodePath -Force -ErrorAction SilentlyContinue
$logDirectory = Split-Path -Path $LogPath -Parent
if (-not (Test-Path -LiteralPath $logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
}

if (Test-Path -LiteralPath $lockPath) {
    $lockOwner = 0
    try { $lockOwner = [int](Get-Content -LiteralPath $lockPath -TotalCount 1) } catch { $lockOwner = 0 }
    $lockAlive = $false
    if ($lockOwner -gt 0) {
        $lockAlive = $null -ne (Get-Process -Id $lockOwner -ErrorAction SilentlyContinue)
    }
    if ($lockAlive) {
        'REA 任务被拒绝：已有分析任务在运行 进程号=' + $lockOwner | Out-File -LiteralPath $LogPath -Encoding utf8
        Set-Content -LiteralPath $exitCodePath -Value 9 -Encoding utf8
        exit 9
    }
    Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
}
Set-Content -LiteralPath $lockPath -Value $PID -Encoding utf8

$env:REA_GHIDRA_STARTUP_TIMEOUT_MS = '3600000'
$env:GHIDRA_HEADLESS_MAXMEM = '4G'
if ($Profile -eq 'fast') {
    $env:REA_GHIDRA_DISABLED_ANALYZERS = 'Decompiler Parameter ID,Decompiler Switch Analysis,Function ID,Demangler GNU'
} else {
    Remove-Item Env:REA_GHIDRA_DISABLED_ANALYZERS -ErrorAction SilentlyContinue
}

if ($ForceFresh -and (Test-Path -LiteralPath $SnapshotPath)) {
    Remove-Item -LiteralPath $SnapshotPath -Force
}

$arguments = New-Object System.Collections.Generic.List[string]
$arguments.Add($reaScript)
if ($Command -eq 'doctor') {
    $arguments.Add('doctor')
} elseif ($Command -eq 'version') {
    $arguments.Add('--version')
} else {
    $arguments.Add($Command)
    $arguments.Add($TargetPath)
    if ($Command -eq 'analyze') {
        $arguments.Add('--snapshot')
        $arguments.Add($SnapshotPath)
    } elseif ($Command -eq 'search') {
        $arguments.Add('--snapshot')
        $arguments.Add($SnapshotPath)
        $arguments.Add('--mode')
        $arguments.Add($Mode)
        $arguments.Add('--pattern')
        $arguments.Add($Pattern)
    } elseif ($Command -eq 'xrefs') {
        $arguments.Add($Address)
    } elseif ($Command -eq 'decompile' -or $Command -eq 'function' -or $Command -eq 'trace') {
        if ($Address.Length -gt 0) {
            $arguments.Add($Address)
        } elseif ($Name.Length -gt 0) {
            $arguments.Add($Name)
        }
    }
    $arguments.Add('--format')
    $arguments.Add('md')
}

'开始时间=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | Out-File -LiteralPath $LogPath -Encoding utf8
'任务类型=' + $Command + ' 目标=' + $TargetPath + ' 配置=' + $Profile + ' 快照=' + $SnapshotPath | Out-File -LiteralPath $LogPath -Encoding utf8 -Append
'分析时限毫秒=' + $env:REA_GHIDRA_STARTUP_TIMEOUT_MS + ' Ghidra堆上限=' + $env:GHIDRA_HEADLESS_MAXMEM | Out-File -LiteralPath $LogPath -Encoding utf8 -Append
'命令行=' + $nodeExecutable + ' ' + ($arguments -join ' ') | Out-File -LiteralPath $LogPath -Encoding utf8 -Append

& $nodeExecutable $arguments.ToArray() 2>&1 | Out-File -LiteralPath $LogPath -Encoding utf8 -Append
$exitCode = $LASTEXITCODE
if ($null -eq $exitCode) { $exitCode = 0 }
'结束时间=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | Out-File -LiteralPath $LogPath -Encoding utf8 -Append
'exit=' + $exitCode | Out-File -LiteralPath $LogPath -Encoding utf8 -Append
Set-Content -LiteralPath $exitCodePath -Value $exitCode -Encoding utf8
Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
exit $exitCode
