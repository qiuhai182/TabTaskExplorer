using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.Services;

/// <summary>
/// 磁盘扫描服务（单例）：桌面主窗口与全部网页端工作区共用一份扫描与增量快照。
/// 职责：
/// 1. 持有共享增量快照，并持久化到 %APPDATA%\TabTaskExplorer\diskview-snapshot.tsv.gz；
/// 2. 同一根目录的并发请求合并为一次运行（单飞），完成结果 15 秒内直接复用；
/// 3. 以事件广播进度与结果，各磁盘视图按自己的根目录过滤后刷新界面；
/// 4. 提供台前周期探测：只比较可见目录与快照的修改时间，有变化才请求增量补扫。
/// </summary>
public class DiskScanService
{
    /// <summary>同根目录扫描完成后，结果在该时间内可直接复用，避免反复重扫。</summary>
    private static readonly TimeSpan ResultCacheDuration = TimeSpan.FromSeconds(15);

    private readonly IDiskUsageProvider diskUsageProvider;
    private readonly object scanLock = new object();
    private readonly Dictionary<string, ScanRun> runningScans = new Dictionary<string, ScanRun>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DiskScanResult> recentResults = new Dictionary<string, DiskScanResult>(StringComparer.OrdinalIgnoreCase);
    private readonly object snapshotLock = new object();
    private readonly object snapshotFileLock = new object();
    private readonly string snapshotFilePath;
    private DiskUsageSnapshot? snapshot;

    /// <summary>扫描进度事件：后台线程触发，订阅方自行投递到界面线程。</summary>
    public event Action<DiskScanProgress>? ProgressChanged;

    /// <summary>扫描完成事件：后台线程或请求线程触发，订阅方自行投递到界面线程。</summary>
    public event Action<DiskScanResult>? ScanCompleted;

    /// <summary>创建扫描服务：快照文件与设置文件同目录。</summary>
    /// <param name="diskUsageProvider">平台磁盘用量提供者。</param>
    public DiskScanService(IDiskUsageProvider diskUsageProvider)
    {
        this.diskUsageProvider = diskUsageProvider;
        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TabTaskExplorer");
        this.snapshotFilePath = Path.Combine(dataDirectory, "diskview-snapshot.tsv.gz");
    }

    /// <summary>是否有扫描正在运行：台前探测在运行期间不再叠加请求。</summary>
    public bool IsScanning
    {
        get
        {
            lock (this.scanLock)
            {
                return this.runningScans.Count > 0;
            }
        }
    }

    /// <summary>
    /// 请求扫描指定根目录：同路径已有运行中的扫描时直接加入（单飞）；
    /// forceFull 完全重扫会取消普通扫描另起，allowCache 允许复用 15 秒内完成结果。
    /// 此电脑视图（空根目录）不经过本服务，由视图自己列驱动器。
    /// </summary>
    /// <param name="rootPath">扫描根目录（非空）。</param>
    /// <param name="ownerId">请求方标识（每个磁盘视图一个），离开台面后用于释放订阅。</param>
    /// <param name="forceFull">是否完全重扫：忽略快照与近期结果。</param>
    /// <param name="allowCache">是否允许复用近期完成结果。</param>
    public void RequestScan(string rootPath, string ownerId, bool forceFull, bool allowCache)
    {
        string key = rootPath == null ? string.Empty : rootPath.Trim();
        if (key.Length == 0)
        {
            return;
        }
        DiskScanResult? cachedResult = null;
        ScanRun? newRun = null;
        lock (this.scanLock)
        {
            ScanRun? running;
            if (this.runningScans.TryGetValue(key, out running))
            {
                if (forceFull && !running.ForceFull)
                {
                    // 请求完全重扫而当前是普通扫描：取消旧运行，下面另起完全重扫
                    UiLogger.Log("磁盘扫描服务", "完全重扫替换进行中的普通扫描：根目录=" + key);
                    running.Owners.Clear();
                    running.Cancellation.Cancel();
                    this.runningScans.Remove(key);
                }
                else
                {
                    if (!running.Owners.Contains(ownerId))
                    {
                        running.Owners.Add(ownerId);
                    }
                    UiLogger.Log("磁盘扫描服务", "加入进行中的扫描：根目录=" + key
                        + " 请求方=" + ownerId + " 订阅者数=" + running.Owners.Count);
                    return;
                }
            }
            DiskScanResult? cached;
            if (!forceFull && allowCache
                && this.recentResults.TryGetValue(key, out cached)
                && !cached.Failed
                && DateTime.Now - cached.CompletedAt < ResultCacheDuration)
            {
                cachedResult = cached;
            }
            else
            {
                newRun = new ScanRun(this, key, forceFull);
                newRun.Owners.Add(ownerId);
                this.runningScans[key] = newRun;
            }
        }
        if (cachedResult != null)
        {
            UiLogger.Log("磁盘扫描服务", "复用近期完成结果：根目录=" + key
                + " 完成时间=" + cachedResult.CompletedAt.ToString("HH:mm:ss"));
            this.RaiseCompleted(cachedResult);
            return;
        }
        if (newRun != null)
        {
            UiLogger.Log("磁盘扫描服务", "开始扫描：根目录=" + key
                + " 模式=" + (newRun.ForceFull ? "完全重扫" : "增量扫描") + " 请求方=" + ownerId);
            Task.Run(newRun.Execute);
        }
    }

    /// <summary>
    /// 释放对某根目录扫描的订阅：请求方离开台面时调用；
    /// 没有任何订阅者时取消该扫描，保证后台不残留扫描任务。
    /// </summary>
    /// <param name="rootPath">此前请求过的根目录。</param>
    /// <param name="ownerId">请求方标识。</param>
    public void ReleaseScan(string rootPath, string ownerId)
    {
        string key = rootPath == null ? string.Empty : rootPath.Trim();
        if (key.Length == 0)
        {
            return;
        }
        bool cancelled = false;
        lock (this.scanLock)
        {
            ScanRun? running;
            if (!this.runningScans.TryGetValue(key, out running))
            {
                return;
            }
            running.Owners.Remove(ownerId);
            if (running.Owners.Count == 0)
            {
                this.runningScans.Remove(key);
                running.Cancellation.Cancel();
                cancelled = true;
            }
        }
        if (cancelled)
        {
            UiLogger.Log("磁盘扫描服务", "扫描已取消（已无台前订阅者）：根目录=" + key);
        }
    }

    /// <summary>
    /// 台前周期探测：比较可见目录当前修改时间与快照记录，返回是否需要增量补扫。
    /// 快照未加载、已有扫描在运行、目录无法访问或目录从未记录时均不触发，避免无意义的重复扫描。
    /// </summary>
    /// <param name="directoryPaths">当前可见的目录路径列表。</param>
    /// <returns>是否需要请求一次增量补扫。</returns>
    public bool NeedsRefresh(IReadOnlyList<string> directoryPaths)
    {
        DiskUsageSnapshot? current = this.snapshot;
        if (current == null)
        {
            return false;
        }
        lock (this.scanLock)
        {
            if (this.runningScans.Count > 0)
            {
                return false;
            }
        }
        foreach (string path in directoryPaths)
        {
            long recordedTicks;
            if (!current.TryGetRecordedMtime(path, out recordedTicks))
            {
                // 从未记录的目录交给首次扫描与展开回填流程，探测不触发
                continue;
            }
            long actualTicks;
            try
            {
                actualTicks = Directory.GetLastWriteTimeUtc(path).Ticks;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
            if (actualTicks != recordedTicks)
            {
                UiLogger.Log("磁盘扫描服务", "台前探测到目录变化：路径=" + path);
                return true;
            }
        }
        return false;
    }

    /// <summary>把一个目录的当前修改时间与大小记入快照（此电脑视图的驱动器行等场景使用，供台前探测比较）。</summary>
    /// <param name="directoryPath">目录路径。</param>
    /// <param name="lastWriteTicks">当前修改时间（UTC ticks）。</param>
    /// <param name="sizeBytes">关联大小（仅作记录，探测只比较修改时间）。</param>
    public void RecordDirectoryMtime(string directoryPath, long lastWriteTicks, long sizeBytes)
    {
        DiskUsageSnapshot current = this.GetSnapshot();
        current.Record(directoryPath, lastWriteTicks, sizeBytes, true);
    }

    /// <summary>把快照持久化到磁盘：扫描完成或展开回填完成后调用，失败只记日志。</summary>
    public void SaveSnapshot()
    {
        DiskUsageSnapshot? current = this.snapshot;
        if (current == null)
        {
            return;
        }
        lock (this.snapshotFileLock)
        {
            try
            {
                current.Save(this.snapshotFilePath);
                UiLogger.Log("磁盘扫描服务", "保存磁盘快照：文件=" + this.snapshotFilePath
                    + " 记录数=" + current.Count);
            }
            catch (Exception exception)
            {
                UiLogger.LogError("磁盘扫描服务", "保存磁盘快照失败：文件=" + this.snapshotFilePath, exception);
            }
        }
    }

    /// <summary>取共享快照：首次访问时从磁盘加载（只加载一次）。</summary>
    public DiskUsageSnapshot GetSnapshot()
    {
        lock (this.snapshotLock)
        {
            if (this.snapshot == null)
            {
                DiskUsageSnapshot loaded = new DiskUsageSnapshot();
                try
                {
                    loaded.Load(this.snapshotFilePath);
                    UiLogger.Log("磁盘扫描服务", "加载磁盘快照：文件=" + this.snapshotFilePath
                        + " 记录数=" + loaded.Count);
                }
                catch (Exception exception)
                {
                    UiLogger.LogError("磁盘扫描服务", "加载磁盘快照失败，按全新快照开始：文件=" + this.snapshotFilePath, exception);
                }
                this.snapshot = loaded;
            }
            return this.snapshot;
        }
    }

    /// <summary>执行一次扫描：并行统计顶层条目、组装结果、回写快照并广播事件。</summary>
    /// <param name="run">本次运行的描述（根目录、模式、取消源、订阅者）。</param>
    private void ExecuteScan(ScanRun run)
    {
        CancellationToken token = run.Cancellation.Token;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            DiskUsageSnapshot scanSnapshot = this.GetSnapshot();
            long reusedBefore = scanSnapshot.ReusedDirectoryCount;
            long scannedBefore = scanSnapshot.ScannedDirectoryCount;
            UiLogger.Log("磁盘扫描服务", "扫描启动：根目录=" + run.RootPath
                + " 快照记录数=" + scanSnapshot.Count
                + " 模式=" + (run.ForceFull ? "完全重扫" : "增量扫描"));
            IReadOnlyList<DiskEntry> entries = this.diskUsageProvider.GetTopLevelEntries(run.RootPath);
            ScanCoordinator coordinator = new ScanCoordinator(this, entries, run.RootPath, scanSnapshot, run.ForceFull, token);
            List<DiskRow> rows = coordinator.Run();
            rows.Sort(CompareRows);
            long totalSize = 0;
            foreach (DiskRow row in rows)
            {
                totalSize = totalSize + row.SizeBytes;
            }
            // 记录根目录自身的修改时间：台前周期探测据此判断根目录一级是否变化
            try
            {
                long rootMtime = new DirectoryInfo(run.RootPath).LastWriteTimeUtc.Ticks;
                scanSnapshot.Record(run.RootPath, rootMtime, totalSize, true);
            }
            catch (UnauthorizedAccessException)
            {
                // 根目录修改时间取不到时跳过记录，探测会对该根目录保持静默
            }
            catch (IOException)
            {
                // 同上
            }
            stopwatch.Stop();
            long reusedCount = scanSnapshot.ReusedDirectoryCount - reusedBefore;
            long scannedCount = scanSnapshot.ScannedDirectoryCount - scannedBefore - 1;
            DiskScanResult result = new DiskScanResult();
            result.RootPath = run.RootPath;
            result.Rows = rows;
            result.Failed = false;
            result.CompletedAt = DateTime.Now;
            result.SummaryText = "扫描完成：" + run.RootPath
                + "，共 " + rows.Count + " 项，共占用 " + FormatSize(totalSize)
                + "（耗时 " + stopwatch.Elapsed.TotalSeconds.ToString("F2") + " 秒"
                + "，快照复用 " + reusedCount + " 个目录，实际扫描 " + scannedCount + " 个目录）";
            bool publish;
            lock (this.scanLock)
            {
                ScanRun? current;
                if (this.runningScans.TryGetValue(run.RootPath, out current) && object.ReferenceEquals(current, run))
                {
                    this.runningScans.Remove(run.RootPath);
                }
                publish = !run.Cancellation.IsCancellationRequested;
                if (publish)
                {
                    this.recentResults[run.RootPath] = result;
                }
            }
            this.SaveSnapshot();
            UiLogger.Log("磁盘扫描服务", result.SummaryText);
            if (publish)
            {
                this.RaiseCompleted(result);
            }
        }
        catch (OperationCanceledException)
        {
            lock (this.scanLock)
            {
                ScanRun? current;
                if (this.runningScans.TryGetValue(run.RootPath, out current) && object.ReferenceEquals(current, run))
                {
                    this.runningScans.Remove(run.RootPath);
                }
            }
            UiLogger.Log("磁盘扫描服务", "扫描已取消：根目录=" + run.RootPath);
        }
        catch (Exception exception)
        {
            lock (this.scanLock)
            {
                ScanRun? current;
                if (this.runningScans.TryGetValue(run.RootPath, out current) && object.ReferenceEquals(current, run))
                {
                    this.runningScans.Remove(run.RootPath);
                }
            }
            UiLogger.LogError("磁盘扫描服务", "扫描失败：根目录=" + run.RootPath, exception);
            DiskScanResult failure = new DiskScanResult();
            failure.RootPath = run.RootPath;
            failure.Rows = new List<DiskRow>();
            failure.Failed = true;
            failure.CompletedAt = DateTime.Now;
            failure.SummaryText = "扫描失败：" + exception.Message;
            if (!run.Cancellation.IsCancellationRequested)
            {
                this.RaiseCompleted(failure);
            }
        }
    }

    /// <summary>广播进度给全部订阅视图（订阅方按根目录过滤并投递到界面线程）。</summary>
    /// <param name="progress">进度快照。</param>
    private void RaiseProgress(DiskScanProgress progress)
    {
        Action<DiskScanProgress>? handler = this.ProgressChanged;
        if (handler != null)
        {
            handler(progress);
        }
    }

    /// <summary>广播完成结果给全部订阅视图。</summary>
    /// <param name="result">完成结果。</param>
    private void RaiseCompleted(DiskScanResult result)
    {
        Action<DiskScanResult>? handler = this.ScanCompleted;
        if (handler != null)
        {
            handler(result);
        }
    }

    /// <summary>排序规则：目录在前，同级按占用大小降序。</summary>
    public static int CompareRows(DiskRow left, DiskRow right)
    {
        if (left.IsDirectory && !right.IsDirectory)
        {
            return -1;
        }
        if (!left.IsDirectory && right.IsDirectory)
        {
            return 1;
        }
        return right.SizeBytes.CompareTo(left.SizeBytes);
    }

    /// <summary>把字节数格式化为人类可读的大小文本。</summary>
    /// <param name="sizeBytes">字节数。</param>
    /// <returns>例如 1.23 GB。</returns>
    public static string FormatSize(long sizeBytes)
    {
        double size = sizeBytes;
        if (size >= 1073741824D)
        {
            return (size / 1073741824D).ToString("F2") + " GB";
        }
        if (size >= 1048576D)
        {
            return (size / 1048576D).ToString("F2") + " MB";
        }
        if (size >= 1024D)
        {
            return (size / 1024D).ToString("F1") + " KB";
        }
        return sizeBytes + " B";
    }

    /// <summary>一次运行中的扫描：根目录、模式、取消源与台前订阅者列表。</summary>
    private sealed class ScanRun
    {
        private readonly DiskScanService owner;

        public ScanRun(DiskScanService owner, string rootPath, bool forceFull)
        {
            this.owner = owner;
            this.RootPath = rootPath;
            this.ForceFull = forceFull;
        }

        public string RootPath { get; }

        public bool ForceFull { get; }

        public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        public List<string> Owners { get; } = new List<string>();

        /// <summary>后台执行入口：以方法组交给 Task.Run，避免委托闭包。</summary>
        public void Execute()
        {
            this.owner.ExecuteScan(this);
        }
    }

    /// <summary>扫描进度快照：事件携带原始数值，界面侧格式化成摘要文本。</summary>
    public class DiskScanProgress
    {
        public string RootPath { get; set; } = string.Empty;

        public int FinishedCount { get; set; }

        public int TotalCount { get; set; }

        public long CountedBytes { get; set; }

        public string CurrentName { get; set; } = string.Empty;
    }

    /// <summary>扫描完成结果：行集合由服务持有，订阅方克隆后使用，保证多视图互不干扰。</summary>
    public class DiskScanResult
    {
        public string RootPath { get; set; } = string.Empty;

        public List<DiskRow> Rows { get; set; } = new List<DiskRow>();

        public string SummaryText { get; set; } = string.Empty;

        public bool Failed { get; set; }

        public DateTime CompletedAt { get; set; }
    }

    /// <summary>
    /// 并行扫描协调器：把顶层条目分派到多个处理器核心同时递归统计，
    /// 聚合各条目的实时进度并按 250 毫秒节流广播进度事件。
    /// </summary>
    private sealed class ScanCoordinator
    {
        private readonly DiskScanService service;
        private readonly IReadOnlyList<DiskEntry> entries;
        private readonly string rootPath;
        private readonly DiskUsageSnapshot snapshot;
        private readonly bool forceFull;
        private readonly CancellationToken token;
        private readonly DiskRow[] rows;
        private readonly long[] liveBytes;
        private readonly ScanEntryProbe[] probes;
        private long finishedBytes;
        private int finishedCount;
        private long lastReportTimestamp;
        private string currentName = string.Empty;
        private Exception? firstException;

        /// <summary>创建协调器：准备每条目的结果槽、进度槽与进度探针。</summary>
        public ScanCoordinator(DiskScanService service, IReadOnlyList<DiskEntry> entries, string rootPath, DiskUsageSnapshot snapshot, bool forceFull, CancellationToken token)
        {
            this.service = service;
            this.entries = entries;
            this.rootPath = rootPath;
            this.snapshot = snapshot;
            this.forceFull = forceFull;
            this.token = token;
            this.rows = new DiskRow[entries.Count];
            this.liveBytes = new long[entries.Count];
            this.probes = new ScanEntryProbe[entries.Count];
            for (int index = 0; index < entries.Count; index++)
            {
                this.probes[index] = new ScanEntryProbe(this, index);
            }
        }

        /// <summary>并行统计全部顶层条目，返回结果行列表；有条目失败时抛出该异常保持失败提示。</summary>
        public List<DiskRow> Run()
        {
            ParallelOptions options = new ParallelOptions();
            options.MaxDegreeOfParallelism = Environment.ProcessorCount;
            options.CancellationToken = this.token;
            Parallel.For(0, this.entries.Count, options, this.ScanOneEntry);
            if (this.firstException != null)
            {
                throw this.firstException;
            }
            List<DiskRow> result = new List<DiskRow>(this.rows.Length);
            foreach (DiskRow row in this.rows)
            {
                if (row != null)
                {
                    result.Add(row);
                }
            }
            return result;
        }

        /// <summary>统计单个顶层条目：目录递归统计（带增量快照），文件直接取自身大小。</summary>
        private void ScanOneEntry(int index)
        {
            DiskEntry entry = this.entries[index];
            try
            {
                DiskRow row = new DiskRow();
                row.Name = entry.Name;
                row.FullPath = entry.FullPath;
                row.IsDirectory = entry.EntryType == DiskEntryKind.Directory;
                if (row.IsDirectory)
                {
                    row.SizeBytes = this.service.diskUsageProvider.ComputeSize(
                        entry.FullPath, this.token, this.probes[index].Report, this.snapshot, this.forceFull);
                }
                else
                {
                    row.SizeBytes = entry.SizeBytes;
                }
                row.EntryTypeText = row.IsDirectory ? "目录" : "文件";
                row.SizeText = FormatSize(row.SizeBytes);
                this.rows[index] = row;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // 单个条目失败先记录，等全部条目跑完后统一抛出，与整次扫描失败的提示保持一致
                Interlocked.CompareExchange(ref this.firstException, exception, null);
                DiskRow failedRow = new DiskRow();
                failedRow.Name = entry.Name;
                failedRow.FullPath = entry.FullPath;
                failedRow.IsDirectory = entry.EntryType == DiskEntryKind.Directory;
                failedRow.EntryTypeText = failedRow.IsDirectory ? "目录" : "文件";
                failedRow.SizeText = FormatSize(0);
                this.rows[index] = failedRow;
            }
            Interlocked.Exchange(ref this.liveBytes[index], 0);
            Interlocked.Add(ref this.finishedBytes, this.rows[index].SizeBytes);
            int doneCount = Interlocked.Increment(ref this.finishedCount);
            this.ReportProgress(doneCount);
        }

        /// <summary>接收某条目扫描过程中的累计字节数，来自进度探针的回调。</summary>
        public void OnEntryBytes(int index, long bytes)
        {
            Interlocked.Exchange(ref this.liveBytes[index], bytes);
            this.currentName = this.entries[index].Name;
            int doneCount = Interlocked.CompareExchange(ref this.finishedCount, 0, 0);
            this.ReportProgress(doneCount);
        }

        /// <summary>按 250 毫秒节流广播进度事件。</summary>
        private void ReportProgress(int doneCount)
        {
            if (this.token.IsCancellationRequested)
            {
                return;
            }
            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.CompareExchange(ref this.lastReportTimestamp, 0, 0);
            if (now - previous < Stopwatch.Frequency / 4)
            {
                return;
            }
            if (Interlocked.CompareExchange(ref this.lastReportTimestamp, now, previous) != previous)
            {
                return;
            }
            long inFlightBytes = 0;
            for (int index = 0; index < this.liveBytes.Length; index++)
            {
                inFlightBytes = inFlightBytes + Interlocked.CompareExchange(ref this.liveBytes[index], 0, 0);
            }
            DiskScanProgress progress = new DiskScanProgress();
            progress.RootPath = this.rootPath;
            progress.FinishedCount = doneCount;
            progress.TotalCount = this.entries.Count;
            progress.CountedBytes = Interlocked.CompareExchange(ref this.finishedBytes, 0, 0) + inFlightBytes;
            progress.CurrentName = this.currentName == null ? string.Empty : this.currentName;
            this.service.RaiseProgress(progress);
        }
    }

    /// <summary>单个条目的进度探针：把统计到的字节数回传给协调器（避免使用委托闭包）。</summary>
    private sealed class ScanEntryProbe
    {
        private readonly ScanCoordinator coordinator;
        private readonly int index;

        public ScanEntryProbe(ScanCoordinator coordinator, int index)
        {
            this.coordinator = coordinator;
            this.index = index;
        }

        /// <summary>作为进度回调传给磁盘用量提供者：参数为该条目已统计到的字节数。</summary>
        public void Report(long bytes)
        {
            this.coordinator.OnEntryBytes(this.index, bytes);
        }
    }
}
