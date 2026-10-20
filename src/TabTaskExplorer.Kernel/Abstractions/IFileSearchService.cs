using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 文件搜索服务：按关键字全盘查找文件与文件夹。
/// Windows 上优先消费 Everything 的消息接口实现毫秒级全盘搜索，Everything 不可用时回退到内置遍历搜索。
/// </summary>
public interface IFileSearchService
{
    /// <summary>当前搜索实现是否可用（例如 Everything 是否正在运行）。</summary>
    bool IsAvailable { get; }

    /// <summary>搜索实现的可读名称，用于界面提示当前使用的搜索来源。</summary>
    string DisplayName { get; }

    /// <summary>
    /// 按条件执行全盘搜索，搜索在后台线程执行，不阻塞调用线程。
    /// </summary>
    /// <param name="options">搜索条件。</param>
    /// <returns>命中的结果列表，最多不超过选项中的最大条数。</returns>
    Task<IReadOnlyList<FileSearchResult>> SearchAsync(FileSearchOptions options);
}
