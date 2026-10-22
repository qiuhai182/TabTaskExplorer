using System;
using System.Collections.Generic;
using System.IO;

namespace TabTaskExplorer.Kernel.Abstractions;

/// <summary>文件系统条目信息（字符串模式）。</summary>
public class ArchiveFsEntry
{
    /// <summary>条目名称（不含路径）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>是否为文件夹。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>文件大小（字节），文件夹为 0。</summary>
    public long LengthBytes { get; set; }

    /// <summary>最后修改时间（世界协调时）。</summary>
    public DateTime ModifiedTimeUtc { get; set; }

    /// <summary>Unix 权限位（07777 掩码）；平台无法获取时为 0，打包时按默认 0644 / 0755 处理。</summary>
    public int UnixMode { get; set; }
}

/// <summary>文件系统条目信息（原始字节模式：文件名字节原样返回，不做字符集转换）。</summary>
public class ArchiveFsRawEntry
{
    /// <summary>条目名称的原始字节（不含路径；Linux 上即磁盘上的真实字节序列）。</summary>
    public byte[] Name { get; set; } = Array.Empty<byte>();

    /// <summary>是否为文件夹。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>文件大小（字节），文件夹为 0。</summary>
    public long LengthBytes { get; set; }

    /// <summary>最后修改时间（世界协调时）。</summary>
    public DateTime ModifiedTimeUtc { get; set; }

    /// <summary>Unix 权限位（07777 掩码）；无法获取时为 0，打包时按默认 0644 / 0755 处理。</summary>
    public int UnixMode { get; set; }
}

/// <summary>
/// 压缩包引擎所需的文件系统访问抽象，双模式：
/// 字符串模式（全部平台）与原始字节模式（仅 Linux，对应 gnu-tar LC_ALL=C 的字节透传语义，
/// 打包读文件名、解压写文件名均不做任何字符集转换）。
/// </summary>
public interface IArchivePathAccess
{
    /// <summary>是否支持原始字节路径（Linux 为 true，Windows / macOS 为 false）。</summary>
    bool SupportsRawBytePaths { get; }

    // ---- 字符串模式（全部平台可用）----

    /// <summary>列出目录全部条目（含隐藏条目，不递归）。</summary>
    IReadOnlyList<ArchiveFsEntry> ListDirectory(string directoryPath);

    /// <summary>打开文件供读取。</summary>
    Stream OpenRead(string filePath);

    /// <summary>创建（或覆盖）文件并返回可写流；父目录必须已存在。</summary>
    Stream CreateFile(string filePath);

    /// <summary>递归创建目录（已存在时无操作）。</summary>
    void CreateDirectory(string directoryPath);

    /// <summary>删除文件（不存在时无操作）。</summary>
    void DeleteFile(string filePath);

    /// <summary>判断文件是否存在。</summary>
    bool FileExists(string filePath);

    /// <summary>把源文件覆盖替换到目标路径（修改压缩包后原地替换用）。</summary>
    void ReplaceFile(string sourceFilePath, string targetFilePath);

    // ---- 原始字节模式（SupportsRawBytePaths = true 的平台可用，其余平台抛不支持）----

    /// <summary>按原始字节路径列出目录全部条目（文件名字节不转码）。</summary>
    IReadOnlyList<ArchiveFsRawEntry> ListDirectoryRaw(byte[] directoryPath);

    /// <summary>按原始字节路径打开文件供读取。</summary>
    Stream OpenReadRaw(byte[] filePath);

    /// <summary>按原始字节路径创建（或覆盖）文件；父目录必须已存在。</summary>
    Stream CreateFileRaw(byte[] filePath);

    /// <summary>按原始字节路径递归创建目录（已存在时无操作）。</summary>
    void CreateDirectoryRaw(byte[] directoryPath);

    /// <summary>按原始字节路径判断文件是否存在。</summary>
    bool FileExistsRaw(byte[] filePath);
}
