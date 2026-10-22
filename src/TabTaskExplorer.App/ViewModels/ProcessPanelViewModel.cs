using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 进程监控标签：系统资源摘要 + 进程树列表 + 每进程处理器占用 + 详情与优先级 / 亲和性调节 + 资源走势图。
/// 只有处在台前（被选中的标签）时才周期刷新，切走立即停止，保持最小资源占用。
/// </summary>
public class ProcessPanelViewModel : INotifyPropertyChanged
{
    /// <summary>走势图保留的采样点数：每 2 秒一个点，约两分钟的历史。</summary>
    private const int TrendCapacity = 60;

    private readonly ProcessSampleService sampleService;
    private readonly IProcessProvider processProvider;
    private readonly IProcessDetailService detailService;
    private readonly ProcessCpuUsageTracker cpuUsageTracker = new ProcessCpuUsageTracker();
    private readonly DispatcherTimer refreshTimer;

    /// <summary>走势图缓冲：系统处理器占用。</summary>
    private readonly List<double> systemCpuTrend = new List<double>();

    /// <summary>走势图缓冲：系统内存占用。</summary>
    private readonly List<double> systemMemoryTrend = new List<double>();

    /// <summary>走势图缓冲：当前选中进程的处理器占用（未选中时不追加）。</summary>
    private readonly List<double> selectedCpuTrend = new List<double>();

    private string resourceText = "正在加载系统资源……";
    private string summaryText = string.Empty;
    private string detailText = "未选中进程";
    private string affinityInput = string.Empty;
    private int selectedPriorityIndex = 2;
    private string sortField = string.Empty;
    private bool sortAscending = true;
    private int detailRequestVersion;
    private DateTime trendUpdatedAt;
    private ProcessRow? selectedRow;
    private bool isForeground;
    private bool closed;
    private bool refreshing;
    private bool heartbeatMode;
    private long lastVisibleTimestamp;

    /// <summary>界面绑定的进程列表（按进程树层级顺序排列）。</summary>
    public ObservableCollection<ProcessRow> Rows { get; } = new ObservableCollection<ProcessRow>();

    /// <summary>标签标题（标签条与网页端镜像共用）。</summary>
    public string Title
    {
        get { return "进程监控"; }
    }

    /// <summary>顶部资源摘要：处理器占用、内存占用与进程数量。</summary>
    public string ResourceText
    {
        get { return this.resourceText; }
        private set
        {
            this.resourceText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResourceText)));
        }
    }

    /// <summary>底部提示文字：刷新结果、结束进程与调节结果。</summary>
    public string SummaryText
    {
        get { return this.summaryText; }
        private set
        {
            this.summaryText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SummaryText)));
        }
    }

    /// <summary>选中进程的详情文字：路径、命令行、启动时间、所有者、优先级与亲和性。</summary>
    public string DetailText
    {
        get { return this.detailText; }
        private set
        {
            this.detailText = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailText)));
        }
    }

    /// <summary>亲和性输入框内容：当前选中进程的核心编号列表（形如 0,1,2），读不到时为空。
    /// 输入框双向绑定需要公开的写入端。</summary>
    public string AffinityInput
    {
        get { return this.affinityInput; }
        set
        {
            this.affinityInput = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AffinityInput)));
        }
    }

    /// <summary>优先级下拉框当前档位（0 低 / 1 低于标准 / 2 标准 / 3 高于标准 / 4 高 / 5 实时）。
    /// 下拉框双向绑定需要公开的写入端。</summary>
    public int SelectedPriorityIndex
    {
        get { return this.selectedPriorityIndex; }
        set
        {
            this.selectedPriorityIndex = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedPriorityIndex)));
        }
    }

    /// <summary>当前平台是否支持进程详情查询与优先级 / 亲和性调节（其他平台为 false，调节行整体禁用）。</summary>
    public bool IsDetailSupported
    {
        get { return this.detailService.IsSupported; }
    }

    /// <summary>当前排序字段：空为默认进程树顺序，Cpu / Name / Memory / ProcessId / WindowTitle。</summary>
    public string SortField
    {
        get { return this.sortField; }
    }

    /// <summary>排序方向是否为升序（降序时表头显示向下箭头）。</summary>
    public bool SortAscending
    {
        get { return this.sortAscending; }
    }

    /// <summary>走势图数据变化时间：每次追加或清空走势图数据时更新，图表控件订阅属性变化后重绘。</summary>
    public DateTime TrendUpdatedAt
    {
        get { return this.trendUpdatedAt; }
    }

    /// <summary>走势图数据：系统处理器占用（最近 60 个采样点，0 到 100）。</summary>
    public IReadOnlyList<double> SystemCpuTrend
    {
        get { return this.systemCpuTrend; }
    }

    /// <summary>走势图数据：系统内存占用（最近 60 个采样点，0 到 100）。</summary>
    public IReadOnlyList<double> SystemMemoryTrend
    {
        get { return this.systemMemoryTrend; }
    }

    /// <summary>走势图数据：当前选中进程的处理器占用（最近 60 个采样点，0 到 100）。</summary>
    public IReadOnlyList<double> SelectedCpuTrend
    {
        get { return this.selectedCpuTrend; }
    }

    /// <summary>当前选中的进程行（结束进程与调节按钮使用）。</summary>
    public ProcessRow? SelectedRow
    {
        get { return this.selectedRow; }
        set
        {
            int previousProcessId = this.selectedRow == null ? 0 : this.selectedRow.ProcessId;
            this.selectedRow = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRow)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedRow)));
            int currentProcessId = value == null ? 0 : value.ProcessId;
            if (currentProcessId != previousProcessId)
            {
                // 选中进程变了：清空选中进程的走势历史并重新读取详情
                this.selectedCpuTrend.Clear();
                this.RaiseTrendChanged();
                this.LoadDetail();
            }
        }
    }

    /// <summary>是否已选中一个进程（决定结束进程按钮是否可用）。</summary>
    public bool HasSelectedRow
    {
        get { return this.selectedRow != null; }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    public ProcessPanelViewModel(
        ProcessSampleService sampleService,
        IProcessProvider processProvider,
        IProcessDetailService detailService)
    {
        this.sampleService = sampleService;
        this.processProvider = processProvider;
        this.detailService = detailService;
        // 显式绑定界面线程的调度器：网页端独立工作区在 HTTP 线程上构造本类，
        // 无参构造会绑定到创建线程的调度器（无人泵动），周期刷新将永远不会执行
        this.refreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.UIThread);
        this.refreshTimer.Interval = TimeSpan.FromMilliseconds(2000);
        this.refreshTimer.Tick += this.OnRefreshTimerTick;
        // 新开标签先显示共享的进程读取记录，避免一打开就整表重新读取，随后由后台采样更新
        ProcessSample? sharedSample = this.sampleService.GetCached();
        if (sharedSample != null)
        {
            this.ApplySample(sharedSample);
            UiLogger.Log("进程监控", "打开标签：直接显示共享的进程读取记录 进程数=" + sharedSample.Processes.Count
                + " 采样时间=" + sharedSample.TakenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff")
                + " 详情调节支持=" + this.detailService.IsSupported);
        }
        else
        {
            UiLogger.Log("进程监控", "打开标签：暂无共享的进程读取记录，将执行首次采样 详情调节支持=" + this.detailService.IsSupported);
        }
        this.StartRefresh();
    }

    /// <summary>
    /// 网页端心跳：页面仍在轮询时由快照构建调用，刷新可见时间戳；
    /// 超过 15 秒没有心跳说明标签页已关闭，周期刷新自动停止。桌面端从不调用。
    /// </summary>
    public void NotifyVisible()
    {
        this.lastVisibleTimestamp = Stopwatch.GetTimestamp();
        this.heartbeatMode = true;
    }

    /// <summary>
    /// 台前台后切换：进入台前立即刷新为最新状态并启动周期刷新；离开台前停止周期刷新。
    /// </summary>
    /// <param name="foreground">是否进入台前。</param>
    public void SetForeground(bool foreground)
    {
        if (this.closed || this.isForeground == foreground)
        {
            return;
        }
        this.isForeground = foreground;
        if (foreground)
        {
            if (this.heartbeatMode)
            {
                this.lastVisibleTimestamp = Stopwatch.GetTimestamp();
            }
            UiLogger.Log("进程监控", "进入台前：启动周期刷新 间隔=" + this.refreshTimer.Interval.TotalMilliseconds + " 毫秒");
            this.StartRefresh();
            this.refreshTimer.Start();
        }
        else
        {
            UiLogger.Log("进程监控", "离开台前：停止周期刷新");
            this.refreshTimer.Stop();
        }
    }

    /// <summary>开始刷新：在后台线程采样一次系统资源与进程列表，避免阻塞界面。</summary>
    public void StartRefresh()
    {
        if (this.closed || this.refreshing)
        {
            return;
        }
        this.refreshing = true;
        Task.Run(new Action(this.RefreshInBackground));
    }

    /// <summary>点击表头排序：重复点击同一字段切换升序降序，点击新字段按升序排列。
    /// 只调换同级子树的位置，父子结构与缩进层级保持不变，选中行保持不变。</summary>
    /// <param name="field">排序字段：Cpu / Name / Memory / ProcessId / WindowTitle。</param>
    public void SetSortField(string field)
    {
        if (this.sortField == field)
        {
            this.sortAscending = !this.sortAscending;
        }
        else
        {
            this.sortField = field;
            this.sortAscending = true;
        }
        this.ReorderRowsInPlace();
        UiLogger.Log("进程监控", "点击表头排序：排序字段=" + field
            + " 排序方向=" + (this.sortAscending ? "升序" : "降序")
            + " 进程行数=" + this.Rows.Count);
    }

    /// <summary>按当前排序字段把界面行列表原地重排（逐位置交换，不增删行，选中状态不受影响）。</summary>
    private void ReorderRowsInPlace()
    {
        List<ProcessRow> sorted = new List<ProcessRow>(this.Rows);
        TreeRowSorter.SortSameLevel<ProcessRow>(sorted, ProcessPanelViewModel.SelectRowLevel, this.CompareRows);
        for (int index = 0; index < sorted.Count && index < this.Rows.Count; index++)
        {
            if (object.ReferenceEquals(this.Rows[index], sorted[index]))
            {
                continue;
            }
            for (int position = index + 1; position < this.Rows.Count; position++)
            {
                if (object.ReferenceEquals(this.Rows[position], sorted[index]))
                {
                    this.Rows.Move(position, index);
                    break;
                }
            }
        }
    }

    /// <summary>同级进程行比较器：按当前排序字段比较，降序时结果取反。</summary>
    /// <param name="left">左行。</param>
    /// <param name="right">右行。</param>
    /// <returns>小于 0 / 等于 0 / 大于 0。</returns>
    private int CompareRows(ProcessRow left, ProcessRow right)
    {
        int result;
        if (this.sortField == "Cpu")
        {
            result = left.CpuPercent.CompareTo(right.CpuPercent);
        }
        else if (this.sortField == "Memory")
        {
            result = left.MemoryBytes.CompareTo(right.MemoryBytes);
        }
        else if (this.sortField == "ProcessId")
        {
            result = left.ProcessId.CompareTo(right.ProcessId);
        }
        else if (this.sortField == "WindowTitle")
        {
            result = string.Compare(left.WindowTitle, right.WindowTitle, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            result = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        }
        if (!this.sortAscending)
        {
            result = -result;
        }
        return result;
    }

    /// <summary>取出行的树形层级（树形同级排序器使用）。</summary>
    /// <param name="row">一行进程数据。</param>
    /// <returns>树形层级，0 为顶层。</returns>
    private static int SelectRowLevel(ProcessRow row)
    {
        return row.Level;
    }

    /// <summary>按进程编号选中一行（网页端点选与结束进程共用；找不到时清空选中）。</summary>
    public void SelectProcessById(int processId)
    {
        ProcessRow? matched = null;
        foreach (ProcessRow row in this.Rows)
        {
            if (row.ProcessId == processId)
            {
                matched = row;
                break;
            }
        }
        this.SelectedRow = matched;
        if (matched == null)
        {
            UiLogger.Log("进程监控", "按编号选中进程：进程编号=" + processId + " 结果=列表中不存在该进程");
        }
    }

    /// <summary>按进程编号结束进程（网页端结束进程按钮使用）。</summary>
    public void TerminateByProcessId(int processId)
    {
        this.SelectProcessById(processId);
        this.TerminateSelected();
    }

    /// <summary>结束当前选中的进程，随后立即刷新一次列表。</summary>
    public void TerminateSelected()
    {
        ProcessRow? row = this.selectedRow;
        if (row == null)
        {
            this.SummaryText = "请先在列表中选中一个进程";
            UiLogger.Log("进程监控", "结束进程：未选中任何进程，本次操作取消");
            return;
        }
        int processId = row.ProcessId;
        string processName = row.Name;
        this.SummaryText = "正在结束进程：" + processName + "（进程编号 " + processId + "）";
        Task.Run(new Action(delegate { this.TerminateInBackground(processId, processName); }));
    }

    /// <summary>给当前选中的进程设置优先级档位（桌面端优先级下拉框的"应用"按钮使用）。</summary>
    /// <param name="priorityIndex">优先级档位下标（0 到 5）。</param>
    public void ApplyPrioritySelected(int priorityIndex)
    {
        ProcessRow? row = this.selectedRow;
        if (row == null)
        {
            this.SummaryText = "请先在列表中选中一个进程";
            UiLogger.Log("进程监控", "调整优先级：未选中任何进程，本次操作取消 档位下标=" + priorityIndex);
            return;
        }
        this.ApplyPriorityByProcessId(row.ProcessId, priorityIndex);
    }

    /// <summary>按进程编号设置优先级档位（网页端调节按钮与桌面端共用）。</summary>
    /// <param name="processId">进程编号。</param>
    /// <param name="priorityIndex">优先级档位下标（0 到 5）。</param>
    public void ApplyPriorityByProcessId(int processId, int priorityIndex)
    {
        if (priorityIndex < 0 || priorityIndex > 5)
        {
            this.SummaryText = "优先级档位无效：" + priorityIndex;
            UiLogger.Log("进程监控", "调整优先级：档位下标超出范围 进程编号=" + processId + " 档位下标=" + priorityIndex);
            return;
        }
        if (!this.detailService.IsSupported)
        {
            this.SummaryText = "当前平台不支持调整优先级";
            UiLogger.Log("进程监控", "调整优先级：当前平台不支持 进程编号=" + processId + " 档位下标=" + priorityIndex);
            return;
        }
        ProcessPriorityLevel level = (ProcessPriorityLevel)priorityIndex;
        string levelName = ProcessPanelViewModel.PriorityLevelName(level);
        this.SummaryText = "正在设置优先级：" + levelName + "（进程编号 " + processId + "）";
        UiLogger.Log("进程监控", "调整优先级：进程编号=" + processId + " 档位下标=" + priorityIndex + " 目标级别=" + levelName);
        Task.Run(new Action(delegate { this.SetPriorityInBackground(processId, level, levelName); }));
    }

    /// <summary>给当前选中的进程设置 CPU 亲和性（桌面端核心编号输入框的"应用"按钮使用）。</summary>
    /// <param name="processorIndexesText">核心编号列表文本，形如 0,1,3。</param>
    public void ApplyAffinitySelected(string processorIndexesText)
    {
        ProcessRow? row = this.selectedRow;
        if (row == null)
        {
            this.SummaryText = "请先在列表中选中一个进程";
            UiLogger.Log("进程监控", "调整处理器亲和性：未选中任何进程，本次操作取消 输入=" + processorIndexesText);
            return;
        }
        this.ApplyAffinityByProcessId(row.ProcessId, processorIndexesText);
    }

    /// <summary>按进程编号设置 CPU 亲和性（网页端调节按钮与桌面端共用）。</summary>
    /// <param name="processId">进程编号。</param>
    /// <param name="processorIndexesText">核心编号列表文本，形如 0,1,3。</param>
    public void ApplyAffinityByProcessId(int processId, string processorIndexesText)
    {
        if (!this.detailService.IsSupported)
        {
            this.SummaryText = "当前平台不支持调整处理器亲和性";
            UiLogger.Log("进程监控", "调整处理器亲和性：当前平台不支持 进程编号=" + processId + " 输入=" + processorIndexesText);
            return;
        }
        int processorCount = Environment.ProcessorCount;
        List<int> processorIndexes = AffinityMaskUtility.ParseIndexes(processorIndexesText);
        long affinityMask = AffinityMaskUtility.ToMask(processorIndexes, processorCount);
        if (affinityMask == AffinityMaskUtility.UnsupportedMask)
        {
            this.SummaryText = "核心编号无效：请填写 0 到 " + (processorCount - 1) + " 之间的编号，用逗号分隔（本机共 " + processorCount + " 个逻辑核心）";
            UiLogger.Log("进程监控", "调整处理器亲和性：核心编号无效 进程编号=" + processId
                + " 输入=" + processorIndexesText + " 核心总数=" + processorCount);
            return;
        }
        this.SummaryText = "正在设置处理器亲和性：核心 " + AffinityMaskUtility.ToText(affinityMask, processorCount)
            + "（进程编号 " + processId + "）";
        UiLogger.Log("进程监控", "调整处理器亲和性：进程编号=" + processId
            + " 核心编号列表=" + processorIndexesText + " 核心总数=" + processorCount + " 掩码=" + affinityMask);
        Task.Run(new Action(delegate { this.SetAffinityInBackground(processId, affinityMask, processorCount); }));
    }

    /// <summary>关闭标签时调用：停止周期刷新并禁止后续采样。</summary>
    public void Close()
    {
        if (this.closed)
        {
            return;
        }
        this.closed = true;
        this.refreshTimer.Stop();
        UiLogger.Log("进程监控", "关闭标签：已停止周期刷新");
    }

    /// <summary>周期刷新到点：先判网页端心跳超时，再在台前且未采样时启动一次新采样。</summary>
    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!this.isForeground || this.closed)
        {
            return;
        }
        if (this.heartbeatMode
            && Stopwatch.GetTimestamp() - this.lastVisibleTimestamp > Stopwatch.Frequency * 15)
        {
            UiLogger.Log("进程监控", "网页端心跳超时（标签页可能已关闭），自动进入台后并停止周期刷新");
            this.SetForeground(false);
            return;
        }
        this.StartRefresh();
    }

    /// <summary>后台读取共享的进程采样，读取完成后回到界面线程更新绑定。</summary>
    private void RefreshInBackground()
    {
        try
        {
            ProcessSample sample = this.sampleService.ReadShared();
            Dispatcher.UIThread.Post(new Action(delegate { this.ApplySample(sample); }));
        }
        catch (Exception error)
        {
            Dispatcher.UIThread.Post(new Action(delegate { this.OnRefreshFailed(error); }));
        }
    }

    /// <summary>后台线程结束指定进程，结果回到界面线程提示。</summary>
    private void TerminateInBackground(int processId, string processName)
    {
        bool success;
        try
        {
            success = this.processProvider.Terminate(processId);
        }
        catch (Exception)
        {
            success = false;
        }
        Dispatcher.UIThread.Post(new Action(delegate { this.OnTerminateFinished(processId, processName, success); }));
    }

    /// <summary>后台线程设置优先级，结果回到界面线程提示并刷新详情。</summary>
    private void SetPriorityInBackground(int processId, ProcessPriorityLevel level, string levelName)
    {
        bool success;
        try
        {
            success = this.detailService.SetPriorityLevel(processId, level);
        }
        catch (Exception)
        {
            success = false;
        }
        Dispatcher.UIThread.Post(new Action(delegate
        {
            if (success)
            {
                this.SummaryText = "已设置优先级：" + levelName + "（进程编号 " + processId + "）";
                UiLogger.Log("进程监控", "调整优先级成功：进程编号=" + processId + " 级别=" + levelName);
            }
            else
            {
                this.SummaryText = "设置优先级失败：" + levelName + "（进程编号 " + processId + "，进程不存在或没有权限）";
                UiLogger.Log("进程监控", "调整优先级失败：进程编号=" + processId + " 级别=" + levelName + " 原因=进程不存在或没有权限");
            }
            this.LoadDetail();
        }));
    }

    /// <summary>后台线程设置 CPU 亲和性，结果回到界面线程提示并刷新详情。</summary>
    private void SetAffinityInBackground(int processId, long affinityMask, int processorCount)
    {
        bool success;
        try
        {
            success = this.detailService.SetProcessorAffinity(processId, affinityMask);
        }
        catch (Exception)
        {
            success = false;
        }
        Dispatcher.UIThread.Post(new Action(delegate
        {
            string maskText = AffinityMaskUtility.ToText(affinityMask, processorCount);
            if (success)
            {
                this.SummaryText = "已设置处理器亲和性：核心 " + maskText + "（进程编号 " + processId + "）";
                UiLogger.Log("进程监控", "调整处理器亲和性成功：进程编号=" + processId + " 掩码=" + affinityMask + " 核心=" + maskText);
            }
            else
            {
                this.SummaryText = "设置处理器亲和性失败：核心 " + maskText + "（进程编号 " + processId + "，进程不存在或没有权限）";
                UiLogger.Log("进程监控", "调整处理器亲和性失败：进程编号=" + processId + " 掩码=" + affinityMask
                    + " 核心=" + maskText + " 原因=进程不存在或没有权限");
            }
            this.LoadDetail();
        }));
    }

    /// <summary>采样失败：恢复可刷新状态并提示。</summary>
    private void OnRefreshFailed(Exception error)
    {
        this.refreshing = false;
        this.ResourceText = "系统资源采样失败：" + error.Message;
        UiLogger.Log("进程监控", "采样失败：" + error.Message);
    }

    /// <summary>结束进程结果回界面线程：记录日志、提示并刷新列表。</summary>
    private void OnTerminateFinished(int processId, string processName, bool success)
    {
        if (success)
        {
            this.SummaryText = "已结束进程：" + processName + "（进程编号 " + processId + "）";
            UiLogger.Log("进程监控", "结束进程成功：进程编号=" + processId + " 进程名称=" + processName);
            // 作废共享的进程读取记录：立刻重新采样，界面马上看不到已结束的进程
            this.sampleService.Invalidate();
        }
        else
        {
            this.SummaryText = "结束进程失败：" + processName + "（进程编号 " + processId + "，进程不存在或没有权限）";
            UiLogger.Log("进程监控", "结束进程失败：进程编号=" + processId + " 进程名称=" + processName + " 原因=进程不存在或没有权限");
        }
        this.refreshing = false;
        this.StartRefresh();
    }

    /// <summary>
    /// 在界面线程上用一份共享采样更新资源摘要、进程树列表与走势图缓冲。
    /// </summary>
    /// <param name="sample">共享的进程读取记录（进程列表只读，这里只转换成显示行）。</param>
    private void ApplySample(ProcessSample sample)
    {
        SystemSnapshot snapshot = sample.Snapshot;

        double memoryUsedPercent = 0;
        if (snapshot.MemoryTotalBytes > 0)
        {
            long memoryUsedBytes = snapshot.MemoryTotalBytes - snapshot.MemoryAvailableBytes;
            memoryUsedPercent = Math.Round(100.0 * memoryUsedBytes / snapshot.MemoryTotalBytes, 1);
        }

        this.ResourceText = "处理器占用 " + snapshot.CpuUsagePercent + "%，内存占用 " + memoryUsedPercent + "%，进程数 " + sample.Processes.Count;

        // 每进程处理器占用：相邻两次采样的累计处理器时间差分（首轮只有基线，显示占位横杠）
        IReadOnlyDictionary<int, double> cpuUsageByProcessId =
            this.cpuUsageTracker.Update(sample.Processes, snapshot.SampleTimeUtc, Environment.ProcessorCount);

        // 记住当前选中的进程编号，刷新后同编号的行继续保持选中，避免周期刷新打断操作；
        // 尚未选中任何行时保持不选中，避免把进程编号为 0 的系统空闲行误判为选中
        int? keepProcessId = null;
        if (this.selectedRow != null)
        {
            keepProcessId = this.selectedRow.ProcessId;
        }

        this.Rows.Clear();
        ProcessRow? restoredRow = null;
        List<ProcessRow> builtRows = new List<ProcessRow>();
        IReadOnlyList<ProcessTreeRow> treeRows = ProcessTreeBuilder.Build(sample.Processes);
        foreach (ProcessTreeRow treeRow in treeRows)
        {
            ProcessInfo info = treeRow.Process;
            ProcessRow row = new ProcessRow();
            row.ProcessId = info.ProcessId;
            row.Name = info.Name;
            row.Level = treeRow.Depth < 0 ? 0 : treeRow.Depth;
            row.IndentText = new string('　', treeRow.Depth < 0 ? 0 : treeRow.Depth);
            double cpuPercent;
            bool hasCpuUsage = cpuUsageByProcessId.TryGetValue(info.ProcessId, out cpuPercent);
            row.CpuPercent = hasCpuUsage ? cpuPercent : -1;
            row.CpuText = hasCpuUsage ? cpuPercent + "%" : "—";
            row.MemoryBytes = info.MemoryBytes;
            row.MemoryText = (info.MemoryBytes / 1024.0 / 1024.0).ToString("F1") + " MB";
            row.WindowTitle = info.WindowTitle;
            builtRows.Add(row);
            if (keepProcessId.HasValue && info.ProcessId == keepProcessId.Value)
            {
                restoredRow = row;
            }
        }
        if (this.sortField.Length > 0)
        {
            // 用户点了表头：每轮刷新都按同一排序字段重建同级顺序
            TreeRowSorter.SortSameLevel<ProcessRow>(builtRows, ProcessPanelViewModel.SelectRowLevel, this.CompareRows);
        }
        foreach (ProcessRow row in builtRows)
        {
            this.Rows.Add(row);
        }
        this.SelectedRow = restoredRow;
        this.refreshing = false;
        this.AppendTrend(sample, cpuUsageByProcessId, memoryUsedPercent);
    }

    /// <summary>把本轮的系统与选中进程占用追加进走势图缓冲，并通知图表重绘。</summary>
    private void AppendTrend(
        ProcessSample sample,
        IReadOnlyDictionary<int, double> cpuUsageByProcessId,
        double memoryUsedPercent)
    {
        ProcessPanelViewModel.AddTrendPoint(this.systemCpuTrend, sample.Snapshot.CpuUsagePercent);
        ProcessPanelViewModel.AddTrendPoint(this.systemMemoryTrend, memoryUsedPercent);
        ProcessRow? row = this.selectedRow;
        if (row != null)
        {
            double selectedPercent;
            if (!cpuUsageByProcessId.TryGetValue(row.ProcessId, out selectedPercent))
            {
                // 选中进程本轮还没有占用结果（刚出现的新进程），按 0 记点，下一轮给出真实值
                selectedPercent = 0;
            }
            ProcessPanelViewModel.AddTrendPoint(this.selectedCpuTrend, selectedPercent);
        }
        this.RaiseTrendChanged();
    }

    /// <summary>向走势图缓冲追加一个点，并按容量顶出最旧的点。</summary>
    private static void AddTrendPoint(List<double> buffer, double value)
    {
        buffer.Add(value);
        while (buffer.Count > TrendCapacity)
        {
            buffer.RemoveAt(0);
        }
    }

    /// <summary>走势图数据变化：更新时间戳属性，图表控件收到属性变化后重绘。</summary>
    private void RaiseTrendChanged()
    {
        this.trendUpdatedAt = DateTime.Now;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TrendUpdatedAt)));
    }

    /// <summary>按当前选中行读取进程详情（选中变化、调节完成、行刷新后都会调用）。</summary>
    private void LoadDetail()
    {
        this.detailRequestVersion++;
        ProcessRow? row = this.selectedRow;
        if (row == null)
        {
            this.DetailText = "未选中进程";
            this.AffinityInput = string.Empty;
            this.SelectedPriorityIndex = 2;
            return;
        }
        if (!this.detailService.IsSupported)
        {
            this.DetailText = "当前平台不支持进程详情查询与调节";
            this.AffinityInput = string.Empty;
            UiLogger.Log("进程监控", "读取进程详情：当前平台不支持 进程编号=" + row.ProcessId);
            return;
        }
        int processId = row.ProcessId;
        string processName = row.Name;
        int requestVersion = this.detailRequestVersion;
        this.DetailText = "正在读取进程详情：进程编号 " + processId + "（" + processName + "）……";
        UiLogger.Log("进程监控", "读取进程详情：进程编号=" + processId + " 进程名称=" + processName);
        Task.Run(new Action(delegate { this.LoadDetailInBackground(requestVersion, processId, processName); }));
    }

    /// <summary>后台线程读取进程详情，完成后回到界面线程回填（过期结果直接丢弃）。</summary>
    private void LoadDetailInBackground(int requestVersion, int processId, string processName)
    {
        ProcessDetail? detail = null;
        try
        {
            detail = this.detailService.GetDetail(processId);
        }
        catch (Exception)
        {
            detail = null;
        }
        string detailText = ProcessPanelViewModel.FormatDetail(detail, processId, processName);
        Dispatcher.UIThread.Post(new Action(delegate { this.OnDetailLoaded(requestVersion, detail, detailText); }));
    }

    /// <summary>详情回填到界面线程：只接受最新一次请求的结果，并同步优先级下拉框与亲和性输入框。</summary>
    private void OnDetailLoaded(int requestVersion, ProcessDetail? detail, string detailText)
    {
        if (requestVersion != this.detailRequestVersion)
        {
            // 已有更新的详情请求（选中行又变了），本次结果作废
            return;
        }
        this.DetailText = detailText;
        if (detail == null)
        {
            return;
        }
        if (detail.PriorityLevel.HasValue)
        {
            this.SelectedPriorityIndex = (int)detail.PriorityLevel.Value;
        }
        if (detail.AffinityMask == AffinityMaskUtility.UnsupportedMask)
        {
            this.AffinityInput = string.Empty;
        }
        else
        {
            this.AffinityInput = ProcessPanelViewModel.FormatAffinityInput(detail.AffinityMask, Environment.ProcessorCount);
        }
    }

    /// <summary>格式化进程详情文字：路径、命令行、启动时间、所有者、优先级与亲和性，读不到的项写"未知 / 不支持"。</summary>
    private static string FormatDetail(ProcessDetail? detail, int processId, string processName)
    {
        if (detail == null)
        {
            return "读取进程详情失败：进程不存在或没有权限（进程编号 " + processId + "，名称 " + processName + "）";
        }
        string executablePath = string.IsNullOrEmpty(detail.ExecutablePath) ? "未知" : detail.ExecutablePath;
        string commandLine = string.IsNullOrEmpty(detail.CommandLine) ? "未知" : detail.CommandLine;
        string startTime = string.IsNullOrEmpty(detail.StartTimeText) ? "未知" : detail.StartTimeText;
        string userName = string.IsNullOrEmpty(detail.UserName) ? "未知" : detail.UserName;
        string priorityName = detail.PriorityLevel.HasValue
            ? ProcessPanelViewModel.PriorityLevelName(detail.PriorityLevel.Value)
            : "未知";
        string affinityText = AffinityMaskUtility.ToText(detail.AffinityMask, Environment.ProcessorCount);
        return "可执行文件：" + executablePath
            + "\n启动命令行：" + commandLine
            + "\n启动时间：" + startTime
            + "\n进程所有者：" + userName
            + "\n优先级：" + priorityName
            + "\nCPU 亲和性：" + affinityText;
    }

    /// <summary>把亲和性位掩码转成输入框可用的核心编号列表文本（形如 0,1,2）；掩码无效时为空字符串。</summary>
    private static string FormatAffinityInput(long affinityMask, int processorCount)
    {
        if (affinityMask == AffinityMaskUtility.UnsupportedMask || affinityMask == 0 || processorCount <= 0)
        {
            return string.Empty;
        }
        string result = string.Empty;
        int limit = processorCount > 64 ? 64 : processorCount;
        for (int processorIndex = 0; processorIndex < limit; processorIndex++)
        {
            if ((affinityMask & (1L << processorIndex)) == 0)
            {
                continue;
            }
            if (result.Length > 0)
            {
                result = result + ",";
            }
            result = result + processorIndex;
        }
        return result;
    }

    /// <summary>优先级档位的显示名称（与优先级下拉框的选项文字一致）。</summary>
    public static string PriorityLevelName(ProcessPriorityLevel level)
    {
        if (level == ProcessPriorityLevel.Idle)
        {
            return "低";
        }
        if (level == ProcessPriorityLevel.BelowNormal)
        {
            return "低于标准";
        }
        if (level == ProcessPriorityLevel.Normal)
        {
            return "标准";
        }
        if (level == ProcessPriorityLevel.AboveNormal)
        {
            return "高于标准";
        }
        if (level == ProcessPriorityLevel.High)
        {
            return "高";
        }
        if (level == ProcessPriorityLevel.Realtime)
        {
            return "实时";
        }
        return "未知";
    }

    /// <summary>描述当前选中的进程（供界面操作日志使用）："进程编号 X（名称 Y）" 或 "未选中进程"。</summary>
    public static string DescribeSelected(ProcessPanelViewModel viewModel)
    {
        ProcessRow? row = viewModel.selectedRow;
        if (row == null)
        {
            return "未选中进程";
        }
        return "进程编号 " + row.ProcessId + "（" + row.Name + "）";
    }
}

/// <summary>进程列表中的一行显示数据（树形缩进 + 每进程处理器占用 + 内存 + 窗口标题）。</summary>
public class ProcessRow
{
    /// <summary>进程编号。</summary>
    public int ProcessId { get; set; }

    /// <summary>树形层级：0 为顶层，每层加 1（树形同级排序器使用）。</summary>
    public int Level { get; set; }

    /// <summary>树形缩进前缀（每层一个全角空格，顶层为空）。</summary>
    public string IndentText { get; set; } = string.Empty;

    /// <summary>显示名称：树形缩进 + 进程名称（行每次刷新整体重建，无需变更通知）。</summary>
    public string DisplayName
    {
        get { return this.IndentText + this.Name; }
    }

    /// <summary>进程名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>本进程处理器占用的显示文字（首轮采样前为占位横杠）。</summary>
    public string CpuText { get; set; } = "—";

    /// <summary>本进程处理器占用百分比（首轮采样前为 -1，排序使用）。</summary>
    public double CpuPercent { get; set; } = -1;

    /// <summary>内存占用字节数（排序使用）。</summary>
    public long MemoryBytes { get; set; }

    /// <summary>内存占用的显示文字。</summary>
    public string MemoryText { get; set; } = string.Empty;

    /// <summary>主窗口标题。</summary>
    public string WindowTitle { get; set; } = string.Empty;
}
