using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 系统资源服务：采样处理器、内存、磁盘用量。
/// 各操作系统在平台适配层各自实现本接口。
/// </summary>
public interface ISystemMetricsProvider
{
    /// <summary>
    /// 采样一次系统资源用量。
    /// </summary>
    /// <returns>系统资源快照。</returns>
    SystemSnapshot GetSnapshot();
}
