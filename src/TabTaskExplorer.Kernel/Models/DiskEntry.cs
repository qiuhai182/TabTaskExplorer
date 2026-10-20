namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 磁盘条目类型：目录或文件。
/// </summary>
public enum DiskEntryKind
{
    /// <summary>目录项。</summary>
    Directory,

    /// <summary>文件项。</summary>
    File
}

/// <summary>
/// 磁盘条目模型：对应磁盘视图列表中的一行，表示一个目录或文件。
/// </summary>
public class DiskEntry
{
    /// <summary>条目显示名称（不含路径部分）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>条目完整路径。</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>条目类型：目录或文件。</summary>
    public DiskEntryKind EntryType { get; set; }

    /// <summary>条目占用大小（字节），目录为递归统计的全部文件总大小，未统计时为 0。</summary>
    public long SizeBytes { get; set; }

    /// <summary>父目录路径。</summary>
    public string ParentPath { get; set; } = string.Empty;
}
