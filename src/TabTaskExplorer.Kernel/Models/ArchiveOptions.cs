using System;
using System.Collections.Generic;

namespace TabTaskExplorer.Kernel.Models;

/// <summary>创建压缩包的选项。</summary>
public class ArchiveCreateOptions
{
    /// <summary>容器格式，默认 ZIP。</summary>
    public ArchiveFormat Format { get; set; } = ArchiveFormat.Zip;

    /// <summary>tar 外层压缩流（tar.gz / tar.bz2 等），非 tar 恒为 None。</summary>
    public ArchiveCompression Compression { get; set; } = ArchiveCompression.None;

    /// <summary>压缩级别 0~9（0 不压缩，5 默认，9 最大）。</summary>
    public int CompressionLevel { get; set; } = 5;

    /// <summary>
    /// tar 文件名编码：null = 默认策略（支持原始字节的平台原样透传，其余平台 UTF-8），
    /// "gbk" 等编码名可强制指定。仅作用于 tar 引擎。
    /// </summary>
    public string? NameEncoding { get; set; }
}

/// <summary>解压压缩包的选项。</summary>
public class ArchiveExtractOptions
{
    /// <summary>解压目标目录（不存在时递归创建）。</summary>
    public string TargetDirectory { get; set; } = string.Empty;

    /// <summary>同名文件是否覆盖；false 表示跳过已存在文件。</summary>
    public bool Overwrite { get; set; } = true;

    /// <summary>压缩包密码（加密压缩包解压时使用），无密码为 null。</summary>
    public string? Password { get; set; }

    /// <summary>
    /// 条目文件名解码编码：null = 智能探测（严格 UTF-8 → GBK → 单字节），
    /// 仅作用于 tar 等按原始字节存储名字的格式。
    /// </summary>
    public string? NameEncoding { get; set; }

    /// <summary>只解压指定条目（归档内相对路径）；null 或空表示解压全部。</summary>
    public IReadOnlyList<string>? EntryFilter { get; set; }
}

/// <summary>列出压缩包内容的选项。</summary>
public class ArchiveListOptions
{
    /// <summary>条目文件名解码编码，null 表示智能探测（同解压选项）。</summary>
    public string? NameEncoding { get; set; }
}

/// <summary>
/// 打包源条目：一次压缩的输入单元，同时携带字节名与字符串名，
/// 供 tar 字节透传引擎与字符串型引擎（ZIP / 7z）共用。
/// </summary>
public class ArchiveSourceItem
{
    /// <summary>归档内相对路径的原始字节（'/' 分隔；tar 直接写入归档头）。</summary>
    public byte[] NameBytes { get; set; } = Array.Empty<byte>();

    /// <summary>归档内相对路径字符串（'/' 分隔；供显示与字符串型引擎使用）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>是否为文件夹。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>本机来源路径（字符串模式平台）；字节模式平台为 null。</summary>
    public string? SourcePath { get; set; }

    /// <summary>本机来源路径的原始字节（字节模式平台，Linux 文件名字节透传）；字符串模式平台为 null。</summary>
    public byte[]? SourcePathRaw { get; set; }

    /// <summary>未压缩大小（字节），文件夹为 0。</summary>
    public long LengthBytes { get; set; }

    /// <summary>最后修改时间（世界协调时）。</summary>
    public DateTime ModifiedTimeUtc { get; set; }

    /// <summary>Unix 权限位（07777 掩码）；无法获取时为 0，写入归档头时按默认 0644 / 0755 处理。</summary>
    public int UnixMode { get; set; }
}
