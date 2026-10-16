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
}
