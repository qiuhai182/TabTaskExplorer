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

    /// <summary>线程数，读取不到时为 0。</summary>
    public int ThreadCount { get; set; }

    /// <summary>句柄数，读取不到时为 0（仅 Windows 有值）。</summary>
    public int HandleCount { get; set; }

    /// <summary>进程所有者用户名，读取失败时为空字符串。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>可执行文件完整路径，读取失败时为空字符串。</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>启动命令行，读取失败时为空字符串。</summary>
    public string CommandLine { get; set; } = string.Empty;

    /// <summary>启动时间（本地时间），读取失败时为 null；用于首轮 CPU 占用估算。</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>累计读取的物理 I/O 字节数，读取不到时为 -1。</summary>
    public long ReadIoBytes { get; set; } = -1L;

    /// <summary>累计写入的物理 I/O 字节数，读取不到时为 -1。</summary>
    public long WriteIoBytes { get; set; } = -1L;

    /// <summary>当前优先级级别，读取失败时为 null。</summary>
    public ProcessPriorityLevel? PriorityLevel { get; set; }

    /// <summary>是否因权限不足读不到详细信息（界面标记为"拒绝访问"）。</summary>
    public bool IsAccessDenied { get; set; }
}
