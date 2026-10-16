using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Linux;

/// <summary>
/// Linux 平台的系统资源采样实现：内存读 /proc/meminfo，处理器读 /proc/stat，根分区用 DriveInfo。
/// </summary>
public class LinuxSystemMetricsProvider : ISystemMetricsProvider
{
    public SystemSnapshot GetSnapshot()
    {
        SystemSnapshot snapshot = new SystemSnapshot();
        snapshot.CpuUsagePercent = this.SampleCpuUsagePercent();
        this.FillMemory(snapshot);
        this.FillRootDrive(snapshot);
        return snapshot;
    }

    /// <summary>
    /// 连续两次读取 /proc/stat 的总处理器时间片，
    /// 两次差值中非空闲部分的占比就是处理器占用率。
    /// </summary>
    private double SampleCpuUsagePercent()
    {
        string? firstLine = ReadCpuLine();
        Thread.Sleep(300);
        string? secondLine = ReadCpuLine();
        if (firstLine == null || secondLine == null)
        {
            return 0;
        }

        long[] first = ParseCpuTimes(firstLine);
        long[] second = ParseCpuTimes(secondLine);

        // 顺序依次是 user nice system idle iowait，空闲部分取 idle 与 iowait 之和
        long firstIdle = first[3] + first[4];
        long secondIdle = second[3] + second[4];

        long firstTotal = 0;
        foreach (long value in first)
        {
            firstTotal += value;
        }
        long secondTotal = 0;
        foreach (long value in second)
        {
            secondTotal += value;
        }

        long totalDelta = secondTotal - firstTotal;
        long idleDelta = secondIdle - firstIdle;
        if (totalDelta <= 0)
        {
            return 0;
        }
        return Math.Round(100.0 * (totalDelta - idleDelta) / totalDelta, 1);
    }

    /// <summary>读取 /proc/stat 中以 cpu 开头的第一行（全部核心汇总）。</summary>
    private static string? ReadCpuLine()
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/stat"))
            {
                if (line.StartsWith("cpu ", StringComparison.Ordinal))
                {
                    return line;
                }
            }
        }
        catch
        {
            // 非 Linux 环境或文件不可读时返回 null
        }
        return null;
    }

    /// <summary>把 cpu 时间行解析成长整型数组，不足的位补 0。</summary>
    private static long[] ParseCpuTimes(string line)
    {
        long[] times = new long[8];
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 1; index < parts.Length && index <= 8; index++)
        {
            times[index - 1] = long.Parse(parts[index]);
        }
        return times;
    }

    /// <summary>读取 /proc/meminfo 中的物理内存总量与可用量。</summary>
    private void FillMemory(SystemSnapshot snapshot)
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    snapshot.MemoryTotalBytes = ParseKilobytes(line) * 1024L;
                }
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                {
                    snapshot.MemoryAvailableBytes = ParseKilobytes(line) * 1024L;
                }
            }
        }
        catch
        {
            // 文件不可读时保持默认值 0
        }
    }

    /// <summary>解析形如 MemTotal: 16384000 kB 的行，返回千字节数值。</summary>
    private static long ParseKilobytes(string line)
    {
        string numberPart = line.Split(':')[1].Trim().Split(' ')[0];
        return long.Parse(numberPart);
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
}
