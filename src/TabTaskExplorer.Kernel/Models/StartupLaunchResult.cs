namespace TabTaskExplorer.Kernel.Models;

/// <summary>一次启动项拉起的结果：成功与否、提示文字与拉起后的进程号（网页链接为 0）。</summary>
public class StartupLaunchResult
{
    /// <summary>是否成功提交拉起。</summary>
    public bool Success { get; set; }

    /// <summary>结果说明（成功时含进程号，失败时含原因）。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>拉起后的进程号；网页链接或拿不到进程号时为 0。</summary>
    public int ProcessId { get; set; }
}
