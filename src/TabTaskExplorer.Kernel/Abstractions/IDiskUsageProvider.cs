using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 磁盘用量提供者：枚举指定路径下的顶层条目并统计占用大小。
/// 由各操作系统平台分别提供实现接口。
/// </summary>
public interface IDiskUsageProvider
{
    /// <summary>
    /// 枚举指定根路径下的全部目录与文件，并递归统计大小。
    /// </summary>
    /// <param name="rootPath">根路径，例如 C:\ 或某个文件夹。</param>
    /// <returns>条目列表。</returns>
    IReadOnlyList<DiskEntry> GetTopLevelEntries(string rootPath);

    /// <summary>
    /// 计算指定路径占用大小：目录递归统计全部文件，文件返回自身大小。
    /// 传入增量快照时，修改时间未变的目录整棵子树直接复用上次结果，只重扫变化过的部分。
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <param name="cancellationToken">取消控制，用于中途停止扫描。</param>
    /// <param name="onProgress">可选的进度回调：参数为当前已统计到的字节数，统计过程中会按节流频率多次上报。</param>
    /// <param name="snapshot">可选的增量快照：修改时间未变的目录复用上次大小；传空表示完全重扫。</param>
    /// <param name="forceFull">是否忽略快照强制完全重扫（仍会把新结果回写进快照）。</param>
    /// <returns>总大小（字节）。</returns>
    long ComputeSize(string path, CancellationToken cancellationToken, Action<long>? onProgress = null, DiskUsageSnapshot? snapshot = null, bool forceFull = false);
}
