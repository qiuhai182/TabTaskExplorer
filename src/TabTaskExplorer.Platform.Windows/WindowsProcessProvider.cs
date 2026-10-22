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
/// 父进程编号通过 Toolhelp32 快照一次取全（参考 Process Explorer 的实现方式），
/// 累计处理器时间用于两次采样差分计算每进程处理器占用。
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsProcessProvider : IProcessProvider
{
    public IReadOnlyList<ProcessInfo> ListProcesses()
    {
        Dictionary<int, int> parentProcessMap = ReadParentProcessIds();
        List<ProcessInfo> result = new List<ProcessInfo>();
        Process[] processes = Process.GetProcesses();
        foreach (Process process in processes)
        {
            ProcessInfo info = new ProcessInfo();
            try
            {
                info.ProcessId = process.Id;
                info.Name = process.ProcessName;
                info.MemoryBytes = process.WorkingSet64;
                info.WindowTitle = process.MainWindowTitle ?? string.Empty;
                info.CpuTimeTicks = process.TotalProcessorTime.Ticks;
                int parentProcessId;
                if (parentProcessMap.TryGetValue(info.ProcessId, out parentProcessId))
                {
                    info.ParentProcessId = parentProcessId;
                }
            }
            catch
            {
                // 部分系统进程已退出或不允许读取详细信息，跳过即可
                process.Dispose();
                continue;
            }
            result.Add(info);
            process.Dispose();
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
    /// 读取全部进程的父子关系：一次 Toolhelp32 进程快照建立"进程编号 → 父进程编号"对照表。
    /// 快照失败时返回空表，此时所有进程按顶层进程平铺展示。
    /// </summary>
    private static Dictionary<int, int> ReadParentProcessIds()
    {
        Dictionary<int, int> parentProcessMap = new Dictionary<int, int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == new IntPtr(-1))
        {
            // 创建快照失败（句柄不足等罕见情况），按平铺处理
            return parentProcessMap;
        }
        try
        {
            ProcessEntry32W entry = new ProcessEntry32W();
            entry.dwSize = (uint)Marshal.SizeOf(typeof(ProcessEntry32W));
            bool hasEntry = Process32FirstW(snapshot, ref entry);
            while (hasEntry)
            {
                int processId = (int)entry.th32ProcessID;
                if (processId > 0 && !parentProcessMap.ContainsKey(processId))
                {
                    parentProcessMap.Add(processId, (int)entry.th32ParentProcessID);
                }
                hasEntry = Process32NextW(snapshot, ref entry);
            }
        }
        catch
        {
            // 读取快照失败时返回已读到的部分，其余按平铺处理
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return parentProcessMap;
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
}
