using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 平台的系统资源采样实现：
/// 处理器用性能计数器，内存用 GlobalMemoryStatusEx 系统调用，系统盘用 DriveInfo。
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsSystemMetricsProvider : ISystemMetricsProvider
{
    public SystemSnapshot GetSnapshot()
    {
        SystemSnapshot snapshot = new SystemSnapshot();
        snapshot.CpuUsagePercent = this.SampleCpuUsagePercent();
        this.FillMemory(snapshot);
        this.FillSystemDrive(snapshot);
        return snapshot;
    }

    /// <summary>
    /// 用性能计数器采样全局处理器占用。
    /// 第一次读取只是建立基准，真正数值来自第二次读取，因此这里会等待约 300 毫秒。
    /// </summary>
    private double SampleCpuUsagePercent()
    {
        // 性能计数器的分类名跟随系统语言：英文系统为 Processor，中文系统为 处理器
        string[] categoryNames = { "Processor", "处理器" };
        foreach (string categoryName in categoryNames)
        {
            try
            {
                using (PerformanceCounter counter = new PerformanceCounter(categoryName, "% Processor Time", "_Total"))
                {
                    counter.NextValue();
                    Thread.Sleep(300);
                    double value = counter.NextValue();
                    if (value < 0)
                    {
                        return 0;
                    }
                    return Math.Round(value, 1);
                }
            }
            catch (InvalidOperationException)
            {
                // 当前分类名在系统上不存在，尝试下一个名称
            }
            catch (UnauthorizedAccessException)
            {
                // 没有权限读取性能计数器，保持默认值 0
                return 0;
            }
        }
        return 0;
    }

    /// <summary>通过系统调用读取物理内存总量与可用量。</summary>
    private void FillMemory(SystemSnapshot snapshot)
    {
        MEMORYSTATUSEX status = new MEMORYSTATUSEX();
        status.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        if (GlobalMemoryStatusEx(ref status))
        {
            snapshot.MemoryTotalBytes = (long)status.ullTotalPhys;
            snapshot.MemoryAvailableBytes = (long)status.ullAvailPhys;
        }
    }

    /// <summary>读取系统盘（Windows 目录所在盘）的总量与剩余空间。</summary>
    private void FillSystemDrive(SystemSnapshot snapshot)
    {
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string? root = Path.GetPathRoot(windowsDirectory);
        if (string.IsNullOrEmpty(root))
        {
            return;
        }
        try
        {
            DriveInfo systemDrive = new DriveInfo(root);
            snapshot.DiskTotalBytes = systemDrive.TotalSize;
            snapshot.DiskFreeBytes = systemDrive.AvailableFreeSpace;
        }
        catch
        {
            // 磁盘信息不可用时保持默认值 0
        }
    }

    /// <summary>GlobalMemoryStatusEx 需要的内存状态结构体。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
