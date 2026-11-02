using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.App.Views;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Capture;
using TabTaskExplorer.Kernel.Media;
using TabTaskExplorer.Kernel.Ocr;
using TabTaskExplorer.Kernel.Services;

namespace TabTaskExplorer.App.Services.Capture;

/// <summary>一次截图或录屏操作的结果，供截图录屏标签页更新状态与刷新历史。</summary>
public class CaptureOutcome
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>用户是否主动取消（取消不算失败，不弹错误框）。</summary>
    public bool Cancelled { get; set; }

    /// <summary>产物文件完整路径；失败或取消时为空文本。</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>结果说明（中文，可直接展示）。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>构造成功结果。</summary>
    public static CaptureOutcome CreateSuccess(string filePath, string message)
    {
        CaptureOutcome outcome = new CaptureOutcome();
        outcome.Success = true;
        outcome.FilePath = filePath;
        outcome.Message = message;
        return outcome;
    }

    /// <summary>构造失败结果。</summary>
    public static CaptureOutcome CreateFailure(string message)
    {
        CaptureOutcome outcome = new CaptureOutcome();
        outcome.Success = false;
        outcome.Message = message;
        return outcome;
    }

    /// <summary>构造取消结果。</summary>
    public static CaptureOutcome CreateCancelled(string message)
    {
        CaptureOutcome outcome = new CaptureOutcome();
        outcome.Success = false;
        outcome.Cancelled = true;
        outcome.Message = message;
        return outcome;
    }
}

/// <summary>
/// 截图与录屏协调器：把平台截图 / 录屏能力、产物保存、剪贴板、历史刷新、全局快捷键与
/// 「完成后自动打开产物文件夹标签页」串成一条完整流程。界面只需调用本类，不接触平台细节。
/// </summary>
public class CaptureCoordinator : IDisposable
{
    /// <summary>区域框选使用的屏幕物理像素区域与缩放比例。</summary>
    private sealed class ScreenBoundsInfo
    {
        /// <summary>屏幕物理像素区域。</summary>
        public CaptureArea Area = CaptureArea.Empty;

        /// <summary>屏幕缩放比例（物理像素与界面逻辑像素的比值）。</summary>
        public double Scaling = 1.0;
    }

    private readonly IScreenCaptureService screenCaptureService;
    private readonly IScreenRecordingService screenRecordingService;
    private readonly IGlobalHotkeyService globalHotkeyService;
    private readonly ISystemClipboardService systemClipboardService;
    private readonly ITextRecognitionService ocrService;
    private readonly IScreenRegionDetector screenRegionDetector;
    private readonly AppSettingsStore settingsStore = new AppSettingsStore();
    private MainWindowViewModel? viewModel;
    private DispatcherTimer? recordingWatchTimer;
    private DateTime recordingStartedAt = DateTime.MinValue;
    private bool wasRecording;
    private string hotkeyStatusText = "全局快捷键：尚未注册";

    public CaptureCoordinator(IScreenCaptureService screenCaptureService,
        IScreenRecordingService screenRecordingService,
        IGlobalHotkeyService globalHotkeyService,
        ISystemClipboardService systemClipboardService,
        IScreenRegionDetector screenRegionDetector,
        ITextRecognitionService textRecognitionService)
    {
        this.screenCaptureService = screenCaptureService;
        this.screenRecordingService = screenRecordingService;
        this.globalHotkeyService = globalHotkeyService;
        this.systemClipboardService = systemClipboardService;
        this.screenRegionDetector = screenRegionDetector;
        this.ocrService = textRecognitionService;
    }

    /// <summary>一次截图或录屏完成（无论成功、失败或取消），用于刷新历史列表与状态文字。</summary>
    public event Action<CaptureOutcome>? CaptureFinished;

    /// <summary>录制状态发生变化（开始或结束）。</summary>
    public event Action? RecordingStateChanged;

    /// <summary>当前是否正在录制。</summary>
    public bool IsRecording
    {
        get { return this.screenRecordingService.IsRecording; }
    }

    /// <summary>本次录制开始时间；未录制时为最小值。</summary>
    public DateTime RecordingStartedAt
    {
        get { return this.recordingStartedAt; }
    }

    /// <summary>当前录制输出文件路径；未录制时为空文本。</summary>
    public string CurrentRecordingPath
    {
        get { return this.screenRecordingService.CurrentOutputPath; }
    }

    /// <summary>最近一次全局快捷键注册结果摘要。</summary>
    public string HotkeyStatusText
    {
        get { return this.hotkeyStatusText; }
    }

    /// <summary>截图能力清单（界面用于禁用当前平台不支持的按钮）。</summary>
    public ScreenCaptureCapabilities CaptureCapabilities
    {
        get { return this.screenCaptureService.Capabilities; }
    }

    /// <summary>当前平台是否支持系统全局快捷键。</summary>
    public bool IsGlobalHotkeySupported
    {
        get { return this.globalHotkeyService.IsSupported; }
    }

    /// <summary>当前是否具备录屏条件（平台支持且已找到 FFmpeg）。</summary>
    public bool IsRecordingAvailable
    {
        get
        {
            if (!this.screenRecordingService.IsSupported)
            {
                return false;
            }
            string? ffmpeg = FfmpegLocator.Find(this.viewModel == null ? string.Empty : this.viewModel.Settings.FfmpegPath);
            return ffmpeg != null;
        }
    }

    /// <summary>把协调器绑定到主窗口视图模型并注册全局快捷键；必须在界面线程调用。</summary>
    public void Attach(MainWindowViewModel mainViewModel)
    {
        this.viewModel = mainViewModel;
        this.ReloadGlobalHotkeys();
        UiLogger.Log("截图录屏", "协调器已绑定主窗口：全局快捷键注册状态=" + this.hotkeyStatusText
            + " 截图能力=全部屏幕:" + this.CaptureCapabilities.FullScreen
            + " 主屏:" + this.CaptureCapabilities.PrimaryScreen
            + " 区域:" + this.CaptureCapabilities.Region
            + " 活动窗口:" + this.CaptureCapabilities.ActiveWindow
            + " 录屏可用:" + this.IsRecordingAvailable);
    }

    /// <summary>按当前设置重新注册全部全局快捷键；设置窗口确定后由界面调用。</summary>
    public void ReloadGlobalHotkeys()
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            this.hotkeyStatusText = "全局快捷键：主窗口未就绪，未注册";
            UiLogger.Log("全局热键", "重新注册被跳过：主窗口视图模型未就绪");
            return;
        }
        this.globalHotkeyService.UnregisterAll();
        if (!this.globalHotkeyService.IsSupported)
        {
            this.hotkeyStatusText = "全局快捷键：不可用（平台不支持或系统拒绝键盘拦截）";
            UiLogger.Log("全局热键", "重新注册被跳过：全局快捷键不可用，原因可能是当前平台不支持或系统拒绝键盘钩子");
            return;
        }
        int successCount = 0;
        int failureCount = 0;
        string failureNames = string.Empty;
        List<HotkeySetting> globalShortcuts = new List<HotkeySetting>();
        if (mainViewModel.Settings.Shortcuts != null)
        {
            foreach (HotkeySetting shortcut in mainViewModel.Settings.Shortcuts)
            {
                if (shortcut != null && shortcut.IsGlobal)
                {
                    globalShortcuts.Add(shortcut);
                }
            }
        }
        foreach (HotkeySetting setting in globalShortcuts)
        {
            if (string.IsNullOrEmpty(setting.KeyText))
            {
                UiLogger.Log("全局热键", "全局快捷键未设置主键，已跳过：功能=" + setting.DisplayName);
                continue;
            }
            string actionName = setting.ActionName;
            Action callback = delegate
            {
                this.OnGlobalHotkeyTriggered(actionName);
            };
            bool registered = this.globalHotkeyService.Register(setting.ActionName, setting.KeyText,
                setting.Ctrl, setting.Alt, setting.Shift, setting.Win, callback);
            UiLogger.Log("全局热键", "注册全局快捷键：动作=" + setting.DisplayName
                + " 动作标识=" + setting.ActionName
                + " 主键=" + setting.KeyText
                + " 控制键=" + setting.Ctrl
                + " 替换键=" + setting.Alt
                + " 上档键=" + setting.Shift
                + " Windows键=" + setting.Win
                + " 结果=" + (registered ? "成功" : "失败，可能与其他快捷键冲突或平台不支持"));
            if (registered)
            {
                successCount++;
            }
            else
            {
                failureCount++;
                if (failureNames.Length == 0)
                {
                    failureNames = setting.DisplayName;
                }
                else
                {
                    failureNames = failureNames + "、" + setting.DisplayName;
                }
            }
        }
        if (failureCount == 0)
        {
            this.hotkeyStatusText = "全局快捷键：已成功注册 " + successCount + " 个";
        }
        else
        {
            this.hotkeyStatusText = "全局快捷键：成功 " + successCount + " 个，失败 " + failureCount
                + " 个（" + failureNames + "），可在设置中心更换按键";
        }
        UiLogger.Log("全局热键", "全局快捷键注册完成：成功数量=" + successCount + " 失败数量=" + failureCount
            + " 失败动作=" + (failureNames.Length == 0 ? "无" : failureNames));
    }

    /// <summary>全局快捷键触发：平台消息线程回调，切换到界面线程后执行动作。</summary>
    private void OnGlobalHotkeyTriggered(string actionName)
    {
        UiLogger.Log("全局热键", "全局快捷键已触发：动作标识=" + actionName + " 结果=已提交界面线程处理");
        Dispatcher.UIThread.Post(delegate
        {
            this.ExecuteGlobalAction(actionName);
        });
    }

    /// <summary>在界面线程执行全局快捷键对应的动作。</summary>
    private void ExecuteGlobalAction(string actionName)
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("全局热键", "全局快捷键动作被忽略：主窗口视图模型未就绪 动作标识=" + actionName);
            return;
        }
        if (string.Equals(actionName, "ScreenshotRegion", StringComparison.Ordinal))
        {
            _ = this.CaptureRegionAsync(mainViewModel.Settings.CaptureDelaySeconds);
        }
        else if (string.Equals(actionName, "ScreenshotFull", StringComparison.Ordinal))
        {
            _ = this.CaptureAsync(CaptureTarget.FullScreen, mainViewModel.Settings.CaptureDelaySeconds);
        }
        else if (string.Equals(actionName, "ScreenshotPrimary", StringComparison.Ordinal))
        {
            _ = this.CaptureAsync(CaptureTarget.PrimaryScreen, mainViewModel.Settings.CaptureDelaySeconds);
        }
        else if (string.Equals(actionName, "ScreenshotWindow", StringComparison.Ordinal))
        {
            _ = this.CaptureAsync(CaptureTarget.ActiveWindow, mainViewModel.Settings.CaptureDelaySeconds);
        }
        else if (string.Equals(actionName, "ScreenshotLastRegion", StringComparison.Ordinal))
        {
            _ = this.CaptureLastRegionAsync(mainViewModel.Settings.CaptureDelaySeconds);
        }
        else if (string.Equals(actionName, "RecordToggle", StringComparison.Ordinal))
        {
            _ = this.ToggleRecordingAsync(this.ResolveRecordingTargetIndex(mainViewModel.Settings.CaptureRecordTarget));
        }
        else if (string.Equals(actionName, "ColorPick", StringComparison.Ordinal))
        {
            _ = this.OpenScreenColorPickerAsync();
        }
        else if (string.Equals(actionName, "PastePin", StringComparison.Ordinal))
        {
            _ = this.PinClipboardImageAsync();
        }
        else
        {
            // 非截图录屏动作：把主窗口带到前台后复用窗口内统一的快捷键分派逻辑
            MainWindow? window = WindowRegistry.Primary;
            if (window == null)
            {
                UiLogger.Log("全局热键", "全局快捷键动作无法执行：没有可用的主窗口 动作标识=" + actionName);
                return;
            }
            if (!window.IsVisible)
            {
                window.Show();
            }
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
            UiLogger.Log("全局热键", "全局快捷键动作转交主窗口执行：动作标识=" + actionName);
            window.ExecuteShortcutAction(actionName);
        }
    }

    /// <summary>执行一次截图：可选延迟、可选区域框选，保存后复制到剪贴板并按设置打开产物文件夹标签。</summary>
    public async Task<CaptureOutcome> CaptureAsync(CaptureTarget target, int delaySeconds)
    {
        return await this.CaptureInternalAsync(target, CaptureArea.Empty, delaySeconds);
    }

    /// <summary>执行区域截图：先弹出全屏框选窗口，用户取消时返回取消结果。</summary>
    public async Task<CaptureOutcome> CaptureRegionAsync(int delaySeconds)
    {
        if (delaySeconds > 0)
        {
            UiLogger.Log("截图", "区域截图进入延迟等待：延迟秒数=" + delaySeconds + " 当前线程=界面线程");
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }
        CaptureArea? selectedArea = await this.SelectRegionAreaAsync();
        if (selectedArea == null)
        {
            UiLogger.Log("截图", "区域截图已取消：用户没有完成框选");
            return CaptureOutcome.CreateCancelled("已取消区域截图");
        }
        await this.WaitForOverlayClosedAsync("区域截图");
        return await this.CaptureInternalAsync(CaptureTarget.Region, selectedArea.Value, 0);
    }

    /// <summary>使用上次区域截图；没有可用历史区域时回落到重新框选。</summary>
    public async Task<CaptureOutcome> CaptureLastRegionAsync(int delaySeconds)
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            return CaptureOutcome.CreateFailure("主窗口视图模型未就绪");
        }
        CaptureArea lastArea = new CaptureArea(mainViewModel.Settings.CaptureLastRegionX,
            mainViewModel.Settings.CaptureLastRegionY,
            mainViewModel.Settings.CaptureLastRegionWidth,
            mainViewModel.Settings.CaptureLastRegionHeight);
        if (!lastArea.IsValid)
        {
            UiLogger.Log("截图", "上次区域截图回落为重新框选：没有有效的历史区域");
            return await this.CaptureRegionAsync(delaySeconds);
        }
        return await this.CaptureInternalAsync(CaptureTarget.Region, lastArea, delaySeconds);
    }

    /// <summary>截图主流程：解析区域、调用平台截图、保存 PNG、写剪贴板并打开产物目录。</summary>
    private async Task<CaptureOutcome> CaptureInternalAsync(CaptureTarget target, CaptureArea area, int delaySeconds)
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            return CaptureOutcome.CreateFailure("主窗口视图模型未就绪，无法截图");
        }
        UiLogger.Log("截图", "开始截图：目标=" + DescribeTarget(target)
            + " 指定区域=" + (area.IsValid ? area.ToString() : "未指定")
            + " 延迟秒数=" + delaySeconds
            + " 包含鼠标指针=" + mainViewModel.Settings.CaptureIncludeCursor
            + " 复制到剪贴板=" + mainViewModel.Settings.CaptureCopyToClipboard
            + " 完成后打开产物文件夹=" + mainViewModel.Settings.CaptureOpenFolderAfter);
        if (delaySeconds > 0)
        {
            UiLogger.Log("截图", "截图进入延迟等待：延迟秒数=" + delaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }

        ScreenCaptureRequest request = new ScreenCaptureRequest();
        request.Target = target;
        request.Area = area;
        request.IncludeCursor = mainViewModel.Settings.CaptureIncludeCursor;
        request.ToolPath = mainViewModel.Settings.FfmpegPath;
        ScreenCaptureResult captureResult = await this.screenCaptureService.CaptureAsync(request);
        if (!captureResult.Success)
        {
            string failureMessage = "截图失败：" + captureResult.ErrorMessage;
            UiLogger.Log("截图", "截图失败：目标=" + DescribeTarget(target) + " 原因=" + captureResult.ErrorMessage);
            CaptureOutcome failure = CaptureOutcome.CreateFailure(failureMessage);
            this.CaptureFinished?.Invoke(failure);
            this.ShowFailure("截图失败", failureMessage);
            return failure;
        }
        UiLogger.Log("截图", "平台截图成功：目标=" + DescribeTarget(target)
            + " 图像宽度=" + captureResult.PixelWidth
            + " 图像高度=" + captureResult.PixelHeight
            + " 图像字节数=" + captureResult.ImageBytes.Length
            + " 实际区域=" + captureResult.Area);

        if (target == CaptureTarget.Region)
        {
            this.RememberLastRegion(area);
        }

        string outputDirectory = CapturePaths.ResolveOutputDirectory(mainViewModel.Settings);
        if (!CapturePaths.EnsureOutputDirectory(outputDirectory, out string directoryError))
        {
            UiLogger.Log("截图", "保存截图失败：目录=" + outputDirectory + " 原因=" + directoryError);
            CaptureOutcome failure = CaptureOutcome.CreateFailure(directoryError);
            this.CaptureFinished?.Invoke(failure);
            this.ShowFailure("保存截图失败", directoryError);
            return failure;
        }
        string screenshotPath = CapturePaths.CreateScreenshotPath(outputDirectory, DateTime.Now);
        try
        {
            await File.WriteAllBytesAsync(screenshotPath, captureResult.ImageBytes);
        }
        catch (Exception error)
        {
            string saveError = "写入截图文件失败：" + error.Message;
            UiLogger.Log("截图", "写入截图文件失败：路径=" + screenshotPath + " 原因=" + error.Message);
            CaptureOutcome failure = CaptureOutcome.CreateFailure(saveError);
            this.CaptureFinished?.Invoke(failure);
            this.ShowFailure("保存截图失败", saveError);
            return failure;
        }
        UiLogger.Log("截图", "截图文件已保存：路径=" + screenshotPath + " 字节数=" + captureResult.ImageBytes.Length);

        if (mainViewModel.Settings.CaptureCopyToClipboard)
        {
            bool copied = false;
            try
            {
                copied = await this.systemClipboardService.WriteAsync(new string[] { screenshotPath }, false);
            }
            catch (Exception error)
            {
                UiLogger.LogError("截图", "写入系统剪贴板异常：路径=" + screenshotPath, error);
            }
            UiLogger.Log("截图", "截图写入系统剪贴板：路径=" + screenshotPath + " 结果="
                + (copied ? "成功，可在资源管理器直接粘贴" : "失败，图片已保存在磁盘上"));
        }
        else
        {
            UiLogger.Log("截图", "截图未写入系统剪贴板：用户已关闭「截图后复制到剪贴板」");
        }

        CaptureOutcome outcome = CaptureOutcome.CreateSuccess(screenshotPath, "截图已保存：" + screenshotPath);
        this.CaptureFinished?.Invoke(outcome);
        if (mainViewModel.Settings.CaptureOpenFolderAfter)
        {
            this.OpenOutputFolderTab(outputDirectory);
        }
        else
        {
            UiLogger.Log("截图", "截图完成后未打开产物文件夹：用户已关闭「完成后自动打开产物文件夹」开关");
        }
        if (mainViewModel.Settings.CaptureOpenEditorAfter)
        {
            Dispatcher.UIThread.Post(delegate
            {
                this.OpenAnnotationEditor(screenshotPath, captureResult.Area);
            });
            UiLogger.Log("截图", "截图完成后已提交标注编辑器打开请求：图片路径=" + screenshotPath
                + " 截图区域=" + captureResult.Area);
        }
        else
        {
            UiLogger.Log("截图", "截图完成后未打开标注编辑器：用户已关闭「截图后打开标注编辑器」开关");
        }
        return outcome;
    }

    /// <summary>开始录屏：解析目标区域、创建输出文件并拉起 FFmpeg。</summary>
    public async Task<CaptureOutcome> StartRecordingAsync(int targetIndex)
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            return CaptureOutcome.CreateFailure("主窗口视图模型未就绪，无法录屏");
        }
        if (this.screenRecordingService.IsRecording)
        {
            UiLogger.Log("录屏", "开始录屏被拒绝：已有录制正在进行，当前输出路径=" + this.CurrentRecordingPath);
            return CaptureOutcome.CreateFailure("已有录制正在进行，请先停止当前录制");
        }
        CaptureTarget target = this.ResolveRecordingTarget(targetIndex);
        CaptureArea area = CaptureArea.Empty;
        if (target == CaptureTarget.Region)
        {
            CaptureArea? selectedArea = await this.SelectRegionAreaAsync();
            if (selectedArea == null)
            {
                UiLogger.Log("录屏", "区域录屏已取消：用户没有完成框选");
                return CaptureOutcome.CreateCancelled("已取消区域录屏");
            }
            await this.WaitForOverlayClosedAsync("区域录屏");
            area = selectedArea.Value;
        }
        if (target == CaptureTarget.ActiveWindow && !this.CaptureCapabilities.ActiveWindow)
        {
            string unsupported = "当前平台不支持活动窗口录制，请改用区域录制";
            UiLogger.Log("录屏", "开始录屏被拒绝：" + unsupported);
            this.ShowFailure("无法录屏", unsupported);
            return CaptureOutcome.CreateFailure(unsupported);
        }

        string outputDirectory = CapturePaths.ResolveOutputDirectory(mainViewModel.Settings);
        if (!CapturePaths.EnsureOutputDirectory(outputDirectory, out string directoryError))
        {
            UiLogger.Log("录屏", "开始录屏失败：目录=" + outputDirectory + " 原因=" + directoryError);
            this.ShowFailure("无法录屏", directoryError);
            return CaptureOutcome.CreateFailure(directoryError);
        }
        string recordingPath = CapturePaths.CreateRecordingPath(outputDirectory, DateTime.Now);
        ScreenRecordingOptions options = new ScreenRecordingOptions();
        options.Target = target;
        options.Area = area;
        options.OutputPath = recordingPath;
        options.FramesPerSecond = mainViewModel.Settings.CaptureRecordFps;
        options.Quality = mainViewModel.Settings.CaptureRecordQuality;
        options.IncludeCursor = mainViewModel.Settings.CaptureIncludeCursor;
        options.MicrophoneDevice = mainViewModel.Settings.CaptureMicrophoneDevice;
        options.FfmpegPath = mainViewModel.Settings.FfmpegPath;
        UiLogger.Log("录屏", "开始录屏：目标=" + DescribeTarget(target)
            + " 指定区域=" + (area.IsValid ? area.ToString() : "未指定")
            + " 输出路径=" + recordingPath
            + " 帧率=" + options.FramesPerSecond
            + " 画质参数=" + options.Quality
            + " 包含鼠标指针=" + options.IncludeCursor
            + " 麦克风设备=" + (string.IsNullOrEmpty(options.MicrophoneDevice) ? "未启用" : options.MicrophoneDevice)
            + " FFmpeg路径=" + (string.IsNullOrEmpty(options.FfmpegPath) ? "自动查找" : options.FfmpegPath));

        ScreenRecordingResult startResult = await this.screenRecordingService.StartAsync(options);
        if (!startResult.Success)
        {
            string failureMessage = "开始录屏失败：" + startResult.ErrorMessage;
            UiLogger.Log("录屏", "开始录屏失败：输出路径=" + recordingPath + " 原因=" + startResult.ErrorMessage);
            CaptureOutcome failure = CaptureOutcome.CreateFailure(failureMessage);
            this.CaptureFinished?.Invoke(failure);
            this.ShowFailure("录屏失败", failureMessage);
            return failure;
        }
        this.recordingStartedAt = DateTime.Now;
        this.wasRecording = true;
        this.StartRecordingWatchTimer();
        UiLogger.Log("录屏", "录屏已开始：输出路径=" + recordingPath + " 开始时间=" + this.recordingStartedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        this.RecordingStateChanged?.Invoke();
        return CaptureOutcome.CreateSuccess(recordingPath, "录屏已开始：" + recordingPath);
    }

    /// <summary>停止录屏：等待文件收尾，成功后按设置打开产物文件夹标签。</summary>
    public async Task<CaptureOutcome> StopRecordingAsync()
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            return CaptureOutcome.CreateFailure("主窗口视图模型未就绪，无法停止录屏");
        }
        if (!this.screenRecordingService.IsRecording)
        {
            this.StopRecordingWatchTimer();
            this.wasRecording = false;
            UiLogger.Log("录屏", "停止录屏被忽略：当前没有正在进行的录制");
            return CaptureOutcome.CreateFailure("当前没有正在进行的录制");
        }
        string recordingPath = this.screenRecordingService.CurrentOutputPath;
        UiLogger.Log("录屏", "请求停止录屏：输出路径=" + recordingPath
            + " 已录制时长=" + this.DescribeRecordingDuration());
        // 先停止轮询再等待编码进程收尾，避免轮询把这次主动停止误判为编码进程意外退出
        this.StopRecordingWatchTimer();
        this.wasRecording = false;
        ScreenRecordingResult stopResult = await this.screenRecordingService.StopAsync();
        this.RecordingStateChanged?.Invoke();
        if (!stopResult.Success)
        {
            string failureMessage = "停止录屏失败：" + stopResult.ErrorMessage;
            UiLogger.Log("录屏", "停止录屏失败：输出路径=" + recordingPath + " 原因=" + stopResult.ErrorMessage);
            CaptureOutcome failure = CaptureOutcome.CreateFailure(failureMessage);
            this.CaptureFinished?.Invoke(failure);
            this.ShowFailure("录屏失败", failureMessage);
            return failure;
        }
        string finalPath = string.IsNullOrEmpty(stopResult.OutputPath) ? recordingPath : stopResult.OutputPath;
        long fileSize = 0;
        try
        {
            FileInfo fileInfo = new FileInfo(finalPath);
            if (fileInfo.Exists)
            {
                fileSize = fileInfo.Length;
            }
        }
        catch
        {
            fileSize = 0;
        }
        UiLogger.Log("录屏", "录屏已结束：输出路径=" + finalPath
            + " 文件大小=" + CapturePaths.DescribeSize(fileSize)
            + " 录制时长=" + this.DescribeRecordingDuration());
        CaptureOutcome outcome = CaptureOutcome.CreateSuccess(finalPath, "录屏已保存：" + finalPath);
        this.CaptureFinished?.Invoke(outcome);
        if (mainViewModel.Settings.CaptureOpenFolderAfter)
        {
            string outputDirectory = Path.GetDirectoryName(finalPath) ?? CapturePaths.ResolveOutputDirectory(mainViewModel.Settings);
            this.OpenOutputFolderTab(outputDirectory);
        }
        else
        {
            UiLogger.Log("录屏", "录屏完成后未打开产物文件夹：用户已关闭「完成后自动打开产物文件夹」开关");
        }
        return outcome;
    }

    /// <summary>录屏开始 / 停止切换：录制中则停止，否则按目标开始。</summary>
    public async Task<CaptureOutcome> ToggleRecordingAsync(int targetIndex)
    {
        if (this.screenRecordingService.IsRecording)
        {
            return await this.StopRecordingAsync();
        }
        return await this.StartRecordingAsync(targetIndex);
    }

    /// <summary>轮询检测编码进程是否已意外退出，并据此收尾产物与界面状态。</summary>
    private void StartRecordingWatchTimer()
    {
        this.StopRecordingWatchTimer();
        DispatcherTimer timer = new DispatcherTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += this.OnRecordingWatchTick;
        this.recordingWatchTimer = timer;
        timer.Start();
    }

    /// <summary>停止录制状态轮询。</summary>
    private void StopRecordingWatchTimer()
    {
        DispatcherTimer? timer = this.recordingWatchTimer;
        if (timer == null)
        {
            return;
        }
        timer.Stop();
        timer.Tick -= this.OnRecordingWatchTick;
        this.recordingWatchTimer = null;
    }

    /// <summary>轮询回调：发现录制进程结束时按产物是否存在决定成功或失败。</summary>
    private void OnRecordingWatchTick(object? sender, EventArgs e)
    {
        if (!this.wasRecording)
        {
            this.StopRecordingWatchTimer();
            return;
        }
        if (this.screenRecordingService.IsRecording)
        {
            return;
        }
        this.wasRecording = false;
        this.StopRecordingWatchTimer();
        MainWindowViewModel? mainViewModel = this.viewModel;
        string recordingPath = this.screenRecordingService.CurrentOutputPath;
        UiLogger.Log("录屏", "检测到编码进程已退出：输出路径=" + recordingPath
            + " 录制时长=" + this.DescribeRecordingDuration());
        this.RecordingStateChanged?.Invoke();
        long recordingFileSize = 0;
        if (recordingPath.Length > 0 && File.Exists(recordingPath))
        {
            try
            {
                FileInfo recordingFile = new FileInfo(recordingPath);
                recordingFileSize = recordingFile.Length;
            }
            catch
            {
                recordingFileSize = 0;
            }
        }
        if (recordingFileSize > 0)
        {
            CaptureOutcome outcome = CaptureOutcome.CreateSuccess(recordingPath, "录屏已结束：" + recordingPath);
            this.CaptureFinished?.Invoke(outcome);
            if (mainViewModel != null && mainViewModel.Settings.CaptureOpenFolderAfter)
            {
                string outputDirectory = Path.GetDirectoryName(recordingPath) ?? CapturePaths.ResolveOutputDirectory(mainViewModel.Settings);
                this.OpenOutputFolderTab(outputDirectory);
            }
            return;
        }
        string failureMessage = "录屏进程已退出，但输出文件不存在或尚未生成";
        UiLogger.Log("录屏", "录屏异常结束：" + failureMessage + " 输出路径=" + recordingPath);
        CaptureOutcome failure = CaptureOutcome.CreateFailure(failureMessage);
        this.CaptureFinished?.Invoke(failure);
        this.ShowFailure("录屏失败", failureMessage);
    }

    /// <summary>弹出区域框选窗口并返回物理像素区域；用户取消或截图失败时返回空。</summary>
    private async Task<CaptureArea?> SelectRegionAreaAsync()
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            return null;
        }
        MainWindow? ownerWindow = WindowRegistry.Primary;
        Screen? screen = null;
        int pointerX = 0;
        int pointerY = 0;
        bool hasPointerPosition = this.screenRegionDetector.TryGetPointerPosition(out pointerX, out pointerY);
        if (hasPointerPosition && ownerWindow != null)
        {
            foreach (Screen candidateScreen in ownerWindow.Screens.All)
            {
                if (pointerX >= candidateScreen.Bounds.X
                    && pointerX < candidateScreen.Bounds.X + candidateScreen.Bounds.Width
                    && pointerY >= candidateScreen.Bounds.Y
                    && pointerY < candidateScreen.Bounds.Y + candidateScreen.Bounds.Height)
                {
                    screen = candidateScreen;
                    break;
                }
            }
        }
        if (screen == null && ownerWindow != null)
        {
            screen = ownerWindow.Screens.ScreenFromWindow(ownerWindow);
            if (screen == null)
            {
                screen = ownerWindow.Screens.Primary;
            }
        }
        if (screen == null)
        {
            UiLogger.Log("截图", "无法打开区域框选：没有可用的屏幕信息");
            this.ShowFailure("区域截图失败", "无法获取屏幕信息");
            return null;
        }
        ScreenBoundsInfo bounds = new ScreenBoundsInfo();
        bounds.Area = new CaptureArea(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
        bounds.Scaling = screen.Scaling;
        UiLogger.Log("截图", "准备区域框选：屏幕区域=" + bounds.Area + " 缩放比例=" + bounds.Scaling
            + " 鼠标位置可用=" + hasPointerPosition
            + " 鼠标横坐标=" + pointerX
            + " 鼠标纵坐标=" + pointerY
            + " 屏幕选择依据=" + (hasPointerPosition ? "鼠标所在显示器（未匹配到时回落主窗口所在显示器）" : "主窗口所在显示器"));

        ScreenCaptureRequest previewRequest = new ScreenCaptureRequest();
        previewRequest.Target = CaptureTarget.Region;
        previewRequest.Area = bounds.Area;
        previewRequest.IncludeCursor = false;
        previewRequest.ToolPath = mainViewModel.Settings.FfmpegPath;
        ScreenCaptureResult preview = await this.screenCaptureService.CaptureAsync(previewRequest);
        if (!preview.Success)
        {
            UiLogger.Log("截图", "区域框选失败：无法获取屏幕预览图 原因=" + preview.ErrorMessage);
            this.ShowFailure("区域截图失败", preview.ErrorMessage);
            return null;
        }
        IReadOnlyList<ScreenRegionCandidate> regionCandidates = this.DetectRegionCandidates(bounds.Area);
        Bitmap? bitmap = null;
        bool selectionWindowCreated = false;
        try
        {
            using (MemoryStream imageStream = new MemoryStream(preview.ImageBytes))
            {
                bitmap = new Bitmap(imageStream);
            }
            RegionSelectionWindow selectionWindow = new RegionSelectionWindow(bitmap, bounds.Area, bounds.Scaling,
                regionCandidates, this.screenRegionDetector);
            selectionWindowCreated = true;
            CaptureArea? selected = await selectionWindow.SelectAreaAsync();
            if (selected == null)
            {
                return null;
            }
            UiLogger.Log("截图", "区域框选完成：选择区域=" + selected.Value);
            return selected;
        }
        catch (Exception error)
        {
            if (bitmap != null && !selectionWindowCreated)
            {
                bitmap.Dispose();
            }
            UiLogger.LogError("截图", "区域框选窗口异常", error);
            this.ShowFailure("区域截图失败", "无法打开区域框选窗口：" + error.Message);
            return null;
        }
    }

    /// <summary>枚举当前屏幕上的可预选区域；平台不支持时返回只含全屏的空列表，框选窗口降级为手工拖动。</summary>
    private IReadOnlyList<ScreenRegionCandidate> DetectRegionCandidates(CaptureArea screenArea)
    {
        try
        {
            IReadOnlyList<ScreenRegionCandidate> candidates = this.screenRegionDetector.Detect(screenArea);
            int windowCount = 0;
            int childViewCount = 0;
            foreach (ScreenRegionCandidate candidate in candidates)
            {
                if (candidate.Kind == ScreenRegionKind.Window)
                {
                    windowCount++;
                }
                else if (candidate.Kind == ScreenRegionKind.ChildView)
                {
                    childViewCount++;
                }
            }
            UiLogger.Log("截图", "区域预选枚举：平台支持=" + this.screenRegionDetector.IsSupported
                + " 屏幕区域=" + screenArea
                + " 候选区域条数=" + candidates.Count
                + " 窗口数量=" + windowCount
                + " 子视图数量=" + childViewCount
                + " 结果=成功");
            return candidates;
        }
        catch (Exception error)
        {
            UiLogger.LogError("截图", "区域预选枚举异常：平台支持=" + this.screenRegionDetector.IsSupported
                + " 结果=失败，回落为仅全屏候选", error);
            List<ScreenRegionCandidate> fallback = new List<ScreenRegionCandidate>();
            ScreenRegionCandidate fullScreen = new ScreenRegionCandidate();
            fullScreen.Area = screenArea;
            fullScreen.Kind = ScreenRegionKind.FullScreen;
            fullScreen.Name = "全屏（当前显示器）";
            fullScreen.Depth = 0;
            fullScreen.ZOrder = -1;
            fallback.Add(fullScreen);
            return fallback;
        }
    }

    /// <summary>等待框选窗口完全从屏幕消失，避免把遮罩边框截进最终图片或录像。</summary>
    private async Task WaitForOverlayClosedAsync(string actionName)
    {
        UiLogger.Log("截图", "等待框选窗口从屏幕消失：" + actionName + " 等待毫秒=220");
        await Task.Delay(220);
    }

    /// <summary>保存上次区域截图范围，供「上次区域」快速复用。</summary>
    private void RememberLastRegion(CaptureArea area)
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null || !area.IsValid)
        {
            return;
        }
        mainViewModel.Settings.CaptureLastRegionX = area.X;
        mainViewModel.Settings.CaptureLastRegionY = area.Y;
        mainViewModel.Settings.CaptureLastRegionWidth = area.Width;
        mainViewModel.Settings.CaptureLastRegionHeight = area.Height;
        this.settingsStore.Save(mainViewModel.Settings);
        UiLogger.Log("截图", "已记录上次区域截图范围：区域=" + area + " 结果=已保存到设置");
    }

    /// <summary>在程序窗口内打开产物文件夹标签；同一路径的标签已存在时直接选中，不重复新建。</summary>
    public void OpenOutputFolderTab(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            UiLogger.Log("截图录屏", "打开产物文件夹被跳过：目录为空");
            return;
        }
        MainWindow? window = WindowRegistry.Primary;
        if (window != null)
        {
            if (!window.IsVisible)
            {
                window.Show();
            }
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
        }
        MainWindowViewModel? mainViewModel = window == null ? this.viewModel : window.DataContext as MainWindowViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("截图录屏", "打开产物文件夹失败：没有可用的主窗口视图模型 目录=" + directory);
            return;
        }
        if (mainViewModel.IsIdeMode)
        {
            UiLogger.Log("截图录屏", "当前处于 IDE 模式，先退出 IDE 模式以便显示产物文件夹标签");
            mainViewModel.ExitIdeMode();
        }
        mainViewModel.OpenFolderTab(directory);
        UiLogger.Log("截图录屏", "已自动打开产物文件夹标签：目录=" + directory
            + " 标签总数=" + mainViewModel.Tabs.Count);
    }

    /// <summary>打开截图录屏标签页（已存在时直接选中）。</summary>
    public void OpenCaptureTab()
    {
        MainWindow? window = WindowRegistry.Primary;
        if (window != null)
        {
            if (!window.IsVisible)
            {
                window.Show();
            }
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
        }
        MainWindowViewModel? mainViewModel = window == null ? this.viewModel : window.DataContext as MainWindowViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("截图录屏", "打开截图录屏标签失败：没有可用的主窗口视图模型");
            return;
        }
        if (mainViewModel.IsIdeMode)
        {
            UiLogger.Log("截图录屏", "当前处于 IDE 模式，先退出 IDE 模式以便显示截图录屏标签");
            mainViewModel.ExitIdeMode();
        }
        mainViewModel.OpenCaptureTab();
    }

    /// <summary>打开标注编辑器（吸收 Snipaste 截图编辑流程）：装载截图、标注、取色、识别与贴图。</summary>
    public void OpenAnnotationEditor(string filePath, CaptureArea area)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            UiLogger.Log("截图标注", "打开标注编辑器被拒绝：文件不存在 路径=" + filePath);
            return;
        }
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("截图标注", "打开标注编辑器被拒绝：主窗口视图模型未就绪 路径=" + filePath);
            return;
        }
        ITextRecognitionService recognitionService = this.ResolveOcrService();
        CaptureEditorWindow.Open(filePath, area, mainViewModel.Settings, recognitionService,
            WindowRegistry.Primary);
        UiLogger.Log("截图标注", "标注编辑器打开请求已提交：图片路径=" + filePath
            + " 截图区域=" + area
            + " 识别引擎=" + recognitionService.ProviderName
            + " 识别可用=" + recognitionService.IsAvailable);
    }

    /// <summary>对已有图片打开标注编辑器（历史产物右键使用），贴图位置取主屏左上角。</summary>
    public void OpenAnnotationEditorForFile(string filePath)
    {
        CaptureArea area = CaptureArea.Empty;
        MainWindow? window = WindowRegistry.Primary;
        if (window != null && window.Screens.Primary != null)
        {
            Screen primary = window.Screens.Primary;
            area = new CaptureArea(primary.Bounds.X, primary.Bounds.Y,
                primary.Bounds.Width, primary.Bounds.Height);
        }
        this.OpenAnnotationEditor(filePath, area);
    }

    /// <summary>
    /// 解析当前可用的文字识别引擎：优先使用平台内置实现（Windows 内置识别），
    /// 否则回退到按设置构造的 Tesseract；两者都不可用时返回平台实现并附带能力说明。
    /// </summary>
    private ITextRecognitionService ResolveOcrService()
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        string configuredPath = mainViewModel == null ? string.Empty : mainViewModel.Settings.OcrTesseractPath;
        string language = mainViewModel == null ? string.Empty : mainViewModel.Settings.OcrLanguage;
        if (this.ocrService.IsAvailable)
        {
            return this.ocrService;
        }
        TesseractTextRecognitionService tesseract = new TesseractTextRecognitionService(configuredPath, language);
        if (tesseract.IsAvailable)
        {
            UiLogger.Log("截图标注", "文字识别引擎解析：内置引擎不可用，改用 Tesseract 路径="
                + tesseract.DescribeAvailability());
            return tesseract;
        }
        UiLogger.Log("截图标注", "文字识别引擎解析：没有可用引擎 内置说明=" + this.ocrService.DescribeAvailability());
        return this.ocrService;
    }

    /// <summary>打开全屏取色器：冻结整屏画面，移动鼠标查看颜色，单击复制十六进制颜色。</summary>
    public async Task<CaptureOutcome> OpenScreenColorPickerAsync()
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        MainWindow? ownerWindow = WindowRegistry.Primary;
        if (mainViewModel == null || ownerWindow == null)
        {
            UiLogger.Log("屏幕取色", "打开取色器被拒绝：主窗口或视图模型未就绪");
            return CaptureOutcome.CreateFailure("主窗口尚未就绪，无法打开取色器");
        }
        Screen? screen = ownerWindow.Screens.ScreenFromWindow(ownerWindow);
        if (screen == null)
        {
            screen = ownerWindow.Screens.Primary;
        }
        if (screen == null)
        {
            UiLogger.Log("屏幕取色", "打开取色器被拒绝：无法获取屏幕信息");
            return CaptureOutcome.CreateFailure("无法获取屏幕信息");
        }
        CaptureArea area = new CaptureArea(screen.Bounds.X, screen.Bounds.Y,
            screen.Bounds.Width, screen.Bounds.Height);
        ScreenCaptureRequest request = new ScreenCaptureRequest();
        request.Target = CaptureTarget.Region;
        request.Area = area;
        request.IncludeCursor = false;
        request.ToolPath = mainViewModel.Settings.FfmpegPath;
        UiLogger.Log("屏幕取色", "正在获取整屏画面：屏幕区域=" + area
            + " 缩放比例=" + screen.Scaling);
        ScreenCaptureResult captureResult = await this.screenCaptureService.CaptureAsync(request);
        if (!captureResult.Success)
        {
            UiLogger.Log("屏幕取色", "获取整屏画面失败：" + captureResult.ErrorMessage);
            this.ShowFailure("取色器无法打开", captureResult.ErrorMessage);
            return CaptureOutcome.CreateFailure(captureResult.ErrorMessage);
        }
        Bitmap bitmap;
        try
        {
            using (MemoryStream stream = new MemoryStream(captureResult.ImageBytes))
            {
                bitmap = new Bitmap(stream);
            }
        }
        catch (Exception error)
        {
            UiLogger.LogError("屏幕取色", "整屏画面解码失败", error);
            return CaptureOutcome.CreateFailure("整屏画面解码失败：" + error.Message);
        }
        ScreenColorPickerWindow window = new ScreenColorPickerWindow(bitmap, area, screen.Scaling);
        window.ShowPicker();
        UiLogger.Log("屏幕取色", "取色器已打开：屏幕区域=" + area
            + " 图像宽度=" + captureResult.PixelWidth
            + " 图像高度=" + captureResult.PixelHeight);
        return CaptureOutcome.CreateSuccess(string.Empty, "取色器已打开，单击复制颜色");
    }

    /// <summary>贴图（剪贴板）：把剪贴板中的图片贴到主屏左上角，可拖动、缩放、调透明度。</summary>
    public async Task<CaptureOutcome> PinClipboardImageAsync()
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        MainWindow? window = WindowRegistry.Primary;
        if (mainViewModel == null || window == null)
        {
            UiLogger.Log("贴图", "剪贴板贴图被拒绝：主窗口或视图模型未就绪");
            return CaptureOutcome.CreateFailure("主窗口尚未就绪，无法贴图");
        }
        IClipboard? clipboard = window.Clipboard;
        Bitmap? bitmap = null;
        if (clipboard != null)
        {
            try
            {
                bitmap = await clipboard.TryGetBitmapAsync();
            }
            catch (Exception error)
            {
                UiLogger.LogError("贴图", "读取剪贴板图片失败", error);
            }
        }
        if (bitmap == null)
        {
            UiLogger.Log("贴图", "剪贴板贴图被拒绝：剪贴板中没有图片");
            this.ShowFailure("无法贴图", "系统剪贴板中没有可用图片。\n请先复制一张图片，或使用截图后直接点击「贴图」。");
            return CaptureOutcome.CreateFailure("系统剪贴板中没有可用图片");
        }
        PixelPoint position = new PixelPoint(60, 60);
        Screen? screen = window.Screens.Primary;
        if (screen != null)
        {
            PixelRect workingArea = screen.WorkingArea;
            position = new PixelPoint(workingArea.X + 60, workingArea.Y + 60);
        }
        PinWindowManager.ShowPinnedImage(bitmap, position, mainViewModel.Settings);
        UiLogger.Log("贴图", "剪贴板图片已贴到屏幕：像素宽度=" + bitmap.PixelSize.Width
            + " 像素高度=" + bitmap.PixelSize.Height
            + " 贴图横坐标=" + position.X
            + " 贴图纵坐标=" + position.Y);
        return CaptureOutcome.CreateSuccess(string.Empty, "剪贴板图片已贴到屏幕");
    }

    /// <summary>在媒体标签中打开截图 / 录像；非媒体文件交给系统默认程序。</summary>
    public void OpenCapturedFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            UiLogger.Log("截图录屏", "打开产物被拒绝：文件不存在 路径=" + filePath);
            return;
        }
        MainWindow? window = WindowRegistry.Primary;
        if (window != null)
        {
            if (!window.IsVisible)
            {
                window.Show();
            }
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
        }
        MainWindowViewModel? mainViewModel = window == null ? this.viewModel : window.DataContext as MainWindowViewModel;
        if (mainViewModel == null)
        {
            UiLogger.Log("截图录屏", "打开产物失败：没有可用的主窗口视图模型 路径=" + filePath);
            return;
        }
        if (mainViewModel.IsIdeMode)
        {
            UiLogger.Log("截图录屏", "当前处于 IDE 模式，先退出 IDE 模式以便在媒体标签打开产物");
            mainViewModel.ExitIdeMode();
        }
        MediaKind kind = MediaFormatCatalog.Classify(filePath);
        if (kind == MediaKind.Unknown)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                startInfo.FileName = filePath;
                startInfo.UseShellExecute = true;
                System.Diagnostics.Process.Start(startInfo);
                UiLogger.Log("截图录屏", "产物不是图片或视频，已交给系统默认程序：路径=" + filePath);
            }
            catch (Exception error)
            {
                UiLogger.LogError("截图录屏", "交给系统默认程序打开失败：" + filePath, error);
                this.ShowFailure("打开失败", "无法打开产物文件：" + error.Message);
            }
            return;
        }
        mainViewModel.OpenMediaTab(filePath, kind);
        UiLogger.Log("截图录屏", "已在媒体标签打开产物：路径=" + filePath + " 媒体类型=" + MediaFormatCatalog.DescribeKind(kind));
    }

    /// <summary>读取产物目录中的截图与录屏文件，按修改时间倒序返回完整路径列表。</summary>
    public string[] ListCaptureFiles()
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            return Array.Empty<string>();
        }
        string directory = CapturePaths.ResolveOutputDirectory(mainViewModel.Settings);
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }
        string[] files;
        try
        {
            files = Directory.GetFiles(directory);
        }
        catch (Exception error)
        {
            UiLogger.LogError("截图录屏", "读取产物目录失败：目录=" + directory, error);
            return Array.Empty<string>();
        }
        FileInfo[] infos = new FileInfo[files.Length];
        int count = 0;
        foreach (string file in files)
        {
            if (!CapturePaths.IsCaptureFile(file))
            {
                continue;
            }
            infos[count] = new FileInfo(file);
            count++;
        }
        Array.Resize(ref infos, count);
        Array.Sort(infos, delegate (FileInfo left, FileInfo right)
        {
            return right.LastWriteTime.CompareTo(left.LastWriteTime);
        });
        string[] paths = new string[infos.Length];
        for (int index = 0; index < infos.Length; index++)
        {
            paths[index] = infos[index].FullName;
        }
        UiLogger.Log("截图录屏", "读取产物目录完成：目录=" + directory + " 产物数量=" + paths.Length);
        return paths;
    }

    /// <summary>保存设置（仅截图录屏相关修改，避免触发全部文件标签刷新）。</summary>
    public void PersistSettings(string reason)
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            return;
        }
        this.settingsStore.Save(mainViewModel.Settings);
        UiLogger.Log("配置更改", "截图录屏设置已保存：原因=" + reason
            + " 保存目录=" + CapturePaths.ResolveOutputDirectory(mainViewModel.Settings)
            + " 复制到剪贴板=" + mainViewModel.Settings.CaptureCopyToClipboard
            + " 完成打开文件夹=" + mainViewModel.Settings.CaptureOpenFolderAfter
            + " 包含鼠标指针=" + mainViewModel.Settings.CaptureIncludeCursor
            + " 延迟秒数=" + mainViewModel.Settings.CaptureDelaySeconds
            + " 录屏帧率=" + mainViewModel.Settings.CaptureRecordFps
            + " 录屏画质=" + mainViewModel.Settings.CaptureRecordQuality
            + " 录屏目标=" + mainViewModel.Settings.CaptureRecordTarget
            + " 麦克风设备=" + (string.IsNullOrEmpty(mainViewModel.Settings.CaptureMicrophoneDevice) ? "未启用" : mainViewModel.Settings.CaptureMicrophoneDevice)
            + " FFmpeg路径=" + (string.IsNullOrEmpty(mainViewModel.Settings.FfmpegPath) ? "自动查找" : mainViewModel.Settings.FfmpegPath));
    }

    /// <summary>描述当前平台能力与 FFmpeg 状态，供设置页与截图录屏标签页展示。</summary>
    public string DescribeEnvironment()
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        string ffmpegPath = FfmpegLocator.Find(mainViewModel == null ? string.Empty : mainViewModel.Settings.FfmpegPath) ?? string.Empty;
        ScreenCaptureCapabilities capabilities = this.CaptureCapabilities;
        string captureText = "截图能力=全部屏幕:" + (capabilities.FullScreen ? "支持" : "不支持")
            + "，主屏:" + (capabilities.PrimaryScreen ? "支持" : "不支持")
            + "，区域:" + (capabilities.Region ? "支持" : "不支持")
            + "，活动窗口:" + (capabilities.ActiveWindow ? "支持" : "不支持");
        string recordText = this.screenRecordingService.IsSupported
            ? "录屏能力=" + (ffmpegPath.Length > 0 ? "可用" : "缺少 FFmpeg，暂不可用")
            : "录屏能力=当前平台不支持";
        string ffmpegText = ffmpegPath.Length > 0 ? "FFmpeg=" + ffmpegPath : "FFmpeg=未找到";
        string hotkeyText = this.globalHotkeyService.IsSupported ? this.hotkeyStatusText : "全局快捷键=不可用";
        return captureText + "；" + recordText + "；" + ffmpegText + "；" + hotkeyText;
    }

    /// <summary>把录屏目标索引解析为截图目标常量。</summary>
    public CaptureTarget ResolveRecordingTarget(int targetIndex)
    {
        if (targetIndex == 1)
        {
            return CaptureTarget.Region;
        }
        if (targetIndex == 2)
        {
            return CaptureTarget.ActiveWindow;
        }
        return CaptureTarget.FullScreen;
    }

    /// <summary>把录屏目标字符串解析为索引，供设置持久化的下拉框使用。</summary>
    public int ResolveRecordingTargetIndex(string targetText)
    {
        if (string.Equals(targetText, "Region", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        if (string.Equals(targetText, "ActiveWindow", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        return 0;
    }

    /// <summary>把截图目标翻译成日志用中文名称。</summary>
    public static string DescribeTarget(CaptureTarget target)
    {
        if (target == CaptureTarget.PrimaryScreen)
        {
            return "主屏";
        }
        if (target == CaptureTarget.Region)
        {
            return "指定区域";
        }
        if (target == CaptureTarget.ActiveWindow)
        {
            return "活动窗口";
        }
        return "全部屏幕";
    }

    /// <summary>返回当前录制的已用时长文字（时:分:秒）。</summary>
    public string DescribeRecordingDuration()
    {
        if (this.recordingStartedAt == DateTime.MinValue)
        {
            return "00:00:00";
        }
        TimeSpan duration = DateTime.Now - this.recordingStartedAt;
        if (duration.TotalSeconds < 0)
        {
            duration = TimeSpan.Zero;
        }
        return duration.ToString(@"hh\:mm\:ss");
    }

    /// <summary>显示失败提示并把主窗口带到前台，保证全局热键触发的失败也能被看到。</summary>
    private void ShowFailure(string title, string message)
    {
        MainWindow? window = WindowRegistry.Primary;
        if (window != null)
        {
            if (!window.IsVisible)
            {
                window.Show();
            }
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
        }
        MessageDialog.Show(window, title, message);
    }

    /// <summary>释放全局快捷键与录制进程。</summary>
    public void Dispose()
    {
        this.StopRecordingWatchTimer();
        this.globalHotkeyService.UnregisterAll();
        if (this.globalHotkeyService is IDisposable hotkeyDisposable)
        {
            hotkeyDisposable.Dispose();
        }
        if (this.screenRecordingService is IDisposable recordingDisposable)
        {
            recordingDisposable.Dispose();
        }
        UiLogger.Log("截图录屏", "协调器已释放：全局快捷键已注销，录制进程已收尾");
    }
}
