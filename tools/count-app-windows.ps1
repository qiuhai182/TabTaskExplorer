# 统计 TabTaskExplorer 进程的可见顶层窗口，用于验证右键菜单关闭后没有残留弹层窗口。
# 用法：powershell -ExecutionPolicy Bypass -File tools\count-app-windows.ps1
param(
    [string]$ProcessName = 'TabTaskExplorer'
)

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class TabXWindowProbe
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
}
"@

$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) {
    Write-Output "未找到进程 $ProcessName（请先启动程序）"
    exit 0
}

$rows = New-Object System.Collections.Generic.List[object]
$callback = [TabXWindowProbe+EnumWindowsProc]{
    param([IntPtr]$hWnd, [IntPtr]$lParam)
    [uint32]$ownerPid = 0
    [TabXWindowProbe]::GetWindowThreadProcessId($hWnd, [ref]$ownerPid) | Out-Null
    if ($ownerPid -eq $process.Id -and [TabXWindowProbe]::IsWindowVisible($hWnd)) {
        $titleBuilder = New-Object System.Text.StringBuilder 512
        $classBuilder = New-Object System.Text.StringBuilder 512
        [TabXWindowProbe]::GetWindowText($hWnd, $titleBuilder, 512) | Out-Null
        [TabXWindowProbe]::GetClassName($hWnd, $classBuilder, 512) | Out-Null
        $rows.Add([pscustomobject]@{
            句柄 = ('0x{0:X}' -f $hWnd.ToInt64())
            类名 = $classBuilder.ToString()
            标题 = $titleBuilder.ToString()
        })
    }
    return $true
}

[TabXWindowProbe]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null

$rows | Sort-Object 类名, 标题 | Format-Table -AutoSize
$popupCount = @($rows | Where-Object { $_.类名 -match 'Popup|Menu|Context' }).Count
Write-Output "进程=$ProcessName 进程号=$($process.Id)"
Write-Output "可见顶层窗口数=$($rows.Count)"
Write-Output "菜单 / 弹层窗口数=$popupCount（右键菜单关闭后应为 0）"
