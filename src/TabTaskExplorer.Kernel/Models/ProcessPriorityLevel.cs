namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 进程优先级级别：六档模型，与 Windows 优先级类别一一对应（参照 Process Explorer 与 Process Lasso）。
/// </summary>
public enum ProcessPriorityLevel
{
    /// <summary>低：仅在系统空闲时运行。</summary>
    Idle = 0,

    /// <summary>低于标准：介于低与标准之间。</summary>
    BelowNormal = 1,

    /// <summary>标准：默认优先级。</summary>
    Normal = 2,

    /// <summary>高于标准：介于标准与高之间。</summary>
    AboveNormal = 3,

    /// <summary>高：优先获得处理器时间。</summary>
    High = 4,

    /// <summary>实时：最高优先级，可能使系统失去响应，谨慎使用。</summary>
    Realtime = 5,
}
