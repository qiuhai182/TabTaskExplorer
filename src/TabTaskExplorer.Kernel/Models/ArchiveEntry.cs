using System;

namespace TabTaskExplorer.Kernel.Models;

/// <summary>
/// 压缩包条目：压缩包内部的一个文件或文件夹。
/// </summary>
public class ArchiveEntry
{
    /// <summary>归档内相对路径（使用 '/' 分隔，显示与界面导航用）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 文件名字节的原始序列（tar 直接来自归档头，压缩包原样透传；
    /// 其他格式为按其原生编码转换后的字节）。用于字节级往返保真。
    /// </summary>
    public byte[] RawName { get; set; } = Array.Empty<byte>();

    /// <summary>是否为文件夹。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>是否为符号链接（tar 保留展示；解压时跳过）。</summary>
    public bool IsSymbolicLink { get; set; }

    /// <summary>未压缩大小（字节），文件夹为 0。</summary>
    public long LengthBytes { get; set; }

    /// <summary>压缩后大小（字节），无法获取时为 0。</summary>
    public long CompressedBytes { get; set; }

    /// <summary>最后修改时间（世界协调时）。</summary>
    public DateTime ModifiedTimeUtc { get; set; }
}
