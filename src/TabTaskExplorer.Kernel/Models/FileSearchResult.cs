namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 文件搜索结果：一条命中的文件或文件夹信息。
/// </summary>
public class FileSearchResult
{
    /// <summary>条目的完整路径。</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>条目名称（不含路径）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>条目所在的目录完整路径。</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>是否为文件夹。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>文件大小（字节），文件夹固定为 0。</summary>
    public long LengthBytes { get; set; }

    /// <summary>最后修改时间（世界协调时）。</summary>
    public DateTime ModifiedTimeUtc { get; set; }
}
