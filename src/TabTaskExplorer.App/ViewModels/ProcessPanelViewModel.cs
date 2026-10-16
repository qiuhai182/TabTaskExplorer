using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>进程监控面板：骨架阶段提供系统资源摘要与进程列表。</summary>
public class ProcessPanelViewModel : INotifyPropertyChanged
{
    private readonly IProcessProvider processProvider;
    private readonly ISystemMetricsProvider metricsProvider;

    private string summaryText = "正在加载系统资源……";
    private SystemSnapshot? latestSnapshot;
    private List<ProcessInfo> latestProcesses = new List<ProcessInfo>();

    /// <summary>界面绑定的进程列表。</summary>
    public ObservableCollection<ProcessRow> Rows { get; } = new ObservableCollection<ProcessRow>();

    /// <summary>顶部摘要文字：处理器、内存占用与进程数量。</summary>
    public string SummaryText
    {
        get { return this.summaryText; }
        private set
        {
            this.summaryText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SummaryText)));
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    public ProcessPanelViewModel(IProcessProvider processProvider, ISystemMetricsProvider metricsProvider)
    {
        this.processProvider = processProvider;
        this.metricsProvider = metricsProvider;
        this.StartRefresh();
    }

    /// <summary>在后台线程启动一次刷新，避免阻塞界面。</summary>
    public void StartRefresh()
    {
        Task.Run(new Action(this.RefreshInBackground));
    }

    /// <summary>后台采样数据，采样完成后回到界面线程更新绑定。</summary>
    private void RefreshInBackground()
    {
        this.latestSnapshot = this.metricsProvider.GetSnapshot();
        this.latestProcesses = new List<ProcessInfo>(this.processProvider.ListProcesses());
        Dispatcher.UIThread.Post(new Action(this.ApplyLatestResults));
    }

    /// <summary>在界面线程上更新摘要文字与进程列表。</summary>
    private void ApplyLatestResults()
    {
        SystemSnapshot snapshot = this.latestSnapshot ?? new SystemSnapshot();

        double memoryUsedPercent = 0;
        if (snapshot.MemoryTotalBytes > 0)
        {
            long memoryUsedBytes = snapshot.MemoryTotalBytes - snapshot.MemoryAvailableBytes;
            memoryUsedPercent = Math.Round(100.0 * memoryUsedBytes / snapshot.MemoryTotalBytes, 1);
        }

        this.SummaryText = "处理器占用 " + snapshot.CpuUsagePercent + "%，内存占用 " + memoryUsedPercent + "%，进程数 " + this.latestProcesses.Count;

        this.Rows.Clear();
        foreach (ProcessInfo info in this.latestProcesses)
        {
            ProcessRow row = new ProcessRow();
            row.ProcessId = info.ProcessId;
            row.Name = info.Name;
            row.MemoryText = (info.MemoryBytes / 1024.0 / 1024.0).ToString("F1") + " MB";
            row.WindowTitle = info.WindowTitle;
            this.Rows.Add(row);
        }
    }
}

/// <summary>进程列表中的一行显示数据。</summary>
public class ProcessRow
{
    /// <summary>进程编号。</summary>
    public int ProcessId { get; set; }

    /// <summary>进程名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>内存占用的显示文字。</summary>
    public string MemoryText { get; set; } = string.Empty;

    /// <summary>主窗口标题。</summary>
    public string WindowTitle { get; set; } = string.Empty;
}
