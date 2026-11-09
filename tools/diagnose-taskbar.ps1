param(
    [string]$OutputPath = 'D:\TabTaskExplorer\output\taskbar-diagnose.txt',
    [string]$ProcessName = 'TabTaskExplorer'
)

$ErrorActionPreference = 'Continue'
if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
function Write-Line([string]$text) {
    Add-Content -LiteralPath $OutputPath -Value $text -Encoding utf8
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class TaskbarDiagNative {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder text);

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_APPWINDOW = 0x00040000;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const uint GW_OWNER = 4;
    public const int DWMWA_CLOAKED = 14;
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    public static string Title(IntPtr hWnd) {
        StringBuilder text = new StringBuilder(512);
        GetWindowText(hWnd, text, text.Capacity);
        return text.ToString();
    }
    public static string ClassName(IntPtr hWnd) {
        StringBuilder text = new StringBuilder(256);
        GetClassName(hWnd, text, text.Capacity);
        return text.ToString();
    }
    public static long ExStyle(IntPtr hWnd) { return GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64(); }
    public static bool Cloaked(IntPtr hWnd) {
        int value = 0;
        int result = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out value, sizeof(int));
        return result == 0 && value != 0;
    }
    public static string OwnerText(IntPtr hWnd) {
        IntPtr owner = GetWindow(hWnd, GW_OWNER);
        if (owner == IntPtr.Zero) { return "-"; }
        return "0x" + owner.ToInt64().ToString("X") + " title=" + Title(owner) + " class=" + ClassName(owner);
    }
    public static string RectText(IntPtr hWnd) {
        RECT rect;
        if (!GetWindowRect(hWnd, out rect)) { return "unknown"; }
        return "(" + rect.Left + "," + rect.Top + ")-(" + rect.Right + "," + rect.Bottom + ")"
            + " size=" + (rect.Right - rect.Left) + "x" + (rect.Bottom - rect.Top);
    }
    public static string Aumid(int processId) {
        IntPtr process = OpenProcess(0x1000, false, processId);
        if (process == IntPtr.Zero) { return "OpenProcessFailed=" + Marshal.GetLastWin32Error(); }
        try {
            uint length = 512;
            StringBuilder text = new StringBuilder(512);
            int result = GetApplicationUserModelId(process, ref length, text);
            if (result == 0) { return text.ToString(); }
            return "Error=" + result + " RequiredLength=" + length;
        } finally { CloseHandle(process); }
    }
    public static string[] WindowsForProcess(int processId) {
        List<string> lines = new List<string>();
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam) {
            uint windowPid = 0;
            GetWindowThreadProcessId(hWnd, out windowPid);
            if ((int)windowPid != processId) { return true; }
            bool visible = IsWindowVisible(hWnd);
            bool iconic = IsIconic(hWnd);
            bool cloaked = Cloaked(hWnd);
            long style = ExStyle(hWnd);
            bool tool = (style & WS_EX_TOOLWINDOW) != 0;
            bool app = (style & WS_EX_APPWINDOW) != 0;
            bool noActivate = (style & WS_EX_NOACTIVATE) != 0;
            bool taskbarCandidate = visible && !tool && !cloaked;
            lines.Add("hwnd=0x" + hWnd.ToInt64().ToString("X")
                + " pid=" + processId
                + " title=[" + Title(hWnd) + "]"
                + " class=[" + ClassName(hWnd) + "]"
                + " visible=" + visible
                + " iconic=" + iconic
                + " cloaked=" + cloaked
                + " toolWindow=" + tool
                + " appWindow=" + app
                + " noActivate=" + noActivate
                + " owner=[" + OwnerText(hWnd) + "]"
                + " rect=" + RectText(hWnd)
                + " taskbarCandidate=" + taskbarCandidate);
            return true;
        }, IntPtr.Zero);
        return lines.ToArray();
    }
}
"@

function Get-ProcessPathSafe($process) {
    try { return $process.Path } catch { return '读取失败：' + $_.Exception.Message }
}

function Get-CommandLineSafe([int]$processId) {
    try {
        $info = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $processId) -ErrorAction Stop
        if ($null -eq $info) { return '未读取到' }
        return ($info.CommandLine + '')
    } catch {
        return '读取失败：' + $_.Exception.Message
    }
}

function Get-MutexState {
    try {
        $mutex = [System.Threading.Mutex]::OpenExisting('TabTaskExplorer_DesktopSingleInstance')
        $mutex.Dispose()
        return '存在（有进程持有或曾持有）'
    } catch [System.Threading.WaitHandleCannotBeOpenedException] {
        return '不存在'
    } catch {
        return '检查失败：' + $_.Exception.Message
    }
}

function Get-PinnedShortcuts {
    $lines = @()
    $pinnedDir = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'
    if (-not (Test-Path -LiteralPath $pinnedDir)) {
        return @('任务栏固定快捷方式目录不存在：' + $pinnedDir)
    }
    $shell = New-Object -ComObject WScript.Shell
    foreach ($file in Get-ChildItem -LiteralPath $pinnedDir -Filter '*.lnk' -ErrorAction SilentlyContinue) {
        try {
            $shortcut = $shell.CreateShortcut($file.FullName)
            $lines += ('快捷方式=' + $file.Name + ' TargetPath=' + $shortcut.TargetPath + ' Arguments=' + $shortcut.Arguments)
        } catch {
            $lines += ('快捷方式=' + $file.Name + ' 读取失败：' + $_.Exception.Message)
        }
    }
    return $lines
}

function Get-RunEntries {
    $lines = @()
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (Test-Path -LiteralPath $runKey) {
        $properties = Get-ItemProperty -LiteralPath $runKey
        foreach ($property in $properties.PSObject.Properties) {
            if ($property.Name -like 'PS*') { continue }
            $lines += ('Run值=' + $property.Name + ' 值=' + $property.Value)
        }
    }
    $startupDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
    if (Test-Path -LiteralPath $startupDir) {
        $shell = New-Object -ComObject WScript.Shell
        foreach ($file in Get-ChildItem -LiteralPath $startupDir -ErrorAction SilentlyContinue) {
            if ($file.Extension -eq '.lnk') {
                try {
                    $shortcut = $shell.CreateShortcut($file.FullName)
                    $lines += ('启动文件夹=' + $file.Name + ' TargetPath=' + $shortcut.TargetPath + ' Arguments=' + $shortcut.Arguments)
                } catch {
                    $lines += ('启动文件夹=' + $file.Name + ' 读取失败：' + $_.Exception.Message)
                }
            }
        }
    }
    return $lines
}

Write-Line ('诊断时间=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'))
Write-Line ('进程名=' + $ProcessName)
Write-Line ('单实例互斥量=' + (Get-MutexState))
Write-Line ''

$processes = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
Write-Line ('进程数量=' + $processes.Count)
if ($processes.Count -eq 0) {
    Write-Line '未发现目标进程；如果任务栏仍有图标，说明是资源管理器残留按钮或已退出的旧进程。'
}

$candidateCount = 0
$windowCount = 0
foreach ($process in $processes) {
    $processId = $process.Id
    $exePath = Get-ProcessPathSafe $process
    $commandLine = Get-CommandLineSafe $processId
    $aumid = [TaskbarDiagNative]::Aumid($processId)
    $startTime = '未知'
    try { $startTime = $process.StartTime.ToString('yyyy-MM-dd HH:mm:ss.fff') } catch { }
    Write-Line ('==================================================')
    Write-Line ('进程 pid=' + $processId + ' start=' + $startTime)
    Write-Line ('  exe=' + $exePath)
    Write-Line ('  commandLine=' + $commandLine)
    Write-Line ('  aumid=' + $aumid)
    Write-Line ('  mainWindowHandle=0x' + $process.MainWindowHandle.ToInt64().ToString('X')
        + ' mainWindowTitle=[' + $process.MainWindowTitle + ']')
    Write-Line '  顶层窗口：'
    $windows = [TaskbarDiagNative]::WindowsForProcess($processId)
    foreach ($line in $windows) {
        $windowCount++
        if ($line -match 'taskbarCandidate=True') { $candidateCount++ }
        Write-Line ('    ' + $line)
    }
}

Write-Line ''
Write-Line '--- 任务栏固定快捷方式 / 启动项 ---'
$pinnedShortcutLines = @(Get-PinnedShortcuts)
$runEntryLines = @(Get-RunEntries)
foreach ($line in $pinnedShortcutLines) { Write-Line $line }
foreach ($line in $runEntryLines) { Write-Line $line }

$taskbarButtonNames = @()
try {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    foreach ($className in @('Shell_TrayWnd', 'MSTaskListWClass')) {
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, $className)
        $tray = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -eq $tray) { continue }
        $buttonCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        $buttons = $tray.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
        foreach ($button in $buttons) {
            $name = $button.Current.Name
            if ($name.Length -gt 0) { $taskbarButtonNames += $name }
        }
    }
} catch {
    $taskbarButtonNames += 'UIA 枚举失败：' + $_.Exception.Message
}

Write-Line ''
Write-Line ('任务栏 UIA 按钮数量=' + $taskbarButtonNames.Count + '（包含开始、搜索等系统按钮，仅作对照）')
foreach ($name in $taskbarButtonNames) { Write-Line ('  按钮=' + $name) }
$appTaskbarButtons = @($taskbarButtonNames | Where-Object {
    $_ -like '*TabTaskExplorer*' -or $_ -like '*贴图*' -or $_ -like '*屏幕取色*'
        -or $_ -like '*标注截图*' -or $_ -like '*选择截图区域*' -or $_ -like '*设置*'
})
Write-Line ('应用相关任务栏按钮数量=' + $appTaskbarButtons.Count)
foreach ($name in $appTaskbarButtons) { Write-Line ('  应用按钮=' + $name) }

Write-Line ''
Write-Line '--- 汇总判断 ---'
Write-Line ('进程数=' + $processes.Count + ' 顶层窗口数=' + $windowCount + ' 任务栏候选窗口数=' + $candidateCount
    + ' 应用相关任务栏按钮数=' + $appTaskbarButtons.Count)
$runningExePaths = @($processes | ForEach-Object { Get-ProcessPathSafe $_ } | Where-Object { $_ -notlike '读取失败*' })
$mismatchedShortcutTargets = @()
foreach ($line in $pinnedShortcutLines) {
    if ($line -match 'TargetPath=(.*?) Arguments=') {
        $targetPath = $Matches[1]
        if ($targetPath -like '*TabTaskExplorer*' -and ($runningExePaths -notcontains $targetPath)) {
            $mismatchedShortcutTargets += $targetPath
        }
    }
}
foreach ($targetPath in $mismatchedShortcutTargets) {
    Write-Line ('固定快捷方式目标与运行 exe 不一致：TargetPath=' + $targetPath
        + ' 运行 exe=' + ($runningExePaths -join ' | ')
        + ' 结果=即使代码相同，任务栏也可能显示为两个图标。')
}
if ($processes.Count -gt 1) {
    Write-Line '存在多个 TabTaskExplorer 进程：先比较上面的 exe 路径；不同 exe/构建会形成不同任务栏分组，结束旧进程。'
}
if ($candidateCount -gt 1) {
    Write-Line '存在多个“可见、非工具窗口、未 cloaked”的窗口：优先检查拖出标签窗口是否仍有任务栏按钮，或某个辅助窗口未成功附加 WS_EX_TOOLWINDOW。'
}
if ($appTaskbarButtons.Count -gt 0 -and $candidateCount -eq 0) {
    Write-Line '有应用相关任务栏按钮但对应进程没有任务栏候选窗口：很可能是资源管理器残留按钮（窗口已销毁但按钮未刷新）。'
}
if ($appTaskbarButtons.Count -gt $candidateCount) {
    Write-Line ('应用相关任务栏按钮数多于候选窗口数（' + $appTaskbarButtons.Count + ' vs ' + $candidateCount
        + '）：可能是任务栏固定快捷方式、分组，也可能是残留按钮；结合上面的标题与 AUMID 判断。')
}
Write-Line ''
Write-Line '--- 清理与修复建议（本脚本不会执行删除） ---'
Write-Line '1. 先通过托盘菜单“退出”或任务管理器结束所有 TabTaskExplorer.exe，避免直接强杀未保存的会话。'
Write-Line '2. 若结束进程后任务栏仍有空按钮，重启资源管理器：taskkill /f /im explorer.exe 然后 start explorer.exe（桌面会短暂刷新）。'
Write-Line '3. 检查任务栏固定快捷方式与启动项：确保 TargetPath 都指向同一个 exe；旧构建目录改名/删除，任务栏取消固定旧图标后重新固定。'
Write-Line '4. 不同 exe 路径（如 bin\win-x64 与 output）即使代码相同也会被 Windows 按路径/快捷方式分组；这不是窗口样式能修复的。'
Write-Line '5. 若上面的“固定快捷方式目标与运行 exe 不一致”命中：右键任务栏该图标取消固定，再从当前正在运行的窗口重新固定；或改为始终启动固定快捷方式指向的那个 exe。'
Write-Line '诊断完成：' + $OutputPath
