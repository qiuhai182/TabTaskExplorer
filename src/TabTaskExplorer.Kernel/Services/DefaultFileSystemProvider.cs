using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 基于标准库 System.IO 的跨平台文件系统默认实现。
/// 平台适配层可继承并覆写（例如 Windows 的回收站删除）。
/// </summary>
public class DefaultFileSystemProvider : IFileSystemProvider
{
    public IReadOnlyList<FileEntry> ListDirectory(string directoryPath)
    {
        DirectoryListingOptions options = new DirectoryListingOptions();
        options.ShowHiddenFiles = true;
        return this.ListDirectory(directoryPath, options);
    }

    public virtual IReadOnlyList<FileEntry> ListDirectory(string directoryPath, DirectoryListingOptions options)
    {
        List<FileEntry> entries = new List<FileEntry>();
        DirectoryInfo directory = new DirectoryInfo(directoryPath);
        foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
        {
            bool isHidden = (item.Attributes & FileAttributes.Hidden) != 0;
            if (isHidden && !options.ShowHiddenFiles)
            {
                continue;
            }

            FileEntry entry = new FileEntry();
            entry.Name = item.Name;
            entry.FullPath = item.FullName;
            entry.IsDirectory = item is DirectoryInfo;
            entry.IsHidden = isHidden;
            if (item is FileInfo file)
            {
                entry.LengthBytes = file.Length;
            }
            entry.ModifiedTimeUtc = item.LastWriteTimeUtc;
            entries.Add(entry);
        }
        return entries;
    }

    public virtual void CreateDirectory(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
    }

    public virtual void Rename(string path, string newName)
    {
        string parentDirectory = Path.GetDirectoryName(path) ?? string.Empty;
        string targetPath = Path.Combine(parentDirectory, newName);
        if (Directory.Exists(path))
        {
            Directory.Move(path, targetPath);
        }
        else
        {
            File.Move(path, targetPath);
        }
    }

    public virtual void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
