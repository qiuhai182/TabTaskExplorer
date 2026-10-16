using System.Runtime.InteropServices;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.MacOS;

/// <summary>
/// macOS 平台的系统资源采样实现：
/// 物理内存总量通过 sysctl 读取，根分区用 DriveInfo，
/// 处理器占用与可用内存需要 host_processor_info 等调用，留待后续版本接入。
/// </summary>
public class MacSystemMetricsProvider : ISystemMetricsProvider
{
    public SystemSnapshot GetSnapshot()
    {
        SystemSnapshot snapshot = new SystemSnapshot();
        snapshot.MemoryTotalBytes = this.GetTotalPhysicalMemory();
        this.FillRootDrive(snapshot);
        return snapshot;
    }

    /// <summary>通过 sysctl 读取物理内存总量（字节）。</summary>
    private long GetTotalPhysicalMemory()
    {
        IntPtr neededSize = IntPtr.Zero;
        int firstResult = sysctlbyname("hw.memsize", IntPtr.Zero, ref neededSize, IntPtr.Zero, IntPtr.Zero);
        if (firstResult != 0 || neededSize.ToInt64() <= 0)
        {
            return 0;
        }

        IntPtr buffer = Marshal.AllocHGlobal(neededSize);
        try
        {
            int secondResult = sysctlbyname("hw.memsize", buffer, ref neededSize, IntPtr.Zero, IntPtr.Zero);
            if (secondResult != 0)
            {
                return 0;
            }
            return Marshal.ReadInt64(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>读取根分区的总量与剩余空间。</summary>
    private void FillRootDrive(SystemSnapshot snapshot)
    {
        try
        {
            DriveInfo rootDrive = new DriveInfo("/");
            snapshot.DiskTotalBytes = rootDrive.TotalSize;
            snapshot.DiskFreeBytes = rootDrive.AvailableFreeSpace;
        }
        catch
        {
            // 磁盘信息不可用时保持默认值 0
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int sysctlbyname(string name, IntPtr oldValue, ref IntPtr oldSize, IntPtr newValue, IntPtr newSize);
}
