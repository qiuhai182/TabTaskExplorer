<#
REA 后台任务入口（可复用的独立脚本）

为什么需要它：
  - 当前 Trae Shell 会话的沙箱执行层无法直接拉起外部程序（node/git/dotnet 都会返回
    “sandbox backend failed”），但 PowerShell 的 Start-Process 属于进程内 API，可以正常
    创建子进程。
  - REA 的 Ghidra 启动时限默认是 330000 毫秒（约 350 秒），大型程序会在导入分析阶段被
    中止；本脚本把时限提高到 3600000 毫秒（1 小时）。
  - REA 的 --snapshot 是分析快照文件，不是项目缓存；analyze/search 等非 live 操作可以
    复用快照，xrefs/decompile 等实时操作会重新导入，因此必须串行执行，避免多个 Ghidra
    会话互相抢占。
  - 本脚本作为子 PowerShell 进程运行，内部直接调用 node；父 Shell 只需 Start-Process 启动它，
    然后轮询日志与 .exitcode 文件即可。

用法（在 Trae Shell 中，必须用 Start-Process 启动，不能直接点号加载）：
  $runner = 'D:\TabTaskExplorer\tools\rea\Run-Rea.ps1'
  $p = Start-Process -FilePath 'powershell.exe' `
      -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',$runner,'-Command','doctor') `
      -WindowStyle Hidden -PassThru
  # 轮询：
  Get-Content 'D:\TabTaskExplorer\output\rea-doctor-最新.txt' -Tail 20
  Test-Path '<日志>.exitcode'

参数示例：
  -Command analyze  [-ForceFresh]
  -Command search   -Pattern 'OCR/provider|BarcodeDecoder' [-Mode regex]
  -Command xrefs    -Address '0x1405028b8' [-Profile fast]
  -Command decompile -Name 'SP::Pahir::...' 或 -Address '0x140...'
  -Command function  -Address '0x140...'
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
    [string]$LogPath = '',
    [switch]$ForceFresh
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
Set-Location -LiteralPath 'D:\TabTaskExplorer'

$nodeExecutable = 'C:\Program Files\nodejs\node.exe'
$reaScript = 'C:\Users\QiuHai\AppData\Roaming\npm\node_modules\rea-agents\scripts\rea.mjs'
$lockPath = 'D:\TabTaskExplorer\output\rea-analysis.lock'

if ($LogPath.Length -eq 0) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $LogPath = 'D:\TabTaskExplorer\output\rea-' + $Command + '-' + $stamp + '.txt'
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
$env:GHIDRA_HEADLESS_MAXMEM = '3G'
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
