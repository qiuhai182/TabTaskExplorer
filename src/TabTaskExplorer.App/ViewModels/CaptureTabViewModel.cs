using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.Services.Capture;
using TabTaskExplorer.Kernel.Capture;
using TabTaskExplorer.Kernel.Media;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>产物历史列表中的一行：文件名、路径、类型、大小与修改时间。</summary>
public class CaptureHistoryItem
{
    /// <summary>文件名（含扩展名）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>完整路径。</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>类型文字（例如「PNG 图片」）。</summary>
    public string TypeText { get; set; } = string.Empty;

    /// <summary>文件大小文字。</summary>
    public string SizeText { get; set; } = string.Empty;

    /// <summary>修改时间文字。</summary>
    public string TimeText { get; set; } = string.Empty;

    /// <summary>图标分类键。</summary>
    public string IconCategory { get; set; } = FileIconCatalog.File;

    /// <summary>图标强调色。</summary>
    public string IconColor { get; set; } = string.Empty;
}

/// <summary>
/// 截图与录屏标签页：提供全屏 / 区域 / 活动窗口 / 上次区域截图、开始停止录屏、
/// 产物历史浏览与设置入口；所有实际动作交给 <see cref="CaptureCoordinator"/>。
/// </summary>
public class CaptureTabViewModel : INotifyPropertyChanged, ITabView
{
    private static readonly int[] DelaySecondsOptions = new int[] { 0, 3, 5, 10 };

    private readonly CaptureCoordinator coordinator;
    private readonly AppSettings settings;
    private readonly DispatcherTimer stateTimer;
    private string statusText = "就绪：可选择截图方式，或使用全局快捷键在任意程序中截图与录屏。";
    private string environmentText = string.Empty;
    private bool isRecording;
    private bool isAttached = true;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    public CaptureTabViewModel(AppSettings settings, CaptureCoordinator coordinator)
    {
        this.settings = settings;
        this.coordinator = coordinator;
        this.delayIndex = FindDelayIndex(settings.CaptureDelaySeconds);
        this.recordingTargetIndex = coordinator.ResolveRecordingTargetIndex(settings.CaptureRecordTarget);
        this.coordinator.CaptureFinished += this.OnCaptureFinished;
        this.coordinator.RecordingStateChanged += this.OnRecordingStateChanged;
        this.stateTimer = new DispatcherTimer();
        this.stateTimer.Interval = TimeSpan.FromSeconds(1);
        this.stateTimer.Tick += this.OnStateTimerTick;
        this.stateTimer.Start();
        this.RefreshHistory();
        this.RefreshEnvironment();
        UiLogger.Log("截图录屏", "截图录屏标签页已创建：保存目录=" + this.OutputDirectory
            + " 延迟秒数=" + settings.CaptureDelaySeconds
            + " 录屏目标索引=" + this.RecordingTargetIndex);
    }

    /// <inheritdoc />
    public string Kind
    {
        get { return TabKinds.Capture; }
    }

    /// <inheritdoc />
    public string Title
    {
        get { return "截图与录屏"; }
    }

    /// <inheritdoc />
    public void FillSnapshot(DebugTabState tabState)
    {
        TabSnapshotFiller.FillCapture(this, tabState);
    }

    /// <summary>应用设置（界面直接绑定本对象暴露的设置属性）。</summary>
    public AppSettings Settings
    {
        get { return this.settings; }
    }

    /// <summary>状态栏文字。</summary>
    public string StatusText
    {
        get { return this.statusText; }
        private set
        {
            this.statusText = value;
            this.RaisePropertyChanged(nameof(this.StatusText));
        }
    }

    /// <summary>平台能力与 FFmpeg 状态说明。</summary>
    public string EnvironmentText
    {
        get { return this.environmentText; }
        private set
        {
            this.environmentText = value;
            this.RaisePropertyChanged(nameof(this.EnvironmentText));
        }
    }

    /// <summary>当前产物目录。</summary>
    public string OutputDirectory
    {
        get { return CapturePaths.ResolveOutputDirectory(this.settings); }
    }

    /// <summary>产物历史列表。</summary>
    public ObservableCollection<CaptureHistoryItem> HistoryItems { get; } = new ObservableCollection<CaptureHistoryItem>();

    /// <summary>截图延迟选择索引：0 立即、1 三秒、2 五秒、3 十秒。</summary>
    public int DelayIndex
    {
        get { return this.delayIndex; }
        set
        {
            int normalized = value;
            if (normalized < 0 || normalized >= DelaySecondsOptions.Length)
            {
                normalized = 0;
            }
            if (normalized == this.delayIndex)
            {
                return;
            }
            this.delayIndex = normalized;
            this.settings.CaptureDelaySeconds = DelaySecondsOptions[normalized];
            this.coordinator.PersistSettings("截图录屏标签页切换截图延迟");
            this.RaisePropertyChanged(nameof(this.DelayIndex));
        }
    }

    private int delayIndex;

    /// <summary>录屏目标选择索引：0 全部屏幕、1 区域、2 活动窗口。</summary>
    public int RecordingTargetIndex
    {
        get { return this.recordingTargetIndex; }
        set
        {
            int normalized = value;
            if (normalized < 0 || normalized > 2)
            {
                normalized = 0;
            }
            if (normalized == this.recordingTargetIndex)
            {
                return;
            }
            this.recordingTargetIndex = normalized;
            string targetText = normalized == 1 ? "Region" : (normalized == 2 ? "ActiveWindow" : "FullScreen");
            this.settings.CaptureRecordTarget = targetText;
            this.coordinator.PersistSettings("截图录屏标签页切换录屏目标");
            this.RaisePropertyChanged(nameof(this.RecordingTargetIndex));
        }
    }

    private int recordingTargetIndex;

    /// <summary>是否具备全部屏幕截图能力。</summary>
    public bool CanCaptureFullScreen
    {
        get { return this.coordinator.CaptureCapabilities.FullScreen; }
    }

    /// <summary>是否具备区域截图能力。</summary>
    public bool CanCaptureRegion
    {
        get { return this.coordinator.CaptureCapabilities.Region; }
    }

    /// <summary>是否具备活动窗口截图能力。</summary>
    public bool CanCaptureWindow
    {
        get { return this.coordinator.CaptureCapabilities.ActiveWindow; }
    }

    /// <summary>是否可以使用上次区域。</summary>
    public bool CanCaptureLastRegion
    {
        get
        {
            return this.CanCaptureRegion
                && this.settings.CaptureLastRegionWidth > 0
                && this.settings.CaptureLastRegionHeight > 0;
        }
    }

    /// <summary>录屏按钮是否可用（录制中始终可用，用于停止）。</summary>
    public bool CanRecord
    {
        get { return this.isRecording || this.coordinator.IsRecordingAvailable; }
    }

    /// <summary>当前是否正在录屏。</summary>
    public bool IsRecording
    {
        get { return this.isRecording; }
    }

    /// <summary>录屏按钮文字。</summary>
    public string RecordingButtonText
    {
        get { return this.isRecording ? "停止录屏" : "开始录屏"; }
    }

    /// <summary>录制状态文字。</summary>
    public string RecordingStatusText
    {
        get
        {
            if (this.isRecording)
            {
                return "正在录屏 · 已录制 " + this.coordinator.DescribeRecordingDuration();
            }
            return "当前未在录屏";
        }
    }

    /// <summary>已录制时长。</summary>
    public string RecordingElapsedText
    {
        get { return this.isRecording ? this.coordinator.DescribeRecordingDuration() : "00:00:00"; }
    }

    /// <summary>执行全部屏幕截图（含延迟）。</summary>
    public async Task CaptureFullScreenAsync()
    {
        this.StatusText = "正在截图（全部屏幕）……";
        CaptureOutcome outcome = await this.coordinator.CaptureAsync(CaptureTarget.FullScreen, this.ResolveDelaySeconds());
        this.ApplyOutcome(outcome);
    }

    /// <summary>执行区域截图（含延迟与框选）。</summary>
    public async Task CaptureRegionAsync()
    {
        this.StatusText = "请拖动鼠标框选截图区域……";
        CaptureOutcome outcome = await this.coordinator.CaptureRegionAsync(this.ResolveDelaySeconds());
        this.ApplyOutcome(outcome);
    }

    /// <summary>执行活动窗口截图（含延迟）。</summary>
    public async Task CaptureWindowAsync()
    {
        this.StatusText = "正在截图（活动窗口）……";
        CaptureOutcome outcome = await this.coordinator.CaptureAsync(CaptureTarget.ActiveWindow, this.ResolveDelaySeconds());
        this.ApplyOutcome(outcome);
    }

    /// <summary>执行上次区域截图（含延迟）。</summary>
    public async Task CaptureLastRegionAsync()
    {
        this.StatusText = "正在截图（上次区域）……";
        CaptureOutcome outcome = await this.coordinator.CaptureLastRegionAsync(this.ResolveDelaySeconds());
        this.ApplyOutcome(outcome);
    }

    /// <summary>开始或停止录屏（按当前录屏目标）。</summary>
    public async Task ToggleRecordingAsync()
    {
        CaptureOutcome outcome = await this.coordinator.ToggleRecordingAsync(this.RecordingTargetIndex);
        if (outcome.Cancelled)
        {
            this.StatusText = outcome.Message;
            return;
        }
        if (outcome.Success)
        {
            this.StatusText = outcome.Message;
        }
        else
        {
            this.StatusText = "操作失败：" + outcome.Message;
        }
        this.UpdateRecordingState();
    }

    /// <summary>打开产物文件夹标签页。</summary>
    public void OpenOutputFolder()
    {
        this.coordinator.OpenOutputFolderTab(this.OutputDirectory);
        this.StatusText = "已打开产物文件夹：" + this.OutputDirectory;
    }

    /// <summary>在媒体标签中打开一条产物。</summary>
    public void OpenHistoryItem(CaptureHistoryItem item)
    {
        if (item == null)
        {
            return;
        }
        this.coordinator.OpenCapturedFile(item.FullPath);
        this.StatusText = "正在打开：" + item.FullPath;
    }

    /// <summary>保存截图录屏设置到磁盘（仅本分页相关字段，不触发全部文件标签刷新）。</summary>
    public void PersistSettings(string reason)
    {
        this.coordinator.PersistSettings(reason);
        this.RefreshEnvironment();
    }

    /// <summary>重新扫描产物目录并刷新历史列表。</summary>
    public void RefreshHistory()
    {
        this.HistoryItems.Clear();
        string[] paths = this.coordinator.ListCaptureFiles();
        int limit = paths.Length > 200 ? 200 : paths.Length;
        for (int index = 0; index < limit; index++)
        {
            string path = paths[index];
            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(path);
                if (!fileInfo.Exists)
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }
            CaptureHistoryItem item = new CaptureHistoryItem();
            item.Name = fileInfo.Name;
            item.FullPath = fileInfo.FullName;
            item.TypeText = MediaFormatCatalog.DescribeType(path);
            item.SizeText = CapturePaths.DescribeSize(fileInfo.Length);
            item.TimeText = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            item.IconCategory = FileIconCatalog.GetCategoryForName(fileInfo.Name);
            item.IconColor = FileIconCatalog.GetAccent(item.IconCategory);
            this.HistoryItems.Add(item);
        }
        this.RaisePropertyChanged(nameof(this.OutputDirectory));
        this.RaisePropertyChanged(nameof(this.CanCaptureLastRegion));
        UiLogger.Log("截图录屏", "历史列表已刷新：产物目录=" + this.OutputDirectory + " 显示条数=" + this.HistoryItems.Count);
    }

    /// <summary>刷新平台能力与全局快捷键状态文字。</summary>
    public void RefreshEnvironment()
    {
        this.EnvironmentText = this.coordinator.DescribeEnvironment();
        this.RaisePropertyChanged(nameof(this.CanCaptureFullScreen));
        this.RaisePropertyChanged(nameof(this.CanCaptureRegion));
        this.RaisePropertyChanged(nameof(this.CanCaptureWindow));
        this.RaisePropertyChanged(nameof(this.CanCaptureLastRegion));
        this.RaisePropertyChanged(nameof(this.CanRecord));
    }

    /// <summary>标签被选中时刷新历史与录制状态。</summary>
    public void OnActivated()
    {
        this.SyncFromSettings();
        this.RefreshHistory();
        this.RefreshEnvironment();
        this.UpdateRecordingState();
        UiLogger.Log("截图录屏", "截图录屏标签已激活：录制状态=" + (this.isRecording ? "正在录制" : "未录制"));
    }

    /// <summary>把外部设置窗口修改后的截图录屏选项同步回本标签页控件。</summary>
    private void SyncFromSettings()
    {
        int delay = FindDelayIndex(this.settings.CaptureDelaySeconds);
        if (delay != this.delayIndex)
        {
            this.delayIndex = delay;
            this.RaisePropertyChanged(nameof(this.DelayIndex));
        }
        int target = this.coordinator.ResolveRecordingTargetIndex(this.settings.CaptureRecordTarget);
        if (target != this.recordingTargetIndex)
        {
            this.recordingTargetIndex = target;
            this.RaisePropertyChanged(nameof(this.RecordingTargetIndex));
        }
        this.RaisePropertyChanged(nameof(this.Settings));
        this.RaisePropertyChanged(nameof(this.OutputDirectory));
    }

    /// <summary>标签关闭时取消事件订阅并停止状态计时器。</summary>
    public void Detach()
    {
        if (!this.isAttached)
        {
            return;
        }
        this.isAttached = false;
        this.stateTimer.Stop();
        this.stateTimer.Tick -= this.OnStateTimerTick;
        this.coordinator.CaptureFinished -= this.OnCaptureFinished;
        this.coordinator.RecordingStateChanged -= this.OnRecordingStateChanged;
        UiLogger.Log("截图录屏", "截图录屏标签页已关闭：事件订阅与状态计时器已释放");
    }

    /// <summary>把延迟索引解析为秒数。</summary>
    private int ResolveDelaySeconds()
    {
        int index = this.DelayIndex;
        if (index < 0 || index >= DelaySecondsOptions.Length)
        {
            return 0;
        }
        return DelaySecondsOptions[index];
    }

    private static int FindDelayIndex(int seconds)
    {
        for (int index = 0; index < DelaySecondsOptions.Length; index++)
        {
            if (DelaySecondsOptions[index] == seconds)
            {
                return index;
            }
        }
        return 0;
    }

    private void ApplyOutcome(CaptureOutcome outcome)
    {
        if (outcome == null)
        {
            return;
        }
        if (outcome.Cancelled)
        {
            this.StatusText = outcome.Message;
            return;
        }
        if (outcome.Success)
        {
            this.StatusText = outcome.Message;
            this.RefreshHistory();
        }
        else
        {
            this.StatusText = "操作失败：" + outcome.Message;
        }
        this.UpdateRecordingState();
    }

    private void OnCaptureFinished(CaptureOutcome outcome)
    {
        if (outcome == null)
        {
            return;
        }
        if (outcome.Success)
        {
            this.StatusText = outcome.Message;
            this.RefreshHistory();
            this.RaisePropertyChanged(nameof(this.CanCaptureLastRegion));
        }
        else if (!outcome.Cancelled)
        {
            this.StatusText = "操作失败：" + outcome.Message;
        }
        else
        {
            this.StatusText = outcome.Message;
        }
        this.UpdateRecordingState();
    }

    private void OnRecordingStateChanged()
    {
        this.UpdateRecordingState();
    }

    private void OnStateTimerTick(object? sender, EventArgs e)
    {
        if (this.isRecording != this.coordinator.IsRecording)
        {
            this.UpdateRecordingState();
            return;
        }
        if (this.isRecording)
        {
            this.RaisePropertyChanged(nameof(this.RecordingElapsedText));
            this.RaisePropertyChanged(nameof(this.RecordingStatusText));
        }
    }

    private void UpdateRecordingState()
    {
        bool recording = this.coordinator.IsRecording;
        if (recording != this.isRecording)
        {
            this.isRecording = recording;
            this.RaisePropertyChanged(nameof(this.IsRecording));
            this.RaisePropertyChanged(nameof(this.RecordingButtonText));
        }
        this.RaisePropertyChanged(nameof(this.RecordingStatusText));
        this.RaisePropertyChanged(nameof(this.RecordingElapsedText));
        this.RaisePropertyChanged(nameof(this.CanRecord));
        this.RaisePropertyChanged(nameof(this.CanCaptureLastRegion));
    }

    private void RaisePropertyChanged(string propertyName)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
