using System.IO;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 磁盘用量提供者的跨平台默认实现：基于 System.IO 目录递归统计。
/// 说明：WizTree 采用 NTFS MFT 直读实现秒级扫描，但需要管理员权限且仅支持 NTFS；
/// 本实现为普通目录遍历，无需管理员权限，可在全部平台运行。
/// </summary>
public class DefaultDiskUsageProvider : IDiskUsageProvider
{
    /// <inheritdoc />
    public IReadOnlyList<DiskEntry> GetTopLevelEntries(string rootPath)
    {
        var entries = new List<DiskEntry>();
        var directoryInfo = new DirectoryInfo(rootPath);
        foreach (var child in directoryInfo.EnumerateFileSystemInfos())
        {
            var entry = new DiskEntry();
            entry.Name = child.Name;
            entry.FullPath = child.FullName;
            entry.ParentPath = rootPath;
            if (child is DirectoryInfo)
            {
                entry.EntryType = DiskEntryKind.Directory;
            }
            else
            {
                entry.EntryType = DiskEntryKind.File;
                var fileInfo = (FileInfo)child;
                entry.SizeBytes = fileInfo.Length;
            }
            entries.Add(entry);
        }
        return entries;
    }

    /// <inheritdoc />
    public long ComputeSize(string path, CancellationToken cancellationToken)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0)
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Exists)
            {
                return fileInfo.Length;
            }
            return 0;
        }
        var directoryInfo = new DirectoryInfo(path);
        return ComputeDirectorySize(directoryInfo, cancellationToken);
    }

    /// <summary>
    /// 递归统计一个目录下所有文件的总大小。
    /// 无权限或 IO 异常的分支按 0 处理，不中断统计。
    /// </summary>
    /// <param name="directory">被统计的目录。</param>
    /// <param name="cancellationToken">取消控制。</param>
    /// <returns>目录内全部文件总大小（字节）。</returns>
    private long ComputeDirectorySize(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        long totalBytes = 0;

        FileInfo[] files;
        try
        {
            files = directory.GetFiles();
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
        foreach (var file in files)
        {
            totalBytes += file.Length;
        }

        DirectoryInfo[] subDirectories;
        try
        {
            subDirectories = directory.GetDirectories();
        }
        catch (UnauthorizedAccessException)
        {
            return totalBytes;
        }
        catch (IOException)
        {
            return totalBytes;
        }
        foreach (var subDirectory in subDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalBytes += ComputeDirectorySize(subDirectory, cancellationToken);
        }
        return totalBytes;
    }
}
