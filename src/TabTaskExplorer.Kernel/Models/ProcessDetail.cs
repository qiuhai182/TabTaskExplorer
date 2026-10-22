namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 单个进程的详情：只在用户选中进程时按需查询，不做周期轮询。
/// </summary>
public class ProcessDetail
{
    /// <summary>进程编号。</summary>
    public int ProcessId { get; set; }

    /// <summary>可执行文件完整路径，读取失败时为空字符串。</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>启动命令行，读取失败时为空字符串。</summary>
    public string CommandLine { get; set; } = string.Empty;

    /// <summary>启动时间（本地时间，格式：yyyy-MM-dd HH:mm:ss），读取失败时为空字符串。</summary>
    public string StartTimeText { get; set; } = string.Empty;

    /// <summary>进程所有者（域名\用户名），读取失败时为空字符串。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>当前优先级级别，读取失败时为 null。</summary>
    public ProcessPriorityLevel? PriorityLevel { get; set; }

    /// <summary>CPU 亲和性位掩码（第 n 位代表第 n 个处理器），读取失败时为 -1。</summary>
    public long AffinityMask { get; set; } = -1;
}
