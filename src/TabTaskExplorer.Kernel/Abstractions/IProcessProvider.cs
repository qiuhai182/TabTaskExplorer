using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 进程服务：枚举进程与结束进程。
/// 各操作系统在平台适配层各自实现本接口。
/// </summary>
public interface IProcessProvider
{
    /// <summary>
    /// 枚举当前系统的全部进程。
    /// </summary>
    /// <returns>进程信息列表。</returns>
    IReadOnlyList<ProcessInfo> ListProcesses();

    /// <summary>
    /// 结束指定编号的进程。
    /// </summary>
    /// <param name="processId">进程编号。</param>
    /// <returns>结束成功返回 true，失败返回 false。</returns>
    bool Terminate(int processId);
}
