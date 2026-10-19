namespace TabTaskExplorer.Kernel.Models;

/// <summary>连接测试结果。</summary>
public class ConnectionTestResult
{
    /// <summary>是否连接成功。</summary>
    public bool Success { get; set; }

    /// <summary>结果说明（中文，可直接展示给用户）。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>连接耗时（毫秒）；未连接成功时为 0。</summary>
    public int LatencyMilliseconds { get; set; }
}
