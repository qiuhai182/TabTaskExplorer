using System.IO;
using System.Runtime.Versioning;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Platform.Windows;

/// <summary>
/// Windows 平台的磁盘用量提供者：枚举指定路径下的顶层条目并递归统计大小。
/// 说明：WizTree 采用 NTFS MFT 直读实现秒级扫描，但需要管理员权限且仅支持 NTFS；
/// 本实现为普通目录遍历，无需管理员权限。
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsDiskUsageProvider : IDiskUsageProvider
{
    public IReadOnlyList<DiskEntry> GetTopLevelEntries(string rootPath)
    {
        List<DiskEntry> entries = new List<DiskEntry>();
        DirectoryInfo directoryInfo = new DirectoryInfo(rootPath);
        IEnumerable<FileSystemInfo> children = directoryInfo.EnumerateFileSystemInfos();
        foreach (FileSystemInfo child in children)
        {
            DiskEntry entry = new DiskEntry();
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
                FileInfo fileInfo = (FileInfo)child;
                entry.SizeBytes = fileInfo.Length;
            }
            entries.Add(entry);
        }
        return entries;
    }

    public long ComputeSize(string path, CancellationToken cancellationToken)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0)
        {
            FileInfo fileInfo = new FileInfo(path);
            if (fileInfo.Exists)
            {
                return fileInfo.Length;
            }
            return 0;
        }
        DirectoryInfo directoryInfo = new DirectoryInfo(path);
        return ComputeDirectorySize(directoryInfo, cancellationToken);
    }

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
            // 无权限访问的目录按 0 处理，不中断统计
            return 0;
        }
        catch (IOException)
        {
            // 目录枚举中途失败丢失部分时按 0 处理
            return 0;
        }
        foreach (FileInfo file in files)
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
            // 无权限读取子目录时只保留已统计的文件大小
            return totalBytes;
        }
        catch (IOException)
        {
            return totalBytes;
        }
        foreach (DirectoryInfo subDirectory in subDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalBytes += ComputeDirectorySize(subDirectory, cancellationToken);
        }
        return totalBytes;
    }
}
