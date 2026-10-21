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

    public long ComputeSize(string path, CancellationToken cancellationToken, Action<long>? onProgress = null, DiskUsageSnapshot? snapshot = null, bool forceFull = false)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0)
        {
            FileInfo fileInfo = new FileInfo(path);
            if (fileInfo.Exists)
            {
                onProgress?.Invoke(fileInfo.Length);
                return fileInfo.Length;
            }
            onProgress?.Invoke(0);
            return 0;
        }
        DirectoryInfo directoryInfo = new DirectoryInfo(path);
        ProgressTracker tracker = new ProgressTracker(onProgress);
        bool rootComplete;
        long totalBytes = ComputeDirectorySize(directoryInfo, cancellationToken, tracker, snapshot, forceFull, out rootComplete);
        tracker.Flush();
        return totalBytes;
    }

    /// <summary>
    /// 递归统计一个目录下所有文件的总大小，并在结束时把结果回写快照。
    /// 采用单次惰性枚举（一次遍历同时收集文件与子目录），避免两次物化数组带来的开销。
    /// 无权限或 IO 异常的分支按已统计到的部分处理，不中断统计，本目录标记为不完整。
    /// 传入快照时逐目录判定：修改时间未变且记录有效的目录整棵子树复用上次大小，不再向下枚举。
    /// </summary>
    /// <param name="directory">被统计的目录。</param>
    /// <param name="cancellationToken">取消控制。</param>
    /// <param name="progressTracker">进度跟踪器，负责节流上报已统计字节数。</param>
    /// <param name="snapshot">增量快照；传空表示完全重扫且不回写。</param>
    /// <param name="forceFull">是否忽略快照强制完全重扫（仍回写新结果）。</param>
    /// <param name="complete">本目录及其子树是否统计完整；不完整时下次扫描必须重试。</param>
    /// <returns>目录内全部文件总大小（字节）。</returns>
    private static long ComputeDirectorySize(DirectoryInfo directory, CancellationToken cancellationToken, ProgressTracker progressTracker, DiskUsageSnapshot? snapshot, bool forceFull, out bool complete)
    {
        complete = true;
        // 有快照时先取目录修改时间：未变化的整棵子树直接复用上次结果，只重扫变化过的部分
        long lastWriteTicks = 0;
        bool recordable = false;
        if (snapshot != null)
        {
            try
            {
                lastWriteTicks = directory.LastWriteTimeUtc.Ticks;
                recordable = true;
            }
            catch (UnauthorizedAccessException)
            {
                recordable = false;
            }
            catch (IOException)
            {
                recordable = false;
            }
            if (recordable && !forceFull)
            {
                long reusedBytes;
                if (snapshot.TryReuse(directory.FullName, lastWriteTicks, out reusedBytes))
                {
                    progressTracker.Add(reusedBytes);
                    return reusedBytes;
                }
            }
        }
        long totalBytes = 0;
        List<DirectoryInfo> subDirectories = new List<DirectoryInfo>();
        try
        {
            foreach (FileSystemInfo child in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (child is DirectoryInfo subDirectory)
                {
                    subDirectories.Add(subDirectory);
                    continue;
                }
                FileInfo fileInfo = (FileInfo)child;
                long fileLength = fileInfo.Length;
                totalBytes += fileLength;
                progressTracker.Add(fileLength);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 无权限访问的目录按已统计到的部分处理，不中断统计
            complete = false;
        }
        catch (IOException)
        {
            // 目录枚举中途失败丢失部分时按已统计到的部分处理
            complete = false;
        }
        foreach (DirectoryInfo subDirectory in subDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long subDirectoryBytes;
            bool subDirectoryComplete;
            subDirectoryBytes = ComputeDirectorySize(subDirectory, cancellationToken, progressTracker, snapshot, forceFull, out subDirectoryComplete);
            totalBytes += subDirectoryBytes;
            if (!subDirectoryComplete)
            {
                complete = false;
            }
        }
        if (snapshot != null && recordable)
        {
            snapshot.Record(directory.FullName, lastWriteTicks, totalBytes, complete);
        }
        return totalBytes;
    }

    /// <summary>
    /// 单次扫描内部的进度跟踪：累计已统计字节数，并按「每 128MB 或每 100 毫秒」节流上报。
    /// 每次 ComputeSize 调用创建独立实例，保证并行扫描时互不干扰。
    /// </summary>
    private sealed class ProgressTracker
    {
        private readonly Action<long>? onProgress;
        private readonly System.Diagnostics.Stopwatch? reportStopwatch;
        private long totalBytes;
        private long reportedBytes;

        public ProgressTracker(Action<long>? onProgress)
        {
            this.onProgress = onProgress;
            this.reportStopwatch = onProgress == null ? null : System.Diagnostics.Stopwatch.StartNew();
        }

        /// <summary>累加一个文件的字节数，并按节流条件上报进度。</summary>
        public void Add(long bytes)
        {
            this.totalBytes += bytes;
            if (this.reportStopwatch == null || this.onProgress == null)
            {
                return;
            }
            if (this.totalBytes - this.reportedBytes >= 134217728L || this.reportStopwatch.ElapsedMilliseconds >= 100L)
            {
                this.Report();
            }
        }

        /// <summary>统计结束时把最终结果补报给调用方。</summary>
        public void Flush()
        {
            if (this.onProgress == null || this.totalBytes == this.reportedBytes)
            {
                return;
            }
            this.Report();
        }

        private void Report()
        {
            this.reportedBytes = this.totalBytes;
            if (this.reportStopwatch != null)
            {
                this.reportStopwatch.Restart();
            }
            this.onProgress!(this.totalBytes);
        }
    }
}
