using Avalonia;
using System.Runtime.InteropServices;

namespace TabTaskExplorer.App;

/// <summary>程序入口：单实例判定与参数转发，然后启动 Avalonia 桌面生命周期。</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 入口日志：本次启动收到的全部参数（系统唤起的文件夹路径走这里进入）
        Services.UiLogger.Log("程序启动", "程序入口：进程号=" + Environment.ProcessId
            + " 参数个数=" + args.Length + " 参数内容=" + string.Join(" | ", args));
        // 启动时按保留策略清理旧日志：保留最新 7 份且总量不超过 256 兆，清理失败不影响启动
        Services.UiLogger.CleanupOldLogs();
        LogInvocationContext();

        // 进程级 AppUserModelID 必须在任何窗口（包括 Avalonia 内部窗口）创建前设置；
        // 放在入口点，覆盖命令行、文件关联、浏览器/系统唤起与第二实例降级启动等全部路径
        if (OperatingSystem.IsWindows())
        {
            bool identityApplied = TabTaskExplorer.Platform.Windows.WindowsWindowHostService
                .TrySetProcessAppUserModelId(Services.WindowHostStyler.AppUserModelId);
            Services.UiLogger.Log("窗口任务栏", "程序入口提前设置进程 AppUserModelID：取值="
                + Services.WindowHostStyler.AppUserModelId
                + " 结果=" + (identityApplied ? "成功" : "失败"));
        }

        // 单实例：拿到互斥量说明没有既有实例，正常启动主实例
        if (Services.SingleInstanceService.AcquirePrimary())
        {
            Services.UiLogger.Log("单实例服务", "单实例判定结果：未发现既有实例，本进程作为主实例正常启动");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return;
        }
        // 已有实例运行：把本次启动参数（系统唤起的文件夹路径）经命名管道转发给既有实例后退出；
        // 转发失败但成功抢占到互斥量时降级为正常启动，保证唤起请求不丢失
        Services.UiLogger.Log("单实例服务", "单实例判定结果：检测到既有实例运行，本进程作为第二实例启动，先尝试经命令管道转发启动参数后退出");
        if (!Services.SingleInstanceService.TryRunAsSecondary(args))
        {
            Services.UiLogger.Log("单实例服务", "第二实例转发未成功但已抢占到互斥量，降级为独立主实例启动，唤起请求改由本进程直接处理");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        else
        {
            Services.UiLogger.Log("单实例服务", "第二实例转发完成，本进程退出，唤起请求由既有实例处理");
        }
    }

    /// <summary>
    /// 记录本次唤起的调用上下文（父进程 + 当前工作目录）：
    /// 外部程序（微信“在文件夹中显示”等）以零参数拉起本程序时，参数里不带路径，
    /// 只有拿到父进程身份与工作目录，才能定位调用方是谁、目标目录是否写在工作目录里。
    /// </summary>
    private static void LogInvocationContext()
    {
        string workingDirectory;
        try
        {
            workingDirectory = Environment.CurrentDirectory;
        }
        catch (Exception ex)
        {
            workingDirectory = "读取失败：" + ex.Message;
        }

        string parentDescription = "未知（未读取到父进程信息）";
        try
        {
            parentDescription = OperatingSystem.IsWindows()
                ? DescribeParentProcess()
                : "未知（非 Windows 平台不读取父进程）";
        }
        catch (Exception ex)
        {
            parentDescription = "未知（读取父进程异常：" + ex.Message + "）";
        }

        Services.UiLogger.Log("程序启动", "唤起上下文：父进程=" + parentDescription + " 当前工作目录=" + workingDirectory);
    }

    /// <summary>
    /// 用 Toolhelp 进程快照查父进程名（单次快照，毫秒级完成，不拖慢单实例判定）。
    /// 刻意不用进程监控面板的 ListProcesses：它带 CPU/IO 采样，实测会让启动多等约 12 秒。
    /// </summary>
    private static string DescribeParentProcess()
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(0x00000002 /* TH32CS_SNAPPROCESS */, 0);
        if (snapshot == new IntPtr(-1))
        {
            return "未知（进程快照创建失败，错误码=" + Marshal.GetLastWin32Error() + "）";
        }
        try
        {
            Dictionary<int, string> namesByPid = new Dictionary<int, string>();
            int parentPid = 0;
            ProcessEntry32W entry = new ProcessEntry32W();
            entry.dwSize = (uint)Marshal.SizeOf(typeof(ProcessEntry32W));
            if (Process32FirstW(snapshot, ref entry))
            {
                do
                {
                    namesByPid[(int)entry.th32ProcessID] = entry.szExeFile;
                    if ((int)entry.th32ProcessID == Environment.ProcessId)
                    {
                        parentPid = (int)entry.th32ParentProcessID;
                    }
                }
                while (Process32NextW(snapshot, ref entry));
            }
            if (parentPid <= 0)
            {
                return "未知（快照中未找到本进程）";
            }
            return namesByPid.TryGetValue(parentPid, out string? parentName)
                ? parentName + "（父进程号=" + parentPid + "）"
                : "父进程号=" + parentPid + "（父进程已退出）";
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    /// <summary>Toolhelp 进程快照条目（只取需要的字段：进程号、父进程号、可执行文件名）。</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Avalonia 应用构建配置：按当前操作系统选择平台实现。</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
