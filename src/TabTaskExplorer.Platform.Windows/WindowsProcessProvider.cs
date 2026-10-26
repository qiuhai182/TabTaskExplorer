using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 平台的进程提供者：枚举本机进程与结束指定进程。
/// 进程编号、父进程编号、线程数、优先级基值与映像名一次 Toolhelp32 快照取全
/// （参考 Process Explorer 的实现方式），内存、CPU 累计时间、窗口标题、句柄数、
/// 启动时间、路径、用户名、命令行与 I/O 字节数再逐个进程补齐；
/// 累计处理器时间用于两次采样差分计算每进程处理器占用。
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsProcessProvider : IProcessProvider
{
    public IReadOnlyList<ProcessInfo> ListProcesses()
    {
        List<ProcessSnapshotEntry> snapshotEntries = ReadProcessSnapshotEntries();
        List<ProcessInfo> result = new List<ProcessInfo>();
        foreach (ProcessSnapshotEntry snapshotEntry in snapshotEntries)
        {
            ProcessInfo info = new ProcessInfo();
            info.ProcessId = snapshotEntry.ProcessId;
            info.ParentProcessId = snapshotEntry.ParentProcessId;
            info.ThreadCount = snapshotEntry.ThreadCount;
            info.PriorityLevel = ToPriorityLevel(snapshotEntry.PriorityBase);
            info.Name = snapshotEntry.ExecutableName;
            FillFromProcessApi(info);
            result.Add(info);
        }
        return result;
    }

    public bool Terminate(int processId)
    {
        try
        {
            Process target = Process.GetProcessById(processId);
            target.Kill();
            return true;
        }
        catch
        {
            // 进程不存在或没有权限时结束失败
            return false;
        }
    }

    /// <summary>
    /// 用 Process 类补齐快照里没有的信息：内存、CPU 累计时间、窗口标题、句柄数、启动时间、路径、用户名、命令行。
    /// 打不开进程或单项读取失败时只标记"拒绝访问"，不让整行进程丢弃。
    /// </summary>
    private static void FillFromProcessApi(ProcessInfo info)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(info.ProcessId);
        }
        catch
        {
            // 进程已退出或无权打开：该行按拒绝访问展示
            info.IsAccessDenied = true;
            return;
        }
        using (process)
        {
            try
            {
                string processName = process.ProcessName;
                if (processName.Length > 0)
                {
                    info.Name = processName;
                }
            }
            catch
            {
                // 取不到进程名时保留快照里的映像名
            }
            try
            {
                info.MemoryBytes = process.WorkingSet64;
            }
            catch
            {
                info.IsAccessDenied = true;
            }
            try
            {
                info.CpuTimeTicks = process.TotalProcessorTime.Ticks;
            }
            catch
            {
                info.IsAccessDenied = true;
            }
            try
            {
                info.WindowTitle = process.MainWindowTitle ?? string.Empty;
            }
            catch
            {
                info.WindowTitle = string.Empty;
            }
            try
            {
                info.HandleCount = process.HandleCount;
            }
            catch
            {
                info.HandleCount = 0;
                info.IsAccessDenied = true;
            }
            try
            {
                info.StartTime = process.StartTime;
            }
            catch
            {
                info.StartTime = null;
            }
            try
            {
                info.ExecutablePath = process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                // 无权读取模块信息的系统进程：路径留空
                info.ExecutablePath = string.Empty;
                info.IsAccessDenied = true;
            }
            info.UserName = WindowsProcessDetailService.ReadUserName(process);
            info.CommandLine = WindowsProcessDetailService.ReadCommandLine(process);
            if (info.UserName.Length == 0)
            {
                info.IsAccessDenied = true;
            }
            ReadIoBytes(process, info);
        }
    }

    /// <summary>读取进程累计读写的物理 I/O 字节数；失败时保持 -1（界面显示"不支持"）。</summary>
    private static void ReadIoBytes(Process process, ProcessInfo info)
    {
        try
        {
            if (GetProcessIoCounters(process.Handle, out ProcessIoCounters ioCounters))
            {
                info.ReadIoBytes = (long)ioCounters.ReadTransferCount;
                info.WriteIoBytes = (long)ioCounters.WriteTransferCount;
            }
        }
        catch
        {
            // 进程已退出或无权查询 I/O 计数
        }
    }

    /// <summary>把 Toolhelp32 的优先级基值映射为内核优先级级别；无法识别时返回 null。</summary>
    private static ProcessPriorityLevel? ToPriorityLevel(int priorityBase)
    {
        return priorityBase switch
        {
            64 => (ProcessPriorityLevel?)ProcessPriorityLevel.Idle,
            16384 => ProcessPriorityLevel.BelowNormal,
            32 => ProcessPriorityLevel.Normal,
            32768 => ProcessPriorityLevel.AboveNormal,
            128 => ProcessPriorityLevel.High,
            256 => ProcessPriorityLevel.Realtime,
            _ => null,
        };
    }

    /// <summary>
    /// 一次 Toolhelp32 进程快照取出每个进程的编号、父进程编号、线程数、优先级基值与映像名。
    /// 快照失败时返回空表，此时列表为空、等待下一轮采样。
    /// </summary>
    private static List<ProcessSnapshotEntry> ReadProcessSnapshotEntries()
    {
        List<ProcessSnapshotEntry> snapshotEntries = new List<ProcessSnapshotEntry>();
        IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == new IntPtr(-1))
        {
            // 创建快照失败（句柄不足等罕见情况）
            return snapshotEntries;
        }
        try
        {
            ProcessEntry32W entry = new ProcessEntry32W();
            entry.dwSize = (uint)Marshal.SizeOf(typeof(ProcessEntry32W));
            bool hasEntry = Process32FirstW(snapshot, ref entry);
            while (hasEntry)
            {
                int processId = (int)entry.th32ProcessID;
                if (processId > 0)
                {
                    ProcessSnapshotEntry snapshotEntry = new ProcessSnapshotEntry();
                    snapshotEntry.ProcessId = processId;
                    snapshotEntry.ParentProcessId = (int)entry.th32ParentProcessID;
                    snapshotEntry.ThreadCount = (int)entry.cntThreads;
                    snapshotEntry.PriorityBase = entry.pcPriClassBase;
                    snapshotEntry.ExecutableName = entry.szExeFile ?? string.Empty;
                    snapshotEntries.Add(snapshotEntry);
                }
                hasEntry = Process32NextW(snapshot, ref entry);
            }
        }
        catch
        {
            // 读取快照失败时返回已读到的部分
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return snapshotEntries;
    }

    /// <summary>进程快照中的单条进程记录（快照能取到、无需再打开进程的字段）。</summary>
    private class ProcessSnapshotEntry
    {
        public int ProcessId { get; set; }

        public int ParentProcessId { get; set; }

        public int ThreadCount { get; set; }

        public int PriorityBase { get; set; }

        public string ExecutableName { get; set; } = string.Empty;
    }

    /// <summary>GetProcessIoCounters 返回的进程 I/O 计数结构。</summary>
    private struct ProcessIoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    /// <summary>创建快照的标志：枚举系统内全部进程。</summary>
    private const uint Th32csSnapProcess = 0x00000002;

    /// <summary>进程快照中的单条进程记录。</summary>
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
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out ProcessIoCounters lpIoCounters);
}
