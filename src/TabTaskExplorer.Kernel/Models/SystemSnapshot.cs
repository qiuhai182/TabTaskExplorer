namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 系统资源快照：一次采样得到的处理器、内存、系统盘用量。
/// </summary>
public class SystemSnapshot
{
    /// <summary>处理器占用百分比（0 到 100），未实现时为 0。</summary>
    public double CpuUsagePercent { get; set; }

    /// <summary>物理内存总量（字节）。</summary>
    public long MemoryTotalBytes { get; set; }

    /// <summary>物理内存可用量（字节）。</summary>
    public long MemoryAvailableBytes { get; set; }

    /// <summary>系统盘总量（字节）。</summary>
    public long DiskTotalBytes { get; set; }

    /// <summary>系统盘剩余空间（字节）。</summary>
    public long DiskFreeBytes { get; set; }

    /// <summary>采样时间（世界协调时）。</summary>
    public DateTime SampleTimeUtc { get; set; } = DateTime.UtcNow;
}
