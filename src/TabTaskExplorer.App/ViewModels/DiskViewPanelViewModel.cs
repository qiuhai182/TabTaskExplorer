using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 磁盘视图面板：展示指定根目录下条目的磁盘占用统计。
/// 构造时不扫描，只有进入台前后才请求扫描并启动周期探测（约每 5 秒比较一次目录修改时间）；
/// 离开台前立即停止全部扫描与探测。文件夹只支持原地展开或收起，不再进入下一级。
/// </summary>
public class DiskViewPanelViewModel : INotifyPropertyChanged
{
    private readonly IDiskUsageProvider diskUsageProvider;
    private readonly DiskScanService scanService;
    private readonly string ownerId;
    private readonly Timer probeTimer;
    private readonly HashSet<string> pendingProbePaths = new HashSet<string>();

    private string summaryText = "正在加载磁盘视图……";
    private string rootPathInput = string.Empty;
    private string subscribedRoot = string.Empty;
    private string sortField = string.Empty;
    private bool sortAscending = true;
    private bool isForeground;
    private bool closed;
    private bool heartbeatMode;
    private long lastVisibleTimestamp;
    private CancellationTokenSource expandCancellationSource = new CancellationTokenSource();
    private string lastTogglePath = string.Empty;
    private long lastToggleTimestamp;

    /// <summary>构造函数：注入磁盘用量统计服务与共享扫描服务；构造时不做任何扫描。</summary>
    /// <param name="diskUsageProvider">平台磁盘用量提供者。</param>
    /// <param name="scanService">共享磁盘扫描服务（单例）。</param>
    public DiskViewPanelViewModel(IDiskUsageProvider diskUsageProvider, DiskScanService scanService)
    {
        this.diskUsageProvider = diskUsageProvider;
        this.scanService = scanService;
        this.ownerId = "磁盘视图-" + Guid.NewGuid().ToString("N");
        this.lastVisibleTimestamp = Stopwatch.GetTimestamp();
        this.scanService.ProgressChanged += this.OnScanProgress;
        this.scanService.ScanCompleted += this.OnScanCompleted;
        this.probeTimer = new Timer(new TimerCallback(this.ProbeTick), null, Timeout.Infinite, Timeout.Infinite);
        UiLogger.Log("磁盘视图", "创建磁盘视图标签：进入台前后才会开始扫描 请求方=" + this.ownerId);
    }

    /// <summary>标签条显示的标题。</summary>
    public string Title { get; set; } = "磁盘视图";

    /// <summary>扫描结果行集合（扁平可见列表：子行紧跟在父行之后）。</summary>
    public ObservableCollection<DiskRow> Rows { get; } = new ObservableCollection<DiskRow>();

    /// <summary>当前排序字段：空为扫描服务默认顺序，Name / Type / Size / Free / Usage / FileSystem。</summary>
    public string SortField
    {
        get { return this.sortField; }
    }

    /// <summary>排序方向是否为升序（降序时表头显示向下箭头）。</summary>
    public bool SortAscending
    {
        get { return this.sortAscending; }
    }

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

    /// <summary>点击表头排序：重复点击同一字段切换升序降序，点击新字段按升序排列。
    /// 只调换同级子树的位置，树形层级与展开状态保持不变。</summary>
    /// <param name="field">排序字段：Name / Type / Size / Free / Usage / FileSystem。</param>
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
        this.ReapplySort();
        UiLogger.Log("磁盘视图", "点击表头排序：排序字段=" + field
            + " 排序方向=" + (this.sortAscending ? "升序" : "降序")
            + " 行数=" + this.Rows.Count);
    }

    /// <summary>按当前排序字段把可见行列表整体重排（同级子树整体换位，父子结构与展开状态不变）。</summary>
    private void ReapplySort()
    {
        if (this.Rows.Count <= 1)
        {
            return;
        }
        List<DiskRow> sorted = new List<DiskRow>(this.Rows);
        TreeRowSorter.SortSameLevel<DiskRow>(sorted, DiskViewPanelViewModel.SelectRowLevel, this.CompareDiskRows);
        this.Rows.Clear();
        foreach (DiskRow row in sorted)
        {
            this.Rows.Add(row);
        }
    }

    /// <summary>同级磁盘行比较器：目录始终在前（与文件列表一致），再按当前排序字段比较；
    /// 未排序时用扫描服务的默认顺序（目录在前、大小降序）。</summary>
    /// <param name="left">左行。</param>
    /// <param name="right">右行。</param>
    /// <returns>小于 0 / 等于 0 / 大于 0。</returns>
    private int CompareDiskRows(DiskRow left, DiskRow right)
    {
        if (this.sortField.Length == 0)
        {
            return DiskScanService.CompareRows(left, right);
        }
        int directoryCompare = right.IsDirectory.CompareTo(left.IsDirectory);
        if (directoryCompare != 0)
        {
            return directoryCompare;
        }
        int result;
        if (this.sortField == "Name")
        {
            result = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        }
        else if (this.sortField == "Type")
        {
            result = string.Compare(left.EntryTypeText, right.EntryTypeText, StringComparison.OrdinalIgnoreCase);
        }
        else if (this.sortField == "Size")
        {
            result = left.SizeBytes.CompareTo(right.SizeBytes);
        }
        else if (this.sortField == "Free")
        {
            result = left.FreeBytes.CompareTo(right.FreeBytes);
        }
        else if (this.sortField == "Usage")
        {
            result = left.UsagePercent.CompareTo(right.UsagePercent);
        }
        else
        {
            result = string.Compare(left.FileSystemText, right.FileSystemText, StringComparison.OrdinalIgnoreCase);
        }
        if (!this.sortAscending)
        {
            result = -result;
        }
        return result;
    }

    /// <summary>取出行的树形层级（树形同级排序器使用）。</summary>
    /// <param name="row">一行磁盘数据。</param>
    /// <returns>树形层级，0 为顶层。</returns>
    private static int SelectRowLevel(DiskRow row)
    {
        return row.Level;
    }

    /// <summary>
    /// 台前台后切换：进入台前立即刷新为最新状态并启动周期探测；
    /// 离开台前停止周期探测、释放扫描订阅并取消展开回填。
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
            string root = this.CurrentRoot();
            UiLogger.Log("磁盘视图", "进入台前：根目录=" + (root.Length == 0 ? "此电脑" : root)
                + " 请求方=" + this.ownerId);
            if (this.heartbeatMode)
            {
                this.lastVisibleTimestamp = Stopwatch.GetTimestamp();
            }
            if (root.Length == 0)
            {
                this.SummaryText = "正在加载磁盘视图……";
                this.BeginLoadDrives();
            }
            else
            {
                this.SummaryText = "正在扫描 " + root + " ……";
                this.subscribedRoot = root;
                this.scanService.RequestScan(root, this.ownerId, false, true);
            }
            this.probeTimer.Change(2000, 5000);
        }
        else
        {
            UiLogger.Log("磁盘视图", "离开台前：停止周期探测并释放扫描订阅 请求方=" + this.ownerId);
            this.probeTimer.Change(Timeout.Infinite, Timeout.Infinite);
            this.ReleaseCurrentScan();
            this.CancelExpandWork();
        }
    }

    /// <summary>
    /// 网页端心跳：页面仍在构建状态时由轮询调用，刷新可见时间戳；
    /// 超过 15 秒没有心跳说明标签页已关闭，周期探测自动停止。桌面端从不调用。
    /// </summary>
    public void NotifyVisible()
    {
        this.lastVisibleTimestamp = Stopwatch.GetTimestamp();
        this.heartbeatMode = true;
    }

    /// <summary>开始刷新：按路径输入框中的路径重新扫描。</summary>
    public void StartRefresh()
    {
        this.StartRefresh(this.rootPathInput);
    }

    /// <summary>按指定根目录刷新：空路径回到此电脑视图，否则请求增量扫描。</summary>
    /// <param name="rootPath">新的根目录路径。</param>
    public void StartRefresh(string rootPath)
    {
        this.ReloadRoot(rootPath, false);
    }

    /// <summary>完全重扫当前根目录：忽略快照与近期结果，重新统计全部目录（刷新按钮）。</summary>
    public void StartFullRefresh()
    {
        this.ReloadRoot(this.rootPathInput, true);
    }

    /// <summary>按指定根目录重新加载视图：释放旧订阅、清掉展开状态后按新模式请求扫描。</summary>
    /// <param name="rootPath">新的根目录路径。</param>
    /// <param name="forceFull">是否完全重扫。</param>
    private void ReloadRoot(string rootPath, bool forceFull)
    {
        string path = rootPath == null ? string.Empty : rootPath.Trim();
        string previousRoot = this.CurrentRoot();
        this.rootPathInput = path;
        this.OnPropertyChanged(nameof(this.RootPathInput));
        this.ReleaseCurrentScan();
        this.CancelExpandWork();
        if (path != previousRoot)
        {
            this.Rows.Clear();
        }
        if (path.Length == 0)
        {
            this.SummaryText = "正在加载磁盘视图……";
            UiLogger.Log("磁盘视图", "加载此电脑视图（仅读取驱动器容量，不进行目录扫描）");
            this.BeginLoadDrives();
            return;
        }
        this.SummaryText = forceFull ? "正在完全重扫 " + path + " ……" : "正在扫描 " + path + " ……";
        UiLogger.Log("磁盘视图", (forceFull ? "开始完全重扫：" : "开始扫描：") + "根目录=" + path);
        this.subscribedRoot = path;
        if (this.isForeground)
        {
            this.scanService.RequestScan(path, this.ownerId, forceFull, !forceFull);
        }
    }

    /// <summary>返回上一级目录；没有上级时回到此电脑视图。</summary>
    public void NavigateUp()
    {
        string parent = string.Empty;
        try
        {
            string current = this.CurrentRoot();
            if (current.Length > 0)
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

    /// <summary>点击行：目录原地展开或收起（不进入下一级），文件忽略；400 毫秒内同路径忽略重复点击。</summary>
    /// <param name="row">被点击的行。</param>
    public void ToggleRow(DiskRow? row)
    {
        if (row == null || !row.IsDirectory)
        {
            return;
        }
        long now = Stopwatch.GetTimestamp();
        if (row.FullPath == this.lastTogglePath
            && now - this.lastToggleTimestamp < Stopwatch.Frequency * 2 / 5)
        {
            return;
        }
        this.lastTogglePath = row.FullPath;
        this.lastToggleTimestamp = now;
        if (row.IsExpanded)
        {
            this.CollapseRow(row);
        }
        else
        {
            this.ExpandRow(row);
        }
    }

    /// <summary>
    /// 按路径切换展开状态（网页端导航入口）：空路径或根路径回到根视图，
    /// 其余路径在当前列表中找到对应行后原地展开或收起。
    /// </summary>
    /// <param name="path">目标路径。</param>
    public void ToggleRowByPath(string path)
    {
        string target = path == null ? string.Empty : path.Trim();
        string root = this.CurrentRoot();
        if (target.Length == 0 || target == root)
        {
            this.StartRefresh(root);
            return;
        }
        foreach (DiskRow row in this.Rows)
        {
            if (row.FullPath == target)
            {
                this.ToggleRow(row);
                return;
            }
        }
        UiLogger.Log("磁盘视图", "展开目标不在当前列表中，已忽略：路径=" + target);
    }

    /// <summary>关闭标签：退订扫描事件、停止周期探测、释放订阅并取消全部展开回填。</summary>
    public void Close()
    {
        if (this.closed)
        {
            return;
        }
        this.closed = true;
        this.scanService.ProgressChanged -= this.OnScanProgress;
        this.scanService.ScanCompleted -= this.OnScanCompleted;
        this.probeTimer.Change(Timeout.Infinite, Timeout.Infinite);
        this.probeTimer.Dispose();
        this.ReleaseCurrentScan();
        this.CancelExpandWork();
        UiLogger.Log("磁盘视图", "关闭磁盘视图标签，已停止探测与扫描 请求方=" + this.ownerId);
    }

    /// <summary>周期探测回调（线程池线程）：先做网页端心跳超时判定，再投递到界面线程探测。</summary>
    /// <param name="state">计时器状态（未使用）。</param>
    private void ProbeTick(object? state)
    {
        if (this.closed || !this.isForeground)
        {
            return;
        }
        if (this.heartbeatMode
            && Stopwatch.GetTimestamp() - this.lastVisibleTimestamp > Stopwatch.Frequency * 15)
        {
            Dispatcher.UIThread.Post(new Action(this.StopFromHeartbeatTimeout));
            return;
        }
        Dispatcher.UIThread.Post(new Action(this.ProbeOnUIThread));
    }

    /// <summary>网页端心跳超时：自动回到台后，停止全部扫描。</summary>
    private void StopFromHeartbeatTimeout()
    {
        UiLogger.Log("磁盘视图", "网页端心跳超时（标签页可能已关闭），自动进入台后并停止扫描 请求方=" + this.ownerId);
        this.SetForeground(false);
    }

    /// <summary>
    /// 界面线程上的台前探测：此电脑视图直接刷新驱动器容量；
    /// 根目录一级有变化时请求增量扫描，其余可见目录有变化时只重算该目录自身或重新展开。
    /// </summary>
    private void ProbeOnUIThread()
    {
        if (this.closed || !this.isForeground)
        {
            return;
        }
        string root = this.CurrentRoot();
        if (root.Length == 0)
        {
            // 此电脑视图：先原地刷新驱动器容量，再继续探测已展开驱动器与其子行
            this.UpdateDriveRows();
        }
        else
        {
            List<string> rootPath = new List<string>();
            rootPath.Add(root);
            if (this.scanService.NeedsRefresh(rootPath))
            {
                UiLogger.Log("磁盘视图", "台前探测到根目录一级变化，请求增量扫描：根目录=" + root);
                this.SummaryText = "检测到目录有变化，正在增量刷新 ……";
                this.subscribedRoot = root;
                this.scanService.RequestScan(root, this.ownerId, false, false);
                return;
            }
        }
        List<DiskRow> candidates = new List<DiskRow>();
        foreach (DiskRow row in this.Rows)
        {
            if (!row.IsDirectory)
            {
                continue;
            }
            bool isDriveRow = row.Parent == null;
            if (root.Length == 0 && isDriveRow && !row.IsExpanded)
            {
                // 此电脑视图的驱动器行容量由 UpdateDriveRows 直接刷新，不参与目录探测
                continue;
            }
            candidates.Add(row);
        }
        foreach (DiskRow row in candidates)
        {
            if (this.pendingProbePaths.Contains(row.FullPath))
            {
                continue;
            }
            if (this.scanService.IsScanning)
            {
                break;
            }
            if (!this.Rows.Contains(row))
            {
                continue;
            }
            if (row.IsPendingSize)
            {
                // 回填曾在切后台时被中断：重新进入台前后按原样继续补齐该行大小
                this.StartRowBackfill(row);
                continue;
            }
            List<string> onePath = new List<string>();
            onePath.Add(row.FullPath);
            if (!this.scanService.NeedsRefresh(onePath))
            {
                continue;
            }
            if (row.IsExpanded)
            {
                UiLogger.Log("磁盘视图", "台前探测到已展开目录变化，重新加载子行：路径=" + row.FullPath);
                this.CollapseRow(row);
                this.ExpandRow(row);
            }
            else
            {
                this.StartRowBackfill(row);
            }
        }
    }

    /// <summary>对单个可见目录发起一次最小补算：只重算该行大小并回写快照。</summary>
    /// <param name="row">需要重算的目录行。</param>
    private void StartRowBackfill(DiskRow row)
    {
        List<DiskRow> rowsToCompute = new List<DiskRow>();
        rowsToCompute.Add(row);
        List<DiskRow> anchors = new List<DiskRow>();
        anchors.Add(row);
        this.pendingProbePaths.Add(row.FullPath);
        UiLogger.Log("磁盘视图", "台前探测到目录变化，重算该行大小：路径=" + row.FullPath);
        BackfillRequest request = new BackfillRequest(this, rowsToCompute, anchors, this.expandCancellationSource.Token);
        Task.Run(request.Execute);
    }

    /// <summary>扫描进度事件（后台线程触发）：过滤自己的根目录后把进度文本投递到界面线程。</summary>
    /// <param name="progress">进度快照。</param>
    private void OnScanProgress(DiskScanService.DiskScanProgress progress)
    {
        if (!this.isForeground || this.closed)
        {
            return;
        }
        string root = this.CurrentRoot();
        if (progress.RootPath != root)
        {
            return;
        }
        string text = DiskViewPanelViewModel.BuildProgressText(progress);
        ProgressTextRequest request = new ProgressTextRequest(this, text, root);
        Dispatcher.UIThread.Post(request.Apply);
    }

    /// <summary>扫描完成事件（后台线程或请求线程触发）：只在台前且根目录一致时应用结果。</summary>
    /// <param name="result">完成结果。</param>
    private void OnScanCompleted(DiskScanService.DiskScanResult result)
    {
        if (!this.isForeground || this.closed)
        {
            return;
        }
        if (result.RootPath != this.CurrentRoot())
        {
            return;
        }
        CompletedRequest request = new CompletedRequest(this, result);
        Dispatcher.UIThread.Post(request.Apply);
    }

    /// <summary>把共享扫描服务的完成结果克隆后应用到本视图的行集合。</summary>
    /// <param name="result">完成结果。</param>
    private void ApplyCompleted(DiskScanService.DiskScanResult result)
    {
        if (this.closed || !this.isForeground || result.RootPath != this.CurrentRoot())
        {
            return;
        }
        this.Rows.Clear();
        foreach (DiskRow row in result.Rows)
        {
            this.Rows.Add(row.Clone());
        }
        if (this.sortField.Length > 0)
        {
            this.ReapplySort();
        }
        this.SummaryText = result.SummaryText;
    }

    /// <summary>展开目录：置展开标记后在后台枚举一层子项，再回填到界面。</summary>
    /// <param name="row">要展开的目录行。</param>
    private void ExpandRow(DiskRow row)
    {
        row.IsExpanded = true;
        this.pendingProbePaths.Add(row.FullPath);
        CancellationToken token = this.expandCancellationSource.Token;
        LoadChildrenRequest request = new LoadChildrenRequest(this, row, token);
        UiLogger.Log("磁盘视图", "展开目录：路径=" + row.FullPath);
        Task.Run(request.Execute);
    }

    /// <summary>收起目录：清除展开标记并移除其全部后代行。</summary>
    /// <param name="row">要收起的目录行。</param>
    private void CollapseRow(DiskRow row)
    {
        row.IsExpanded = false;
        this.ClearChildrenTree(row);
        int index = this.Rows.IndexOf(row);
        if (index >= 0)
        {
            int descendants = this.CountDescendants(index);
            for (int count = 0; count < descendants; count++)
            {
                this.Rows.RemoveAt(index + 1);
            }
        }
        UiLogger.Log("磁盘视图", "收起目录：路径=" + row.FullPath);
    }

    /// <summary>递归清空行的子行记录。</summary>
    /// <param name="row">目标行。</param>
    private void ClearChildrenTree(DiskRow row)
    {
        foreach (DiskRow child in row.Children)
        {
            this.ClearChildrenTree(child);
        }
        row.Children.Clear();
    }

    /// <summary>统计某行之后紧跟着的后代行数量（层级大于该行的连续行）。</summary>
    /// <param name="index">该行在集合中的位置。</param>
    /// <returns>后代行数量。</returns>
    private int CountDescendants(int index)
    {
        int level = this.Rows[index].Level;
        int descendants = 0;
        for (int position = index + 1; position < this.Rows.Count && this.Rows[position].Level > level; position++)
        {
            descendants++;
        }
        return descendants;
    }

    /// <summary>把后台枚举到的子行应用到界面，并启动子目录大小回填。</summary>
    /// <param name="request">加载请求（含子行列表与错误）。</param>
    private void ApplyChildren(LoadChildrenRequest request)
    {
        if (this.closed)
        {
            return;
        }
        DiskRow parent = request.Parent;
        if (request.Token.IsCancellationRequested)
        {
            // 取消时待探测集合已随取消操作清空，这里不能再动，避免误删新一轮的标记
            return;
        }
        this.pendingProbePaths.Remove(parent.FullPath);
        if (request.Error != null || request.Children == null)
        {
            parent.IsExpanded = false;
            return;
        }
        if (!parent.IsExpanded)
        {
            return;
        }
        int parentIndex = this.Rows.IndexOf(parent);
        if (parentIndex < 0 || parent.Children.Count > 0)
        {
            return;
        }
        List<DiskRow> pending = new List<DiskRow>();
        int insertIndex = parentIndex + 1;
        foreach (DiskRow child in request.Children)
        {
            parent.Children.Add(child);
            this.Rows.Insert(insertIndex, child);
            insertIndex++;
            if (child.IsDirectory && child.IsPendingSize)
            {
                pending.Add(child);
            }
        }
        if (parent.IsPendingSize)
        {
            pending.Add(parent);
        }
        if (pending.Count == 0)
        {
            return;
        }
        List<DiskRow> anchors = new List<DiskRow>();
        if (parent.IsPendingSize)
        {
            anchors.Add(parent);
        }
        if (request.Children.Count > 0)
        {
            anchors.Add(request.Children[0]);
        }
        foreach (DiskRow row in pending)
        {
            this.pendingProbePaths.Add(row.FullPath);
        }
        BackfillRequest backfill = new BackfillRequest(this, pending, anchors, this.expandCancellationSource.Token);
        Task.Run(backfill.Execute);
    }

    /// <summary>把后台回填的子目录大小应用到界面，并按新大小重排同级分组。</summary>
    /// <param name="request">回填请求（含各行结果）。</param>
    private void ApplyBackfill(BackfillRequest request)
    {
        if (this.closed)
        {
            return;
        }
        bool cancelled = request.Token.IsCancellationRequested;
        for (int index = 0; index < request.Rows.Count; index++)
        {
            DiskRow row = request.Rows[index];
            if (request.Completed[index])
            {
                row.SizeBytes = request.Sizes[index];
                row.SizeText = DiskScanService.FormatSize(request.Sizes[index]);
                row.IsPendingSize = false;
            }
            else if (request.Failed[index])
            {
                row.SizeText = "无法统计";
                row.IsPendingSize = false;
            }
            if (!cancelled)
            {
                this.pendingProbePaths.Remove(row.FullPath);
            }
        }
        if (cancelled)
        {
            return;
        }
        foreach (DiskRow anchor in request.Anchors)
        {
            this.ReorderChildrenGroup(anchor);
        }
        // 回填完成后把新记录的目录大小与修改时间持久化，下次启动可直接复用
        this.scanService.SaveSnapshot();
    }

    /// <summary>
    /// 把锚点所在行的同级连续分组按「目录在前、大小降序」重新排序；
    /// 分组内有已展开且带子行的行时会打乱其子行位置，此时跳过本次重排。
    /// </summary>
    /// <param name="anchor">分组内的任意一行。</param>
    private void ReorderChildrenGroup(DiskRow anchor)
    {
        int anchorIndex = this.Rows.IndexOf(anchor);
        if (anchorIndex < 0)
        {
            return;
        }
        DiskRow? parent = anchor.Parent;
        int start = anchorIndex;
        while (start > 0 && object.ReferenceEquals(this.Rows[start - 1].Parent, parent))
        {
            start--;
        }
        int end = anchorIndex;
        while (end + 1 < this.Rows.Count && object.ReferenceEquals(this.Rows[end + 1].Parent, parent))
        {
            end++;
        }
        int count = end - start + 1;
        if (count <= 1)
        {
            return;
        }
        for (int index = start; index <= end; index++)
        {
            DiskRow row = this.Rows[index];
            if (row.IsExpanded && row.Children.Count > 0)
            {
                return;
            }
        }
        List<DiskRow> group = new List<DiskRow>(count);
        for (int index = start; index <= end; index++)
        {
            group.Add(this.Rows[index]);
        }
        group.Sort(this.CompareDiskRows);
        for (int index = 0; index < count; index++)
        {
            this.Rows[start + index] = group[index];
        }
    }

    /// <summary>在后台读取驱动器列表（此电脑视图，仅读容量，不进行目录扫描）。</summary>
    private void BeginLoadDrives()
    {
        Task.Run(new Action(this.LoadDrives));
    }

    /// <summary>后台枚举全部驱动器并构建行，完成后投递到界面线程。</summary>
    private void LoadDrives()
    {
        try
        {
            List<DiskRow> rows = new List<DiskRow>();
            DriveInfo[] drives = DriveInfo.GetDrives();
            foreach (DriveInfo drive in drives)
            {
                DiskRow row = DiskViewPanelViewModel.BuildDriveRow(drive);
                this.RecordDriveMtime(row);
                rows.Add(row);
            }
            string summary = "扫描完成：此电脑，共 " + rows.Count + " 个驱动器（读取驱动器容量，未进行目录扫描）";
            DriveRowsApplyRequest request = new DriveRowsApplyRequest(this, rows, summary);
            Dispatcher.UIThread.Post(request.Apply);
        }
        catch (Exception exception)
        {
            UiLogger.LogError("磁盘视图", "读取驱动器列表失败", exception);
            DriveRowsApplyRequest request = new DriveRowsApplyRequest(this, new List<DiskRow>(), "扫描失败：" + exception.Message);
            Dispatcher.UIThread.Post(request.Apply);
        }
    }

    /// <summary>把后台读到的驱动器行应用到界面；根目录已切走时丢弃过期结果。</summary>
    /// <param name="rows">驱动器行列表。</param>
    /// <param name="summary">摘要文本。</param>
    private void ApplyDriveRows(List<DiskRow> rows, string summary)
    {
        if (this.closed || this.CurrentRoot().Length != 0)
        {
            return;
        }
        if (this.Rows.Count == 0)
        {
            foreach (DiskRow row in rows)
            {
                this.Rows.Add(row);
            }
            if (this.sortField.Length > 0)
            {
                this.ReapplySort();
            }
        }
        else
        {
            // 重新进入台前后：只合并刷新驱动器容量，保留已展开的树与子行，不重建列表
            this.MergeDriveRows(rows);
        }
        this.SummaryText = summary;
    }

    /// <summary>台前周期里直接刷新此电脑视图的驱动器容量（仅读 DriveInfo，成本极低）。</summary>
    private void UpdateDriveRows()
    {
        if (this.closed || !this.isForeground || this.CurrentRoot().Length != 0)
        {
            return;
        }
        try
        {
            List<DiskRow> freshRows = new List<DiskRow>();
            DriveInfo[] drives = DriveInfo.GetDrives();
            foreach (DriveInfo drive in drives)
            {
                freshRows.Add(DiskViewPanelViewModel.BuildDriveRow(drive));
            }
            this.MergeDriveRows(freshRows);
        }
        catch (Exception exception)
        {
            UiLogger.LogError("磁盘视图", "刷新驱动器容量失败", exception);
        }
    }

    /// <summary>
    /// 把新的驱动器行合并进当前列表：按路径原地更新容量等字段、追加新出现的盘、
    /// 移除消失的盘及其子行；已存在的驱动器行保留展开状态与子行。
    /// </summary>
    /// <param name="freshRows">后台新构建的驱动器行。</param>
    private void MergeDriveRows(List<DiskRow> freshRows)
    {
        HashSet<string> seen = new HashSet<string>();
        foreach (DiskRow fresh in freshRows)
        {
            seen.Add(fresh.FullPath);
            DiskRow? existing = null;
            foreach (DiskRow row in this.Rows)
            {
                if (row.Parent == null && row.FullPath == fresh.FullPath)
                {
                    existing = row;
                    break;
                }
            }
            if (existing != null)
            {
                existing.SizeBytes = fresh.SizeBytes;
                existing.SizeText = fresh.SizeText;
                existing.FreeText = fresh.FreeText;
                existing.FreeBytes = fresh.FreeBytes;
                existing.UsageText = fresh.UsageText;
                existing.UsagePercent = fresh.UsagePercent;
                existing.FileSystemText = fresh.FileSystemText;
                this.RecordDriveMtime(existing);
            }
            else
            {
                this.RecordDriveMtime(fresh);
                this.Rows.Add(fresh);
            }
        }
        for (int index = this.Rows.Count - 1; index >= 0; index--)
        {
            DiskRow row = this.Rows[index];
            if (row.Parent == null && row.EntryTypeText == "驱动器" && !seen.Contains(row.FullPath))
            {
                int descendants = this.CountDescendants(index);
                for (int count = 0; count < descendants; count++)
                {
                    this.Rows.RemoveAt(index + 1);
                }
                this.Rows.RemoveAt(index);
            }
        }
        if (this.sortField.Length > 0)
        {
            this.ReapplySort();
        }
    }

    /// <summary>把驱动器根目录的修改时间记入快照，供已展开驱动器行的台前探测比较。</summary>
    /// <param name="row">驱动器行。</param>
    private void RecordDriveMtime(DiskRow row)
    {
        try
        {
            long lastWriteTicks = Directory.GetLastWriteTimeUtc(row.FullPath).Ticks;
            this.scanService.RecordDirectoryMtime(row.FullPath, lastWriteTicks, row.SizeBytes);
        }
        catch (Exception)
        {
            // 驱动器不可访问时跳过记录，探测会保持静默
        }
    }

    /// <summary>构建一行驱动器数据（容量、剩余、使用率与文件系统）。</summary>
    /// <param name="drive">驱动器信息。</param>
    /// <returns>驱动器行。</returns>
    private static DiskRow BuildDriveRow(DriveInfo drive)
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
        row.SizeText = DiskScanService.FormatSize(usedBytes) + " / " + DiskScanService.FormatSize(totalBytes);
        if (totalBytes > 0)
        {
            row.FreeText = DiskScanService.FormatSize(freeBytes);
            row.FreeBytes = freeBytes;
            row.UsagePercent = usedBytes * 100D / totalBytes;
            row.UsageText = row.UsagePercent.ToString("F1") + "%";
        }
        try
        {
            row.FileSystemText = drive.DriveFormat;
        }
        catch (Exception)
        {
            // 无介质或不可访问的驱动器取不到文件系统，保持占位符
            row.FileSystemText = "—";
        }
        return row;
    }

    /// <summary>释放当前根目录的扫描订阅（没有订阅时不做事）。</summary>
    private void ReleaseCurrentScan()
    {
        if (this.subscribedRoot.Length == 0)
        {
            return;
        }
        this.scanService.ReleaseScan(this.subscribedRoot, this.ownerId);
        this.subscribedRoot = string.Empty;
    }

    /// <summary>取消全部在途的展开加载与大小回填，并清空待探测路径集合。</summary>
    private void CancelExpandWork()
    {
        this.expandCancellationSource.Cancel();
        this.expandCancellationSource = new CancellationTokenSource();
        this.pendingProbePaths.Clear();
    }

    /// <summary>取当前根目录（去除首尾空白，留空表示此电脑视图）。</summary>
    /// <returns>根目录文本。</returns>
    private string CurrentRoot()
    {
        return this.rootPathInput == null ? string.Empty : this.rootPathInput.Trim();
    }

    /// <summary>触发属性变化通知。</summary>
    /// <param name="propertyName">属性名。</param>
    private void OnPropertyChanged(string propertyName)
    {
        PropertyChangedEventHandler? handler = this.PropertyChanged;
        if (handler != null)
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>把扫描服务的进度快照转成本视图的进度文本。</summary>
    /// <param name="progress">进度快照。</param>
    /// <returns>进度文本。</returns>
    private static string BuildProgressText(DiskScanService.DiskScanProgress progress)
    {
        string text = "正在扫描 " + progress.RootPath + " …… 已完成 " + progress.FinishedCount + "/" + progress.TotalCount
            + " 项，已统计 " + DiskScanService.FormatSize(progress.CountedBytes);
        if (progress.CurrentName.Length > 0)
        {
            text = text + "，正在统计：" + progress.CurrentName;
        }
        return text;
    }

    /// <summary>一次展开加载：后台枚举父行的一层子项并构建子行，完成后投递回界面线程。</summary>
    private sealed class LoadChildrenRequest
    {
        private readonly DiskViewPanelViewModel owner;
        private readonly DiskRow parent;
        private readonly CancellationToken token;

        public LoadChildrenRequest(DiskViewPanelViewModel owner, DiskRow parent, CancellationToken token)
        {
            this.owner = owner;
            this.parent = parent;
            this.token = token;
        }

        /// <summary>父行（展开目标）。</summary>
        public DiskRow Parent
        {
            get { return this.parent; }
        }

        /// <summary>加载令牌：用于界面应用时判定是否已取消。</summary>
        public CancellationToken Token
        {
            get { return this.token; }
        }

        /// <summary>枚举得到的子行列表；失败时为空。</summary>
        public List<DiskRow>? Children { get; private set; }

        /// <summary>加载失败的异常；成功时为空。</summary>
        public Exception? Error { get; private set; }

        /// <summary>后台执行：枚举一层子项、构建子行并排序，最后投递到界面线程。</summary>
        public void Execute()
        {
            try
            {
                IReadOnlyList<DiskEntry> entries = this.owner.diskUsageProvider.GetTopLevelEntries(this.parent.FullPath);
                if (this.token.IsCancellationRequested)
                {
                    // 已取消：待探测集合随取消操作清空，不再投递
                    return;
                }
                List<DiskRow> children = new List<DiskRow>();
                foreach (DiskEntry entry in entries)
                {
                    DiskRow child = new DiskRow();
                    child.Name = entry.Name;
                    child.FullPath = entry.FullPath;
                    child.IsDirectory = entry.EntryType == DiskEntryKind.Directory;
                    child.Level = this.parent.Level + 1;
                    child.Parent = this.parent;
                    child.EntryTypeText = child.IsDirectory ? "目录" : "文件";
                    if (child.IsDirectory)
                    {
                        child.SizeText = "计算中……";
                        child.IsPendingSize = true;
                    }
                    else
                    {
                        child.SizeBytes = entry.SizeBytes;
                        child.SizeText = DiskScanService.FormatSize(entry.SizeBytes);
                    }
                    children.Add(child);
                }
                children.Sort(this.owner.CompareDiskRows);
                this.Children = children;
            }
            catch (Exception exception)
            {
                this.Error = exception;
                UiLogger.LogError("磁盘视图", "展开目录失败：路径=" + this.parent.FullPath, exception);
            }
            Dispatcher.UIThread.Post(this.Apply);
        }

        /// <summary>界面线程入口：交给视图应用子行。</summary>
        public void Apply()
        {
            this.owner.ApplyChildren(this);
        }
    }

    /// <summary>一批目录大小回填：并行统计各行大小，完成后一次性回填并重排同级分组。</summary>
    private sealed class BackfillRequest
    {
        private readonly DiskViewPanelViewModel owner;
        private readonly List<DiskRow> rows;
        private readonly List<DiskRow> anchors;
        private readonly CancellationToken token;
        private readonly long[] sizes;
        private readonly bool[] completed;
        private readonly bool[] failed;

        public BackfillRequest(DiskViewPanelViewModel owner, List<DiskRow> rows, List<DiskRow> anchors, CancellationToken token)
        {
            this.owner = owner;
            this.rows = rows;
            this.anchors = anchors;
            this.token = token;
            this.sizes = new long[rows.Count];
            this.completed = new bool[rows.Count];
            this.failed = new bool[rows.Count];
        }

        /// <summary>待回填的行列表。</summary>
        public List<DiskRow> Rows
        {
            get { return this.rows; }
        }

        /// <summary>回填完成后需要重排的同级分组锚点。</summary>
        public List<DiskRow> Anchors
        {
            get { return this.anchors; }
        }

        /// <summary>回填令牌。</summary>
        public CancellationToken Token
        {
            get { return this.token; }
        }

        /// <summary>各行统计结果（与 Rows 一一对应）。</summary>
        public long[] Sizes
        {
            get { return this.sizes; }
        }

        /// <summary>各行是否统计成功。</summary>
        public bool[] Completed
        {
            get { return this.completed; }
        }

        /// <summary>各行是否统计失败。</summary>
        public bool[] Failed
        {
            get { return this.failed; }
        }

        /// <summary>后台执行：并行统计各行大小，无论成败都把结果投递回界面线程。</summary>
        public void Execute()
        {
            try
            {
                ParallelOptions options = new ParallelOptions();
                options.MaxDegreeOfParallelism = Environment.ProcessorCount;
                options.CancellationToken = this.token;
                Parallel.For(0, this.rows.Count, options, this.ComputeOne);
            }
            catch (OperationCanceledException)
            {
                UiLogger.Log("磁盘视图", "子目录大小回填已取消（视图已离开台前或已切换根目录）");
            }
            catch (Exception exception)
            {
                UiLogger.LogError("磁盘视图", "子目录大小回填失败", exception);
            }
            finally
            {
                Dispatcher.UIThread.Post(this.Apply);
            }
        }

        /// <summary>统计单个目录的大小：经共享服务单飞，同路径并发回填只实际扫描一次。</summary>
        /// <param name="index">行序号。</param>
        private void ComputeOne(int index)
        {
            DiskRow row = this.rows[index];
            try
            {
                long sizeBytes = this.owner.scanService.ComputeSizeShared(row.FullPath, this.token);
                this.sizes[index] = sizeBytes;
                this.completed[index] = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                this.failed[index] = true;
                UiLogger.LogError("磁盘视图", "统计子目录失败：路径=" + row.FullPath, exception);
            }
        }

        /// <summary>界面线程入口：交给视图回填结果。</summary>
        public void Apply()
        {
            this.owner.ApplyBackfill(this);
        }
    }

    /// <summary>把一条进度文本在界面线程上应用到摘要栏（过期时丢弃）。</summary>
    private sealed class ProgressTextRequest
    {
        private readonly DiskViewPanelViewModel owner;
        private readonly string text;
        private readonly string root;

        public ProgressTextRequest(DiskViewPanelViewModel owner, string text, string root)
        {
            this.owner = owner;
            this.text = text;
            this.root = root;
        }

        /// <summary>界面线程入口：校验视图状态后更新摘要文本。</summary>
        public void Apply()
        {
            if (this.owner.closed || !this.owner.isForeground || this.owner.CurrentRoot() != this.root)
            {
                return;
            }
            this.owner.SummaryText = this.text;
        }
    }

    /// <summary>把一条扫描完成结果在界面线程上应用到行集合（过期时丢弃）。</summary>
    private sealed class CompletedRequest
    {
        private readonly DiskViewPanelViewModel owner;
        private readonly DiskScanService.DiskScanResult result;

        public CompletedRequest(DiskViewPanelViewModel owner, DiskScanService.DiskScanResult result)
        {
            this.owner = owner;
            this.result = result;
        }

        /// <summary>界面线程入口：校验视图状态后应用结果。</summary>
        public void Apply()
        {
            this.owner.ApplyCompleted(this.result);
        }
    }

    /// <summary>把后台读取的驱动器行在界面线程上应用到行集合。</summary>
    private sealed class DriveRowsApplyRequest
    {
        private readonly DiskViewPanelViewModel owner;
        private readonly List<DiskRow> rows;
        private readonly string summary;

        public DriveRowsApplyRequest(DiskViewPanelViewModel owner, List<DiskRow> rows, string summary)
        {
            this.owner = owner;
            this.rows = rows;
            this.summary = summary;
        }

        /// <summary>界面线程入口：应用驱动器行与摘要。</summary>
        public void Apply()
        {
            this.owner.ApplyDriveRows(this.rows, this.summary);
        }
    }
}

/// <summary>磁盘视图列表的一行：文件或目录的占用统计，支持树形层级展开。</summary>
public class DiskRow : INotifyPropertyChanged
{
    private string sizeText = string.Empty;
    private long sizeBytes;
    private bool isExpanded;
    private int level;

    /// <summary>条目显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>条目完整路径。</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>条目类型文本：驱动器、目录或文件。</summary>
    public string EntryTypeText { get; set; } = string.Empty;

    /// <summary>剩余可用空间文本；非驱动器行显示占位符。</summary>
    public string FreeText { get; set; } = "—";

    /// <summary>剩余可用空间字节数（非驱动器行为 0，排序使用）。</summary>
    public long FreeBytes { get; set; }

    /// <summary>已用空间占比文本（如 82.7%）；非驱动器行显示占位符。</summary>
    public string UsageText { get; set; } = "—";

    /// <summary>已用空间占比数值（0 到 100，非驱动器行为 0，排序使用）。</summary>
    public double UsagePercent { get; set; }

    /// <summary>文件系统格式（如 NTFS）；非驱动器行显示占位符。</summary>
    public string FileSystemText { get; set; } = "—";

    /// <summary>是否为目录（驱动器也视为目录，可展开）。</summary>
    public bool IsDirectory { get; set; }

    /// <summary>父行；根层行为空。</summary>
    public DiskRow? Parent { get; set; }

    /// <summary>目录大小是否仍在统计中（显示「计算中……」占位）。</summary>
    public bool IsPendingSize { get; set; }

    /// <summary>直接子行列表（仅展开过的目录才有内容）。</summary>
    public List<DiskRow> Children { get; } = new List<DiskRow>();

    /// <summary>占用字节数。</summary>
    public long SizeBytes
    {
        get { return this.sizeBytes; }
        set
        {
            this.sizeBytes = value;
            this.OnPropertyChanged(nameof(this.SizeBytes));
        }
    }

    /// <summary>格式化后的大小文本。</summary>
    public string SizeText
    {
        get { return this.sizeText; }
        set
        {
            this.sizeText = value;
            this.OnPropertyChanged(nameof(this.SizeText));
        }
    }

    /// <summary>树形层级：0 为根层，每展开一层加 1。</summary>
    public int Level
    {
        get { return this.level; }
        set
        {
            this.level = value;
            this.OnPropertyChanged(nameof(this.Level));
            this.OnPropertyChanged(nameof(this.IndentMargin));
        }
    }

    /// <summary>是否已展开（仅目录行有意义）。</summary>
    public bool IsExpanded
    {
        get { return this.isExpanded; }
        set
        {
            this.isExpanded = value;
            this.OnPropertyChanged(nameof(this.IsExpanded));
            this.OnPropertyChanged(nameof(this.ExpandGlyph));
        }
    }

    /// <summary>名称列缩进：每层 16 像素。</summary>
    public Thickness IndentMargin
    {
        get { return new Thickness(this.level * 16, 0, 0, 0); }
    }

    /// <summary>展开指示符号：目录未展开显示 ▸、已展开显示 ▾，文件为空。</summary>
    public string ExpandGlyph
    {
        get
        {
            if (!this.IsDirectory)
            {
                return string.Empty;
            }
            return this.isExpanded ? "▾" : "▸";
        }
    }

    /// <summary>属性变化通知事件。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>克隆一行（共享扫描服务的结果行由各视图克隆后使用，保证多视图互不干扰）。</summary>
    /// <returns>本行的副本（不含父子关系与展开状态）。</returns>
    public DiskRow Clone()
    {
        DiskRow copy = new DiskRow();
        copy.Name = this.Name;
        copy.FullPath = this.FullPath;
        copy.EntryTypeText = this.EntryTypeText;
        copy.FreeText = this.FreeText;
        copy.FreeBytes = this.FreeBytes;
        copy.UsageText = this.UsageText;
        copy.UsagePercent = this.UsagePercent;
        copy.FileSystemText = this.FileSystemText;
        copy.IsDirectory = this.IsDirectory;
        copy.sizeBytes = this.sizeBytes;
        copy.sizeText = this.sizeText;
        copy.level = this.level;
        return copy;
    }

    /// <summary>触发属性变化通知。</summary>
    /// <param name="propertyName">属性名。</param>
    private void OnPropertyChanged(string propertyName)
    {
        PropertyChangedEventHandler? handler = this.PropertyChanged;
        if (handler != null)
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
