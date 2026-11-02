using System.IO;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 目录比对引擎：递归比较两个目录，输出对齐后的条目与状态。
/// 纯托管实现，Windows / Linux / macOS 行为一致；不依赖任何平台专用接口。
/// </summary>
public class DirectoryCompareEngine
{
    /// <summary>条目数量上限，防止误选超大目录导致内存与界面失控。</summary>
    public const int MaxEntryCount = 200000;

    /// <summary>
    /// 比较两个目录。
    /// </summary>
    /// <param name="leftRoot">左侧根目录；可以不存在（此时全部条目视为仅右侧存在）。</param>
    /// <param name="rightRoot">右侧根目录；可以不存在（此时全部条目视为仅左侧存在）。</param>
    /// <param name="options">比对选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按相对路径排序的比对结果。</returns>
    public DirectoryCompareResult Compare(string leftRoot, string rightRoot,
        DirectoryCompareOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leftRoot) || string.IsNullOrWhiteSpace(rightRoot))
        {
            throw new ArgumentException("目录比对需要左右两个目录路径");
        }

        DirectoryCompareResult result = new DirectoryCompareResult();
        bool leftExists = Directory.Exists(leftRoot);
        bool rightExists = Directory.Exists(rightRoot);
        if (leftExists || rightExists)
        {
            this.CompareDirectory(leftRoot, rightRoot, string.Empty, options, result, cancellationToken);
        }

        result.Entries.Sort(CompareByRelativePath);
        CountStatuses(result);
        return result;
    }

    /// <summary>比较一个目录层级，并在需要时递归进入两侧同名的子目录。</summary>
    private void CompareDirectory(string leftDirectory, string rightDirectory, string relativePrefix,
        DirectoryCompareOptions options, DirectoryCompareResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Entries.Count >= MaxEntryCount)
        {
            return;
        }

        Dictionary<string, FileSystemInfo> leftEntries = ListEntries(leftDirectory, options.IncludeHidden,
            options.IgnoreCase);
        Dictionary<string, FileSystemInfo> rightEntries = ListEntries(rightDirectory, options.IncludeHidden,
            options.IgnoreCase);
        StringComparer comparer = options.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        List<string> names = new List<string>();
        foreach (string name in leftEntries.Keys)
        {
            names.Add(name);
        }
        foreach (string name in rightEntries.Keys)
        {
            if (!leftEntries.ContainsKey(name))
            {
                names.Add(name);
            }
        }
        names.Sort((first, second) => comparer.Compare(first, second));

        foreach (string name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Entries.Count >= MaxEntryCount)
            {
                return;
            }

            FileSystemInfo? left = leftEntries.TryGetValue(name, out FileSystemInfo? leftItem) ? leftItem : null;
            FileSystemInfo? right = rightEntries.TryGetValue(name, out FileSystemInfo? rightItem) ? rightItem : null;
            string relativePath = string.IsNullOrEmpty(relativePrefix) ? name : relativePrefix + "/" + name;

            DirectoryCompareEntry entry = new DirectoryCompareEntry();
            entry.RelativePath = relativePath;
            entry.Name = name;
            entry.LeftExists = left != null;
            entry.RightExists = right != null;
            entry.LeftPath = left == null ? string.Empty : left.FullName;
            entry.RightPath = right == null ? string.Empty : right.FullName;

            if (left == null && right != null)
            {
                // 仅右侧：目录只登记一层，用户双击后进入该目录再做逐层比较
                entry.IsDirectory = (right.Attributes & FileAttributes.Directory) != 0;
                FillSideInfo(entry, false, right);
                entry.Status = CompareEntryStatus.RightOnly;
                result.Entries.Add(entry);
                continue;
            }
            if (right == null && left != null)
            {
                entry.IsDirectory = (left.Attributes & FileAttributes.Directory) != 0;
                FillSideInfo(entry, true, left);
                entry.Status = CompareEntryStatus.LeftOnly;
                result.Entries.Add(entry);
                continue;
            }
            if (left == null || right == null)
            {
                continue;
            }

            bool leftIsDirectory = (left.Attributes & FileAttributes.Directory) != 0;
            bool rightIsDirectory = (right.Attributes & FileAttributes.Directory) != 0;
            FillSideInfo(entry, true, left);
            FillSideInfo(entry, false, right);

            if (leftIsDirectory && rightIsDirectory)
            {
                entry.IsDirectory = true;
                entry.Status = CompareEntryStatus.Same;
                result.Entries.Add(entry);
                if (options.IncludeSubDirectories && !IsReparsePoint(left) && !IsReparsePoint(right))
                {
                    this.CompareDirectory(left.FullName, right.FullName, relativePath, options, result, cancellationToken);
                }
                continue;
            }
            if (leftIsDirectory != rightIsDirectory)
            {
                entry.IsDirectory = false;
                entry.Status = CompareEntryStatus.TypeMismatch;
                result.Entries.Add(entry);
                continue;
            }

            entry.IsDirectory = false;
            entry.Status = CompareFiles(left, right, options);
            result.Entries.Add(entry);
        }
    }

    /// <summary>枚举目录条目并建立名称索引；目录不存在时返回空索引。</summary>
    private static Dictionary<string, FileSystemInfo> ListEntries(string directoryPath, bool includeHidden,
        bool ignoreCase)
    {
        StringComparer comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Dictionary<string, FileSystemInfo> entries = new Dictionary<string, FileSystemInfo>(comparer);
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            return entries;
        }
        try
        {
            DirectoryInfo directory = new DirectoryInfo(directoryPath);
            foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
            {
                if (!includeHidden && (item.Attributes & FileAttributes.Hidden) != 0)
                {
                    continue;
                }
                if (!entries.ContainsKey(item.Name))
                {
                    entries.Add(item.Name, item);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 无权读取的目录按空目录处理，比对继续；界面会在摘要中提示差异数量
        }
        catch (IOException)
        {
            // 目录在枚举过程中被删除或占用：按当前已枚举到的条目继续
        }
        return entries;
    }

    /// <summary>把一侧的大小与修改时间写入条目。</summary>
    private static void FillSideInfo(DirectoryCompareEntry entry, bool isLeft, FileSystemInfo info)
    {
        long length = 0;
        DateTime modified = DateTime.MinValue;
        if ((info.Attributes & FileAttributes.Directory) == 0)
        {
            try
            {
                FileInfo file = new FileInfo(info.FullName);
                if (file.Exists)
                {
                    length = file.Length;
                    modified = file.LastWriteTimeUtc;
                }
            }
            catch (IOException)
            {
                // 文件在枚举后被删除：大小与时间保持默认值，状态仍按名称对齐
            }
        }
        else
        {
            modified = info.LastWriteTimeUtc;
        }

        if (isLeft)
        {
            entry.LeftLengthBytes = length;
            entry.LeftModifiedTimeUtc = modified;
        }
        else
        {
            entry.RightLengthBytes = length;
            entry.RightModifiedTimeUtc = modified;
        }
    }

    /// <summary>判断目录是否为符号链接或重解析点，避免递归成环。</summary>
    private static bool IsReparsePoint(FileSystemInfo info)
    {
        return (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    /// <summary>比较两个文件：大小、可选内容比较与修改时间容差。</summary>
    private static CompareEntryStatus CompareFiles(FileSystemInfo left, FileSystemInfo right,
        DirectoryCompareOptions options)
    {
        FileInfo leftFile = new FileInfo(left.FullName);
        FileInfo rightFile = new FileInfo(right.FullName);
        if (!leftFile.Exists || !rightFile.Exists)
        {
            return CompareEntryStatus.Different;
        }
        if (leftFile.Length != rightFile.Length)
        {
            return CompareEntryStatus.Different;
        }
        if (options.CompareFileContent)
        {
            bool equal;
            try
            {
                equal = FilesHaveSameContent(leftFile.FullName, rightFile.FullName);
            }
            catch (IOException)
            {
                return CompareEntryStatus.Different;
            }
            catch (UnauthorizedAccessException)
            {
                return CompareEntryStatus.Different;
            }
            return equal ? CompareEntryStatus.Same : CompareEntryStatus.Different;
        }

        long toleranceTicks = Math.Max(0, options.ModifiedTimeToleranceSeconds) * TimeSpan.TicksPerSecond;
        long difference = leftFile.LastWriteTimeUtc.Ticks - rightFile.LastWriteTimeUtc.Ticks;
        if (Math.Abs(difference) <= toleranceTicks)
        {
            return CompareEntryStatus.Same;
        }
        return difference > 0 ? CompareEntryStatus.LeftNewer : CompareEntryStatus.RightNewer;
    }

    /// <summary>流式比较两个文件内容是否逐字节相同，避免一次性读入大文件。</summary>
    private static bool FilesHaveSameContent(string leftPath, string rightPath)
    {
        const int BufferSize = 81920;
        using FileStream leftStream = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using FileStream rightStream = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (leftStream.Length != rightStream.Length)
        {
            return false;
        }
        byte[] leftBuffer = new byte[BufferSize];
        byte[] rightBuffer = new byte[BufferSize];
        while (true)
        {
            int leftRead = leftStream.Read(leftBuffer, 0, BufferSize);
            int rightRead = rightStream.Read(rightBuffer, 0, BufferSize);
            if (leftRead != rightRead)
            {
                return false;
            }
            if (leftRead == 0)
            {
                return true;
            }
            for (int index = 0; index < leftRead; index++)
            {
                if (leftBuffer[index] != rightBuffer[index])
                {
                    return false;
                }
            }
        }
    }

    /// <summary>按相对路径排序：父目录自然排在子条目之前。</summary>
    private static int CompareByRelativePath(DirectoryCompareEntry first, DirectoryCompareEntry second)
    {
        return string.Compare(first.RelativePath, second.RelativePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>统计各状态的条目数量。</summary>
    private static void CountStatuses(DirectoryCompareResult result)
    {
        result.SameCount = 0;
        result.DifferentCount = 0;
        result.LeftOnlyCount = 0;
        result.RightOnlyCount = 0;
        foreach (DirectoryCompareEntry entry in result.Entries)
        {
            switch (entry.Status)
            {
                case CompareEntryStatus.Same:
                    result.SameCount++;
                    break;
                case CompareEntryStatus.LeftOnly:
                    result.LeftOnlyCount++;
                    break;
                case CompareEntryStatus.RightOnly:
                    result.RightOnlyCount++;
                    break;
                default:
                    result.DifferentCount++;
                    break;
            }
        }
    }
}
