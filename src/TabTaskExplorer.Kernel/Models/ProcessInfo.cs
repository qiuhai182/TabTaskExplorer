namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 进程信息：进程监控面板展示一行数据所需的全部字段。
/// </summary>
public class ProcessInfo
{
    /// <summary>进程编号。</summary>
    public int ProcessId { get; set; }

    /// <summary>进程名称（不含扩展名）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>物理内存占用（字节）。</summary>
    public long MemoryBytes { get; set; }

    /// <summary>主窗口标题，无窗口的进程为空字符串。</summary>
    public string WindowTitle { get; set; } = string.Empty;

    /// <summary>父进程编号，未知或读取不到时为 0。</summary>
    public int ParentProcessId { get; set; }

    /// <summary>累计占用处理器的时间（Tick），用于两次采样差分计算每进程处理器占用。</summary>
    public long CpuTimeTicks { get; set; }
}
