using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 进程详情与调节服务：按需查询单个进程的详情，按需设置优先级与 CPU 亲和性。
/// Windows 平台在适配层实现；其他平台此能力留空（见 NullProcessDetailService）。
/// </summary>
public interface IProcessDetailService
{
    /// <summary>当前平台是否支持详情查询与调节。</summary>
    bool IsSupported { get; }

    /// <summary>
    /// 查询指定进程的详情。
    /// </summary>
    /// <param name="processId">进程编号。</param>
    /// <returns>详情对象；进程不存在或平台不支持时返回 null。</returns>
    ProcessDetail? GetDetail(int processId);

    /// <summary>
    /// 设置指定进程的优先级级别。
    /// </summary>
    /// <param name="processId">进程编号。</param>
    /// <param name="level">目标优先级级别。</param>
    /// <returns>设置成功返回 true，失败返回 false。</returns>
    bool SetPriorityLevel(int processId, ProcessPriorityLevel level);

    /// <summary>
    /// 设置指定进程的 CPU 亲和性位掩码（第 n 位代表第 n 个处理器）。
    /// </summary>
    /// <param name="processId">进程编号。</param>
    /// <param name="affinityMask">亲和性位掩码。</param>
    /// <returns>设置成功返回 true，失败返回 false。</returns>
    bool SetProcessorAffinity(int processId, long affinityMask);
}
