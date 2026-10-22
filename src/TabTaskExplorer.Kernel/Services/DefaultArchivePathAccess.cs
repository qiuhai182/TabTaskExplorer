using System;
using System.Collections.Generic;
using System.IO;
using TabTaskExplorer.Kernel.Abstractions;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 字符串模式的压缩包文件系统访问实现（全部平台通用）。
/// 原始字节模式由 Linux 平台层提供；Windows / macOS 与 Linux 的字符串场景统一走本实现。
/// </summary>
public sealed class DefaultArchivePathAccess : IArchivePathAccess
{
    /// <summary>字符串模式实现，不支持原始字节路径。</summary>
    public bool SupportsRawBytePaths => false;

    /// <summary>列出目录全部条目（含隐藏条目，不递归）。</summary>
    public IReadOnlyList<ArchiveFsEntry> ListDirectory(string directoryPath)
    {
        List<ArchiveFsEntry> result = new List<ArchiveFsEntry>();
        foreach (string path in Directory.EnumerateFileSystemEntries(directoryPath))
        {
            ArchiveFsEntry entry = new ArchiveFsEntry();
            entry.Name = Path.GetFileName(path);
            if (Directory.Exists(path))
            {
                entry.IsDirectory = true;
                DirectoryInfo info = new DirectoryInfo(path);
                entry.ModifiedTimeUtc = info.LastWriteTimeUtc;
            }
            else
            {
                FileInfo info = new FileInfo(path);
                entry.LengthBytes = info.Length;
                entry.ModifiedTimeUtc = info.LastWriteTimeUtc;
            }
            result.Add(entry);
        }
        return result;
    }

    /// <summary>打开文件供读取。</summary>
    public Stream OpenRead(string filePath)
    {
        return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <summary>创建（或覆盖）文件并返回可写流；父目录必须已存在。</summary>
    public Stream CreateFile(string filePath)
    {
        return new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
    }

    /// <summary>递归创建目录（已存在时无操作）。</summary>
    public void CreateDirectory(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
    }

    /// <summary>删除文件（不存在时无操作）。</summary>
    public void DeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    /// <summary>判断文件是否存在。</summary>
    public bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    /// <summary>把源文件覆盖替换到目标路径（修改压缩包后原地替换用）。</summary>
    public void ReplaceFile(string sourceFilePath, string targetFilePath)
    {
        File.Copy(sourceFilePath, targetFilePath, true);
        File.Delete(sourceFilePath);
    }

    /// <summary>字符串模式不支持原始字节路径。</summary>
    public IReadOnlyList<ArchiveFsRawEntry> ListDirectoryRaw(byte[] directoryPath)
    {
        throw new NotSupportedException("当前平台不支持原始字节路径访问");
    }

    /// <summary>字符串模式不支持原始字节路径。</summary>
    public Stream OpenReadRaw(byte[] filePath)
    {
        throw new NotSupportedException("当前平台不支持原始字节路径访问");
    }

    /// <summary>字符串模式不支持原始字节路径。</summary>
    public Stream CreateFileRaw(byte[] filePath)
    {
        throw new NotSupportedException("当前平台不支持原始字节路径访问");
    }

    /// <summary>字符串模式不支持原始字节路径。</summary>
    public void CreateDirectoryRaw(byte[] directoryPath)
    {
        throw new NotSupportedException("当前平台不支持原始字节路径访问");
    }

    /// <summary>字符串模式不支持原始字节路径。</summary>
    public bool FileExistsRaw(byte[] filePath)
    {
        throw new NotSupportedException("当前平台不支持原始字节路径访问");
    }
}
