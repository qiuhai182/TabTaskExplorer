using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 磁盘视图面板：统计指定目录下各级条目的磁盘占用情况。
/// 支持切换根目录扫描，扫描在后台线程执行，避免阻塞界面。
/// </summary>
public class DiskViewPanelViewModel : INotifyPropertyChanged
{
    private readonly IDiskUsageProvider diskUsageProvider;

    private string summaryText = "正在加载磁盘视图……";
    private string rootPathInput = string.Empty;
    private List<DiskRow> latestRows = new List<DiskRow>();
    private string latestSummary = string.Empty;
    private CancellationTokenSource? scanCancellationSource;
    private CancellationToken currentScanToken;

    /// <summary>构造函数：注入磁盘用量统计服务。</summary>
    public DiskViewPanelViewModel(IDiskUsageProvider diskUsageProvider)
    {
        this.diskUsageProvider = diskUsageProvider;
    }

    /// <summary>标签条显示的标题。</summary>
    public string Title { get; set; } = "磁盘视图";

    /// <summary>扫描结果行集合。</summary>
    public ObservableCollection<DiskRow> Rows { get; } = new ObservableCollection<DiskRow>();

    /// <summary>属性变化通知事件。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>扫描状态摘要文本。</summary>
    public string SummaryText
    {
        get { return this.summaryText; }
        private set
        {
            this.summaryText = value;
            this.OnPropertyChanged(nameof(this.SummaryText));
        }
    }

    /// <summary>用户输入的根目录路径，留空表示此电脑视图。</summary>
    public string RootPathInput
    {
        get { return this.rootPathInput; }
        set
        {
            this.rootPathInput = value == null ? string.Empty : value;
            this.OnPropertyChanged(nameof(this.RootPathInput));
        }
    }

    /// <summary>开始刷新：按路径输入框中的路径重新扫描。</summary>
    public void StartRefresh()
    {
        this.StartRefresh(this.rootPathInput);
    }

    /// <summary>开始扫描指定根目录，先取消进行中的旧扫描。</summary>
    public void StartRefresh(string rootPath)
    {
        if (this.scanCancellationSource != null)
        {
            this.scanCancellationSource.Cancel();
            this.scanCancellationSource.Dispose();
            this.scanCancellationSource = null;
        }
        string path = rootPath == null ? string.Empty : rootPath;
        this.rootPathInput = path;
        this.OnPropertyChanged(nameof(this.RootPathInput));
        if (string.IsNullOrWhiteSpace(path))
        {
            this.SummaryText = "正在加载磁盘视图……";
        }
        else
        {
            this.SummaryText = "正在扫描 " + path + " ……";
        }
        CancellationTokenSource source = new CancellationTokenSource();
        this.scanCancellationSource = source;
        this.currentScanToken = source.Token;
        Task.Run(new Action(this.ScanInBackground));
    }

    /// <summary>打开条目：目录进入下一级，文件忽略。</summary>
    public void Open(DiskRow? row)
    {
        if (row == null || !row.IsDirectory)
        {
            return;
        }
        this.StartRefresh(row.FullPath);
    }

    /// <summary>返回上一级目录；没有上级时回到此电脑视图。</summary>
    public void NavigateUp()
    {
        string parent = string.Empty;
        try
        {
            string current = this.rootPathInput == null ? string.Empty : this.rootPathInput.Trim();
            if (!string.IsNullOrEmpty(current))
            {
                DirectoryInfo? parentInfo = Directory.GetParent(current);
                if (parentInfo != null)
                {
                    parent = parentInfo.FullName;
                }
            }
        }
        catch (Exception)
        {
            parent = string.Empty;
        }
        this.StartRefresh(parent);
    }

    /// <summary>后台扫描：空路径列出全部驱动器，否则统计顶层条目占用。</summary>
    private void ScanInBackground()
    {
        CancellationToken token = this.currentScanToken;
        string rootPath = this.rootPathInput == null ? string.Empty : this.rootPathInput.Trim();
        try
        {
            List<DiskRow> rows = new List<DiskRow>();
            Stopwatch stopwatch = Stopwatch.StartNew();
            if (string.IsNullOrEmpty(rootPath))
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                foreach (DriveInfo drive in drives)
                {
                    long totalBytes = 0;
                    long freeBytes = 0;
                    try
                    {
                        totalBytes = drive.TotalSize;
                        freeBytes = drive.AvailableFreeSpace;
                    }
                    catch (Exception)
                    {
                        // 驱动器不可访问时忽略容量信息
                    }
                    long usedBytes = totalBytes - freeBytes;
                    DiskRow row = new DiskRow();
                    row.Name = drive.Name;
                    row.FullPath = drive.Name;
                    row.EntryTypeText = "驱动器";
                    row.IsDirectory = true;
                    row.SizeBytes = usedBytes;
                    row.SizeText = this.FormatSize(usedBytes) + " / " + this.FormatSize(totalBytes);
                    rows.Add(row);
                }
                stopwatch.Stop();
                this.latestRows = rows;
                this.latestSummary = "扫描完成：此电脑，共 " + rows.Count + " 个驱动器（耗时 " + stopwatch.Elapsed.TotalSeconds.ToString("F2") + " 秒）";
            }
            else
            {
                IReadOnlyList<DiskEntry> entries = this.diskUsageProvider.GetTopLevelEntries(rootPath);
                foreach (DiskEntry entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    DiskRow row = new DiskRow();
                    row.Name = entry.Name;
                    row.FullPath = entry.FullPath;
                    row.IsDirectory = entry.EntryType == DiskEntryKind.Directory;
                    if (row.IsDirectory)
                    {
                        row.SizeBytes = this.diskUsageProvider.ComputeSize(entry.FullPath, token);
                    }
                    else
                    {
                        row.SizeBytes = entry.SizeBytes;
                    }
                    row.EntryTypeText = row.IsDirectory ? "目录" : "文件";
                    row.SizeText = this.FormatSize(row.SizeBytes);
                    rows.Add(row);
                }
                rows.Sort(DiskViewPanelViewModel.CompareRows);
                long totalSize = 0;
                foreach (DiskRow row in rows)
                {
                    totalSize = totalSize + row.SizeBytes;
                }
                stopwatch.Stop();
                this.latestRows = rows;
                this.latestSummary = "扫描完成：" + rootPath + "，共 " + rows.Count + " 项，共占用 " + this.FormatSize(totalSize) + "（耗时 " + stopwatch.Elapsed.TotalSeconds.ToString("F2") + " 秒）";
            }
            Dispatcher.UIThread.Post(new Action(this.ApplyLatestResults));
        }
        catch (OperationCanceledException)
        {
            // 用户切换了扫描目标，旧扫描静默结束
        }
        catch (Exception exception)
        {
            this.latestRows = new List<DiskRow>();
            this.latestSummary = "扫描失败：" + exception.Message;
            Dispatcher.UIThread.Post(new Action(this.ApplyLatestResults));
        }
    }

    /// <summary>把后台扫描结果应用到界面集合。</summary>
    private void ApplyLatestResults()
    {
        this.Rows.Clear();
        foreach (DiskRow row in this.latestRows)
        {
            this.Rows.Add(row);
        }
        this.SummaryText = this.latestSummary;
    }

    /// <summary>排序规则：目录在前，同级按占用大小降序。</summary>
    private static int CompareRows(DiskRow left, DiskRow right)
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
    private string FormatSize(long sizeBytes)
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
        return sizeBytes.ToString() + " B";
    }

    /// <summary>触发属性变化通知。</summary>
    private void OnPropertyChanged(string propertyName)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>磁盘视图列表的一行：文件或目录的占用统计。</summary>
public class DiskRow
{
    /// <summary>条目显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>条目完整路径。</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>条目类型文本：驱动器、目录或文件。</summary>
    public string EntryTypeText { get; set; } = string.Empty;

    /// <summary>占用字节数。</summary>
    public long SizeBytes { get; set; }

    /// <summary>格式化后的大小文本。</summary>
    public string SizeText { get; set; } = string.Empty;

    /// <summary>是否为目录（驱动器也视为目录，可双击进入）。</summary>
    public bool IsDirectory { get; set; }
}
