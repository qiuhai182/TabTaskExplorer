using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>
/// 远程连接配置存储：负责连接配置列表的读取与保存。
/// 默认实现为内核 JSON 文件存储，可替换为数据库等实现。
/// </summary>
public interface IConnectionStore
{
    /// <summary>底层存储文件路径；用于凭据迁移前备份原件，无法提供时返回空字符串。</summary>
    string StorePath
    {
        get { return string.Empty; }
    }

    /// <summary>读取全部连接配置。</summary>
    /// <returns>全部连接配置；文件不存在时返回空列表。</returns>
    IReadOnlyList<RemoteConnectionProfile> LoadAll();

    /// <summary>整体保存连接配置列表。</summary>
    /// <param name="profiles">要保存的全部连接配置。</param>
    void SaveAll(IReadOnlyList<RemoteConnectionProfile> profiles);
}
