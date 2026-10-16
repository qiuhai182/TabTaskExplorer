using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 任务存储服务：任务看板数据的读写。
/// </summary>
public interface ITaskStore
{
    /// <summary>
    /// 读取全部任务。
    /// </summary>
    /// <returns>任务列表，没有数据时返回空列表。</returns>
    IReadOnlyList<TaskItem> LoadAll();

    /// <summary>
    /// 把全部任务整体写回存储。
    /// </summary>
    /// <param name="tasks">要保存的任务列表。</param>
    void SaveAll(IReadOnlyList<TaskItem> tasks);
}
