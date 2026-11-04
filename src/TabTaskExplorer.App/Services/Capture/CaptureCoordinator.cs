using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
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
    private readonly IClipboardImageWriter clipboardImageWriter;
    private readonly ITextRecognitionService ocrService;
    private readonly IScreenRegionDetector screenRegionDetector;
    private readonly ICaptureCancelInput captureCancelInput;
    private readonly IForegroundWindowService foregroundWindowService;
    private readonly AppSettingsStore settingsStore = new AppSettingsStore();
    private IntPtr pendingForegroundWindow;
    private MainWindowViewModel? viewModel;
    private DispatcherTimer? recordingWatchTimer;
    private DateTime recordingStartedAt = DateTime.MinValue;
    private bool wasRecording;
    private string hotkeyStatusText = "全局快捷键：尚未注册";

    public CaptureCoordinator(IScreenCaptureService screenCaptureService,
        IScreenRecordingService screenRecordingService,
        IGlobalHotkeyService globalHotkeyService,
        ISystemClipboardService systemClipboardService,
        IClipboardImageWriter clipboardImageWriter,
        IScreenRegionDetector screenRegionDetector,
        ITextRecognitionService textRecognitionService,
        ICaptureCancelInput captureCancelInput,
        IForegroundWindowService foregroundWindowService)
    {
        this.screenCaptureService = screenCaptureService;
        this.screenRecordingService = screenRecordingService;
        this.globalHotkeyService = globalHotkeyService;
        this.systemClipboardService = systemClipboardService;
        this.clipboardImageWriter = clipboardImageWriter;
        this.screenRegionDetector = screenRegionDetector;
        this.ocrService = textRecognitionService;
        this.captureCancelInput = captureCancelInput;
        this.foregroundWindowService = foregroundWindowService;
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
        else if (string.Equals(actionName, "ScreenshotPin", StringComparison.Ordinal))
        {
            _ = this.CapturePinRegionAsync(mainViewModel.Settings.CaptureDelaySeconds);
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
        return await this.CaptureInternalAsync(target, CaptureArea.Empty, delaySeconds,
            DescribeCaptureMode(target));
    }

    /// <summary>执行区域截图：先弹出全屏框选窗口，用户取消时返回取消结果。</summary>
    public async Task<CaptureOutcome> CaptureRegionAsync(int delaySeconds)
    {
        CaptureCancelSource cancelSource = await this.WaitForCancelablePhaseAsync(delaySeconds, "区域截图", "延迟等待");
        if (cancelSource != CaptureCancelSource.None)
        {
            return this.CreateCaptureCancelOutcome(cancelSource, "区域截图", "延迟等待");
        }
        CaptureArea? selectedArea = await this.SelectRegionAreaAsync("区域截图", false);
        if (selectedArea == null)
        {
            UiLogger.Log("截图", "区域截图已取消：用户没有完成框选");
            return CaptureOutcome.CreateCancelled("已取消区域截图");
        }
        MainWindowCaptureState? hideState = this.PrepareMainWindowForCapture(CaptureTarget.Region, selectedArea.Value, "区域截图");
        cancelSource = await this.WaitForOverlayClosedAsync("区域截图", "区域截图");
        if (cancelSource != CaptureCancelSource.None)
        {
            this.RestoreMainWindowAfterCapture(hideState, "区域截图选区");
            return this.CreateCaptureCancelOutcome(cancelSource, "区域截图", "遮罩关闭后");
        }
        CaptureOutcome regionOutcome = await this.CaptureInternalAsync(CaptureTarget.Region, selectedArea.Value, 0, "区域截图", false);
        this.RestoreMainWindowAfterCapture(hideState, "区域截图选区");
        return regionOutcome;
    }

    /// <summary>
    /// 「贴图截图」：与区域截图完全相同的选区流程（窗口自动识别、Tab/滚轮层级切换），
    /// 确认后用同一份产物生成贴图。默认先按产物规则保存文件（进入产物历史），再用该文件生成贴图；
    /// 关闭设置项后则只在内存中生成贴图，不落盘、不进入历史。
    /// </summary>
    public async Task<CaptureOutcome> CapturePinRegionAsync(int delaySeconds)
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            return CaptureOutcome.CreateFailure("主窗口视图模型未就绪，无法贴图截图");
        }
        UiLogger.Log("截图", "开始贴图截图：延迟秒数=" + delaySeconds
            + " 保存产物=" + mainViewModel.Settings.CapturePinShotSaveFile
            + " 包含鼠标指针=" + mainViewModel.Settings.CaptureIncludeCursor);
        CaptureCancelSource cancelSource = await this.WaitForCancelablePhaseAsync(delaySeconds, "贴图截图", "延迟等待");
        if (cancelSource != CaptureCancelSource.None)
        {
            return this.CreateCaptureCancelOutcome(cancelSource, "贴图截图", "延迟等待");
        }
        CaptureArea? selectedArea = await this.SelectRegionAreaAsync("贴图截图", false);
        if (selectedArea == null)
        {
            UiLogger.Log("截图", "贴图截图已取消：用户没有完成框选");
            return CaptureOutcome.CreateCancelled("已取消贴图截图");
        }
        cancelSource = await this.WaitForOverlayClosedAsync("贴图截图", "贴图截图");
        if (cancelSource != CaptureCancelSource.None)
        {
            return this.CreateCaptureCancelOutcome(cancelSource, "贴图截图", "遮罩关闭后");
        }

        ScreenCaptureRequest request = new ScreenCaptureRequest();
        request.Target = CaptureTarget.Region;
        request.Area = selectedArea.Value;
        request.IncludeCursor = mainViewModel.Settings.CaptureIncludeCursor;
        request.ToolPath = mainViewModel.Settings.FfmpegPath;
        MainWindowCaptureState? pinHideState = this.PrepareMainWindowForCapture(CaptureTarget.Region, selectedArea.Value, "贴图截图");
        ScreenCaptureResult captureResult = await this.screenCaptureService.CaptureAsync(request);
        this.RestoreMainWindowAfterCapture(pinHideState, "贴图截图");
        if (!captureResult.Success)
        {
            string failureMessage = "贴图截图失败：" + captureResult.ErrorMessage;
            UiLogger.Log("截图", "贴图截图失败：区域=" + selectedArea.Value + " 原因=" + captureResult.ErrorMessage);
            CaptureOutcome failure = CaptureOutcome.CreateFailure(failureMessage);
            this.CaptureFinished?.Invoke(failure);
            this.ShowFailure("贴图截图失败", failureMessage);
            return failure;
        }
        cancelSource = this.captureCancelInput.PollCancelRequest();
        if (cancelSource != CaptureCancelSource.None)
        {
            return this.CreateCaptureCancelOutcome(cancelSource, "贴图截图", "截图完成前");
        }
        this.RememberLastRegion(selectedArea.Value);
        UiLogger.Log("截图", "贴图截图平台截图成功：区域=" + captureResult.Area
            + " 图像宽度=" + captureResult.PixelWidth
            + " 图像高度=" + captureResult.PixelHeight
            + " 图像字节数=" + captureResult.ImageBytes.Length);

        bool saveFile = mainViewModel.Settings.CapturePinShotSaveFile;
        string savedPath = string.Empty;
        Bitmap bitmap;
        if (saveFile)
        {
            string outputDirectory = CapturePaths.ResolveOutputDirectory(mainViewModel.Settings);
            if (!CapturePaths.EnsureOutputDirectory(outputDirectory, out string directoryError))
            {
                UiLogger.Log("截图", "贴图截图保存失败：目录=" + outputDirectory + " 原因=" + directoryError);
                CaptureOutcome directoryFailure = CaptureOutcome.CreateFailure(directoryError);
                this.CaptureFinished?.Invoke(directoryFailure);
                this.ShowFailure("保存贴图截图失败", directoryError);
                return directoryFailure;
            }
            string screenshotPath = CapturePaths.CreateScreenshotPath(outputDirectory, DateTime.Now);
            try
            {
                await File.WriteAllBytesAsync(screenshotPath, captureResult.ImageBytes);
                // 贴图必须与产物文件同源：直接从刚写入的文件解码，不做二次编码或额外落盘
                using (FileStream stream = new FileStream(screenshotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bitmap = new Bitmap(stream);
                }
                savedPath = screenshotPath;
                UiLogger.Log("截图", "贴图截图产物已保存：路径=" + screenshotPath
                    + " 字节数=" + captureResult.ImageBytes.Length
                    + " 结果=贴图将使用同一份文件");
            }
            catch (Exception error)
            {
                UiLogger.LogError("截图", "贴图截图保存或读取产物失败：路径=" + screenshotPath, error);
                CaptureOutcome saveFailure = CaptureOutcome.CreateFailure("保存贴图截图失败：" + error.Message);
                this.CaptureFinished?.Invoke(saveFailure);
                this.ShowFailure("保存贴图截图失败", "写入或读取截图文件失败：" + error.Message);
                return saveFailure;
            }
        }
        else
        {
            try
            {
                using (MemoryStream imageStream = new MemoryStream(captureResult.ImageBytes))
                {
                    bitmap = new Bitmap(imageStream);
                }
                UiLogger.Log("截图", "贴图截图未保存产物：图像只存在于内存，像素宽度=" + bitmap.PixelSize.Width
                    + " 像素高度=" + bitmap.PixelSize.Height);
            }
            catch (Exception error)
            {
                UiLogger.LogError("截图", "贴图截图内存位图解码失败", error);
                CaptureOutcome decodeFailure = CaptureOutcome.CreateFailure("贴图截图解码失败：" + error.Message);
                this.CaptureFinished?.Invoke(decodeFailure);
                this.ShowFailure("贴图截图失败", "截图像素解码失败：" + error.Message);
                return decodeFailure;
            }
        }

        cancelSource = this.captureCancelInput.PollCancelRequest();
        if (cancelSource != CaptureCancelSource.None)
        {
            bitmap.Dispose();
            this.DeleteCancelledProduct(savedPath, "贴图截图");
            return this.CreateCaptureCancelOutcome(cancelSource, "贴图截图", "贴图创建前");
        }

        PixelPoint pinPosition = this.ResolvePinPlacement(captureResult.Area, captureResult.PixelWidth, captureResult.PixelHeight);
        try
        {
            PinWindowManager.ShowPinnedImage(bitmap, pinPosition, mainViewModel.Settings,
                this.ResolveOcrService(), false);
        }
        catch (Exception error)
        {
            UiLogger.LogError("贴图", "贴图截图创建贴图窗口失败：区域=" + captureResult.Area, error);
            CaptureOutcome pinFailure = CaptureOutcome.CreateFailure("创建贴图窗口失败：" + error.Message);
            this.CaptureFinished?.Invoke(pinFailure);
            this.ShowFailure("贴图截图失败", "创建贴图窗口失败：" + error.Message);
            return pinFailure;
        }

        if (saveFile)
        {
            UiLogger.Log("截图", "贴图截图不自动写剪贴板：复制改由产物历史或完成提示的显式按钮触发 产物路径=" + savedPath);
            string outputDirectory = Path.GetDirectoryName(savedPath) ?? CapturePaths.ResolveOutputDirectory(mainViewModel.Settings);
            CaptureOutcome outcome = CaptureOutcome.CreateSuccess(savedPath, "贴图截图已保存并贴到屏幕：" + savedPath);
            this.CaptureFinished?.Invoke(outcome);
            if (mainViewModel.Settings.CaptureOpenFolderAfter)
            {
                this.OpenOutputFolderTab(outputDirectory, false);
            }
            UiLogger.Log("截图", "贴图截图完成：产物路径=" + savedPath
                + " 贴图源=同一产物文件"
                + " 贴图位置横坐标=" + pinPosition.X
                + " 贴图位置纵坐标=" + pinPosition.Y
                + " 自动复制到剪贴板=否");
            this.FinishCapturePresentation("贴图截图");
            return outcome;
        }

        CaptureOutcome pinOnlyOutcome = CaptureOutcome.CreateSuccess(string.Empty, "贴图截图已贴到屏幕（未保存产物）");
        this.CaptureFinished?.Invoke(pinOnlyOutcome);
        UiLogger.Log("截图", "贴图截图完成：产物不落盘 贴图源=内存位图"
            + " 贴图位置横坐标=" + pinPosition.X
            + " 贴图位置纵坐标=" + pinPosition.Y);
        return pinOnlyOutcome;
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
        return await this.CaptureInternalAsync(CaptureTarget.Region, lastArea, delaySeconds, "上次区域截图");
    }

    /// <summary>
    /// 显式复制产物到剪贴板（用户点击按钮或网页端调用，绝不由截图流程自动触发）：
    /// 多选时一次写入全部文件路径，并把其中第一张图片写成 PNG / CF_DIBV5 / CF_DIB 位图。
    /// </summary>
    public async Task<CaptureOutcome> CopyCaptureFilesToClipboardAsync(IReadOnlyList<string> filePaths, string sceneName)
    {
        List<string> existingPaths = new List<string>();
        if (filePaths != null)
        {
            foreach (string path in filePaths)
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    existingPaths.Add(path);
                }
            }
        }
        if (existingPaths.Count == 0)
        {
            UiLogger.Log("系统剪贴板", sceneName + "显式复制被拒绝：没有可复制的产物文件");
            return CaptureOutcome.CreateFailure("没有可复制的产物文件");
        }
        string imagePath = string.Empty;
        foreach (string path in existingPaths)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg"
                || extension == ".bmp" || extension == ".gif" || extension == ".webp")
            {
                imagePath = path;
                break;
            }
        }
        bool copied = false;
        if (imagePath.Length > 0 && this.clipboardImageWriter.IsSupported)
        {
            try
            {
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
                ClipboardImageWriteResult result = await this.clipboardImageWriter.WriteAsync(imageBytes, existingPaths);
                UiLogger.Log("系统剪贴板", sceneName + "显式复制：图片=" + imagePath
                    + " 文件数量=" + existingPaths.Count + " 结果=" + (result.Success ? "成功" : "失败")
                    + " 明细=" + result.Detail);
                copied = result.Success;
            }
            catch (Exception error)
            {
                UiLogger.LogError("系统剪贴板", sceneName + "显式复制异常：图片=" + imagePath, error);
            }
        }
        else
        {
            UiLogger.Log("系统剪贴板", sceneName + "显式复制：没有图片文件或平台不支持图片剪贴板 文件数量="
                + existingPaths.Count + " 结果=回退为仅写文件路径");
        }
        if (!copied)
        {
            try
            {
                copied = await this.systemClipboardService.WriteAsync(existingPaths, false);
                UiLogger.Log("系统剪贴板", sceneName + "显式复制回退为仅写文件路径：文件数量=" + existingPaths.Count
                    + " 结果=" + (copied ? "成功" : "失败"));
            }
            catch (Exception error)
            {
                UiLogger.LogError("系统剪贴板", sceneName + "显式复制回退写文件路径异常：文件数量=" + existingPaths.Count, error);
            }
        }
        if (!copied)
        {
            return CaptureOutcome.CreateFailure("复制到剪贴板失败，产物文件仍在磁盘上");
        }
        return CaptureOutcome.CreateSuccess(existingPaths[0], "已复制 " + existingPaths.Count + " 个产物到剪贴板");
    }

    /// <summary>主窗口在截图前的状态快照，用于截图后按原状态恢复。</summary>
    private sealed class MainWindowCaptureState
    {
        /// <summary>截图前主窗口是否可见。</summary>
        public bool WasVisible;

        /// <summary>截图前的窗口状态（普通 / 最大化 / 最小化）。</summary>
        public WindowState PreviousState;

        /// <summary>本次截图的判定目标类型（窗口 / 区域 / 全屏）。</summary>
        public string TargetType;

        /// <summary>本次是否真的执行了隐藏。</summary>
        public bool WasHidden;
    }

    /// <summary>
    /// 按目标类型判定是否需要临时隐藏主窗口：
    /// 区域与主窗口相交才隐藏；活动窗口且目标是本程序窗口不隐藏；全屏一律不隐藏（按用户所见截取）。
    /// </summary>
    private MainWindowCaptureState? PrepareMainWindowForCapture(CaptureTarget target, CaptureArea area, string sceneName)
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        MainWindow? window = WindowRegistry.Primary;
        string targetType = "全屏";
        if (target == CaptureTarget.Region)
        {
            targetType = "区域";
        }
        else if (target == CaptureTarget.ActiveWindow)
        {
            targetType = "窗口";
        }
        if (window == null || mainViewModel == null)
        {
            UiLogger.Log("截图", sceneName + "主窗口避让判定：目标类型=" + targetType
                + " 与主窗口相交=未知 处理=保持 恢复=无需恢复（主窗口或视图模型不存在）");
            return null;
        }
        PixelRect windowRect = GetMainWindowPixelRect(window);
        bool intersects = area.IsValid && Intersects(area, windowRect);
        bool ownWindowTarget = false;
        if (target == CaptureTarget.ActiveWindow)
        {
            IntPtr foreground = this.foregroundWindowService.CaptureForegroundWindow();
            IntPtr self = window.TryGetPlatformHandle() == null ? IntPtr.Zero : window.TryGetPlatformHandle().Handle;
            ownWindowTarget = foreground != IntPtr.Zero && self != IntPtr.Zero && foreground == self;
        }
        bool needHide = false;
        if (targetType == "区域")
        {
            needHide = intersects;
        }
        else if (targetType == "窗口")
        {
            needHide = intersects && !ownWindowTarget;
        }
        MainWindowCaptureState state = new MainWindowCaptureState();
        state.WasVisible = window.IsVisible;
        state.PreviousState = window.WindowState;
        state.TargetType = targetType;
        if (needHide && mainViewModel.Settings.CaptureHideMainWindow && window.IsVisible)
        {
            window.Hide();
            state.WasHidden = true;
        }
        UiLogger.Log("截图", sceneName + "主窗口避让判定：目标类型=" + targetType
            + " 与主窗口相交=" + (intersects ? "是" : "否")
            + " 本程序窗口目标=" + (ownWindowTarget ? "是" : "否")
            + " 处理=" + (state.WasHidden ? "临时隐藏" : "保持")
            + " 设置项=" + (mainViewModel.Settings.CaptureHideMainWindow ? "开启" : "关闭")
            + " 恢复=" + (state.WasHidden ? "待恢复" : "无需恢复"));
        return state;
    }

    /// <summary>取主窗口在物理像素坐标中的矩形，用于与截图区域判断相交。</summary>
    private static PixelRect GetMainWindowPixelRect(MainWindow window)
    {
        double scaling = window.RenderScaling <= 0 ? 1.0 : window.RenderScaling;
        int width = (int)Math.Round(window.Bounds.Width * scaling);
        int height = (int)Math.Round(window.Bounds.Height * scaling);
        return new PixelRect(window.Position.X, window.Position.Y, width, height);
    }

    /// <summary>判断截图区域与主窗口矩形是否有交集。</summary>
    private static bool Intersects(CaptureArea area, PixelRect rect)
    {
        int left = Math.Max(area.X, rect.X);
        int top = Math.Max(area.Y, rect.Y);
        int right = Math.Min(area.X + area.Width, rect.X + rect.Width);
        int bottom = Math.Min(area.Y + area.Height, rect.Y + rect.Height);
        return right > left && bottom > top;
    }

    /// <summary>
    /// 截图完成后的展示：按设置激活主窗口并切到「截图与录屏」页；关闭设置时保持当前前台窗口。
    /// 激活只发生在截图完成之后，截图期间所有相关调用点均用局部参数抑制激活。
    /// </summary>
    private void FinishCapturePresentation(string sceneName)
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            return;
        }
        if (!mainViewModel.Settings.CaptureActivateAfterCapture)
        {
            UiLogger.Log("截图", sceneName + "完成后不激活主窗口：因截图流程而抑制激活 设置项=关闭 结果=保持当前前台窗口");
            return;
        }
        MainWindow? window = WindowRegistry.Primary;
        if (window != null)
        {
            window.ShowActivated = true;
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
        mainViewModel.OpenCaptureTab();
        UiLogger.Log("截图", sceneName + "完成后激活主窗口并切到截图与录屏页：结果=已展示最新产物");
    }

    /// <summary>截图后恢复主窗口：不激活、不改变前台窗口，原为最小化则保持最小化。</summary>
    private void RestoreMainWindowAfterCapture(MainWindowCaptureState? state, string sceneName)
    {
        if (state == null || !state.WasHidden)
        {
            if (state != null)
            {
                UiLogger.Log("截图", sceneName + "主窗口恢复：目标类型=" + state.TargetType
                    + " 处理=保持 恢复=无需恢复");
            }
            return;
        }
        MainWindow? window = WindowRegistry.Primary;
        if (window == null)
        {
            UiLogger.Log("截图", sceneName + "恢复主窗口失败：主窗口不存在");
            return;
        }
        bool previousShowActivated = window.ShowActivated;
        window.ShowActivated = false;
        window.Show();
        window.ShowActivated = previousShowActivated;
        if (state.PreviousState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Minimized;
        }
        UiLogger.Log("截图", sceneName + "主窗口恢复：目标类型=" + state.TargetType
            + " 原可见=" + state.WasVisible + " 原窗口状态=" + state.PreviousState
            + " 处理=临时隐藏 恢复=已恢复");
    }

    /// <summary>计算贴图位置：默认避开原截图区域，优先右侧、其次下方，放不下才翻转，并记住上次位置。</summary>
    private PixelPoint ResolvePinPlacement(CaptureArea sourceArea, int imageWidth, int imageHeight)
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        MainWindow? window = WindowRegistry.Primary;
        PixelRect workingArea = new PixelRect(0, 0, 1920, 1080);
        if (window != null && window.Screens.Primary != null)
        {
            workingArea = window.Screens.Primary.WorkingArea;
        }
        bool avoidSource = mainViewModel == null || mainViewModel.Settings.CapturePinAvoidSource;
        if (!avoidSource && mainViewModel != null && mainViewModel.Settings.CapturePinLastX >= 0
            && mainViewModel.Settings.CapturePinLastY >= 0)
        {
            PixelPoint remembered = new PixelPoint(mainViewModel.Settings.CapturePinLastX,
                mainViewModel.Settings.CapturePinLastY);
            UiLogger.Log("贴图", "贴图位置=复用上次位置 横坐标=" + remembered.X + " 纵坐标=" + remembered.Y);
            return remembered;
        }
        int offset = 8;
        int x = sourceArea.X + sourceArea.Width + offset;
        int y = sourceArea.Y;
        if (x + imageWidth > workingArea.X + workingArea.Width)
        {
            x = sourceArea.X - imageWidth - offset;
        }
        if (x < workingArea.X)
        {
            x = workingArea.X + offset;
        }
        if (y + imageHeight > workingArea.Y + workingArea.Height)
        {
            y = sourceArea.Y + sourceArea.Height - imageHeight;
        }
        if (y < workingArea.Y)
        {
            y = workingArea.Y + offset;
        }
        PixelPoint position = new PixelPoint(x, y);
        if (mainViewModel != null)
        {
            mainViewModel.Settings.CapturePinLastX = x;
            mainViewModel.Settings.CapturePinLastY = y;
            this.settingsStore.Save(mainViewModel.Settings);
        }
        UiLogger.Log("贴图", "贴图位置计算：原区域=" + sourceArea + " 图像宽度=" + imageWidth
            + " 图像高度=" + imageHeight + " 结果横坐标=" + x + " 结果纵坐标=" + y
            + " 避开原区域=" + avoidSource);
        return position;
    }

    /// <summary>截图主流程：解析区域、调用平台截图、保存 PNG 并打开产物目录（不再自动写剪贴板）。</summary>
    private async Task<CaptureOutcome> CaptureInternalAsync(CaptureTarget target, CaptureArea area,
        int delaySeconds, string captureMode, bool resetCancelTracking = true)
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
            + " 自动复制到剪贴板=已停用（设置值=" + mainViewModel.Settings.CaptureCopyToClipboard + "）"
            + " 完成后打开产物文件夹=" + mainViewModel.Settings.CaptureOpenFolderAfter);
        CaptureCancelSource cancelSource = await this.WaitForCancelablePhaseAsync(delaySeconds,
            captureMode, "延迟等待", resetCancelTracking);
        if (cancelSource != CaptureCancelSource.None)
        {
            return this.CreateCaptureCancelOutcome(cancelSource, captureMode, "延迟等待");
        }

        ScreenCaptureRequest request = new ScreenCaptureRequest();
        request.Target = target;
        request.Area = area;
        request.IncludeCursor = mainViewModel.Settings.CaptureIncludeCursor;
        request.ToolPath = mainViewModel.Settings.FfmpegPath;
        MainWindowCaptureState? hideState = this.PrepareMainWindowForCapture(target, area, captureMode);
        ScreenCaptureResult captureResult = await this.screenCaptureService.CaptureAsync(request);
        this.RestoreMainWindowAfterCapture(hideState, captureMode);
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
        cancelSource = this.captureCancelInput.PollCancelRequest();
        if (cancelSource != CaptureCancelSource.None)
        {
            return this.CreateCaptureCancelOutcome(cancelSource, captureMode, "保存产物前");
        }

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
        cancelSource = this.captureCancelInput.PollCancelRequest();
        if (cancelSource != CaptureCancelSource.None)
        {
            this.DeleteCancelledProduct(screenshotPath, captureMode);
            return this.CreateCaptureCancelOutcome(cancelSource, captureMode, "写入剪贴板前");
        }

        UiLogger.Log("截图", "截图未自动写剪贴板：复制改由产物历史或完成提示的显式按钮触发 产物路径=" + screenshotPath);

        CaptureOutcome outcome = CaptureOutcome.CreateSuccess(screenshotPath, "截图已保存：" + screenshotPath);
        this.CaptureFinished?.Invoke(outcome);
        if (mainViewModel.Settings.CaptureOpenFolderAfter)
        {
            this.OpenOutputFolderTab(outputDirectory, false);
        }
        else
        {
            UiLogger.Log("截图", "截图完成后未打开产物文件夹：用户已关闭「完成后自动打开产物文件夹」开关");
        }
        // 截图完成后再激活主窗口并切到截图页；截图期间的所有窗口行为均用局部参数抑制激活
        this.FinishCapturePresentation(captureMode);
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
            CaptureArea? selectedArea = await this.SelectRegionAreaAsync("区域录屏");
            if (selectedArea == null)
            {
                UiLogger.Log("录屏", "区域录屏已取消：用户没有完成框选");
                return CaptureOutcome.CreateCancelled("已取消区域录屏");
            }
            CaptureCancelSource cancelSource = await this.WaitForOverlayClosedAsync("区域录屏", "区域录屏");
            if (cancelSource != CaptureCancelSource.None)
            {
                return this.CreateCaptureCancelOutcome(cancelSource, "区域录屏", "遮罩关闭后");
            }
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
    private async Task<CaptureArea?> SelectRegionAreaAsync(string captureMode,
        bool resetCancelTracking = true)
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        if (mainViewModel == null)
        {
            return null;
        }
        // 打开遮罩前清除已按下的按键，避免用右键菜单触发截图时旧按键被当成取消；
        // 已经经过延迟/遮罩阶段的截图不再复位，保证阶段切换瞬间按下的右键仍能被捕获
        if (resetCancelTracking)
        {
            this.captureCancelInput.Reset();
        }
        this.pendingForegroundWindow = this.foregroundWindowService.CaptureForegroundWindow();
        UiLogger.Log("截图", "区域框选前记录前台窗口：句柄=" + this.pendingForegroundWindow.ToInt64()
            + " 结果=" + (this.pendingForegroundWindow == IntPtr.Zero ? "不可用" : "已记录，取消时恢复"));
        MainWindow? ownerWindow = WindowRegistry.Primary;
        // 多显示器：整个虚拟桌面分别创建遮罩，光标跨屏时识别与高亮跟随当前屏幕；
        // 单显示器仍沿用原有单窗口流程
        if (ownerWindow != null && this.screenRegionDetector.IsSupported
            && ownerWindow.Screens.All.Count > 1)
        {
            return await this.SelectMultiScreenAreaAsync(ownerWindow, ownerWindow.Screens.All,
                mainViewModel, captureMode);
        }
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
                regionCandidates, this.screenRegionDetector, null, captureMode);
            selectionWindowCreated = true;
            Task<CaptureArea?> selectionTask = selectionWindow.SelectAreaAsync();
            CaptureArea? selected = await this.AwaitSelectionWithCancelAsync(selectionTask,
                selectionWindow, null, captureMode);
            if (selected == null)
            {
                this.RestorePendingForeground(captureMode);
                return null;
            }
            this.pendingForegroundWindow = IntPtr.Zero;
            UiLogger.Log("截图", "区域框选完成：选择区域=" + selected.Value);
            return selected;
        }
        catch (Exception error)
        {
            if (bitmap != null && !selectionWindowCreated)
            {
                bitmap.Dispose();
            }
            this.RestorePendingForeground(captureMode);
            UiLogger.LogError("截图", "区域框选窗口异常", error);
            this.ShowFailure("区域截图失败", "无法打开区域框选窗口：" + error.Message);
            return null;
        }
    }

    /// <summary>
    /// 多显示器框选：按显示器分别创建预览与遮罩（各自使用该屏 DPI），
    /// 由 <see cref="RegionSelectionSession"/> 统一处理光标跨屏、Tab/滚轮层级与跨屏拖拽。
    /// </summary>
    private async Task<CaptureArea?> SelectMultiScreenAreaAsync(MainWindow ownerWindow,
        IReadOnlyList<Screen> screens, MainWindowViewModel mainViewModel, string captureMode)
    {
        List<CaptureArea> areas = new List<CaptureArea>();
        List<double> scalings = new List<double>();
        List<Bitmap> pendingBitmaps = new List<Bitmap>();
        RegionSelectionSession? session = null;
        try
        {
            foreach (Screen screen in screens)
            {
                CaptureArea area = new CaptureArea(screen.Bounds.X, screen.Bounds.Y,
                    screen.Bounds.Width, screen.Bounds.Height);
                if (!area.IsValid)
                {
                    continue;
                }
                ScreenCaptureRequest previewRequest = new ScreenCaptureRequest();
                previewRequest.Target = CaptureTarget.Region;
                previewRequest.Area = area;
                previewRequest.IncludeCursor = false;
                previewRequest.ToolPath = mainViewModel.Settings.FfmpegPath;
                ScreenCaptureResult preview = await this.screenCaptureService.CaptureAsync(previewRequest);
                if (!preview.Success)
                {
                    UiLogger.Log("截图", "多屏框选失败：无法获取屏幕预览图 屏幕区域=" + area
                        + " 原因=" + preview.ErrorMessage);
                    this.ShowFailure("区域截图失败", preview.ErrorMessage);
                    return null;
                }
                Bitmap bitmap;
                using (MemoryStream imageStream = new MemoryStream(preview.ImageBytes))
                {
                    bitmap = new Bitmap(imageStream);
                }
                pendingBitmaps.Add(bitmap);
                areas.Add(area);
                scalings.Add(screen.Scaling);
                UiLogger.Log("截图", "多屏框选预览已获取：屏幕区域=" + area
                    + " 缩放比例=" + screen.Scaling
                    + " 图像宽度=" + preview.PixelWidth + " 图像高度=" + preview.PixelHeight);
            }
            if (areas.Count == 0)
            {
                UiLogger.Log("截图", "多屏框选失败：没有可用的显示器区域");
                this.ShowFailure("区域截图失败", "没有可用的显示器区域");
                return null;
            }
            session = new RegionSelectionSession(this.screenRegionDetector);
            List<RegionSelectionWindow> windows = new List<RegionSelectionWindow>();
            for (int index = 0; index < areas.Count; index++)
            {
                Bitmap bitmap = pendingBitmaps[index];
                RegionSelectionWindow window = new RegionSelectionWindow(bitmap, areas[index], scalings[index],
                    new List<ScreenRegionCandidate>(), this.screenRegionDetector, session, captureMode);
                pendingBitmaps[index] = null;
                windows.Add(window);
            }
            UiLogger.Log("截图", "多屏框选窗口已创建：显示器数量=" + windows.Count
                + " 虚拟桌面覆盖=" + DescribeVirtualDesktop(areas)
                + " 结果=等待用户跨屏框选");
            session.ShowAll(ownerWindow);
            CaptureArea? selected = await this.AwaitSelectionWithCancelAsync(session.Completion,
                null, session, captureMode);
            if (selected == null)
            {
                UiLogger.Log("截图", "多屏框选已取消：用户没有完成跨屏框选");
                this.RestorePendingForeground(captureMode);
                return null;
            }
            this.pendingForegroundWindow = IntPtr.Zero;
            UiLogger.Log("截图", "多屏框选完成：选择区域=" + selected.Value
                + " 横跨显示器数量=" + CountIntersectedScreens(selected.Value, areas));
            return selected;
        }
        catch (Exception error)
        {
            UiLogger.LogError("截图", "多屏框选会话异常", error);
            this.RestorePendingForeground(captureMode);
            session?.Complete(null);
            this.ShowFailure("区域截图失败", "无法打开多屏框选窗口：" + error.Message);
            return null;
        }
        finally
        {
            foreach (Bitmap bitmap in pendingBitmaps)
            {
                if (bitmap != null)
                {
                    bitmap.Dispose();
                }
            }
        }
    }

    private static string DescribeVirtualDesktop(List<CaptureArea> areas)
    {
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;
        foreach (CaptureArea area in areas)
        {
            left = Math.Min(left, area.X);
            top = Math.Min(top, area.Y);
            right = Math.Max(right, area.X + area.Width);
            bottom = Math.Max(bottom, area.Y + area.Height);
        }
        if (left == int.MaxValue)
        {
            return "无";
        }
        return new CaptureArea(left, top, right - left, bottom - top).ToString();
    }

    private static int CountIntersectedScreens(CaptureArea selection, List<CaptureArea> areas)
    {
        int count = 0;
        foreach (CaptureArea area in areas)
        {
            int left = Math.Max(selection.X, area.X);
            int top = Math.Max(selection.Y, area.Y);
            int right = Math.Min(selection.X + selection.Width, area.X + area.Width);
            int bottom = Math.Min(selection.Y + selection.Height, area.Y + area.Height);
            if (right > left && bottom > top)
            {
                count++;
            }
        }
        return count;
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

    /// <summary>等待框选窗口完全从屏幕消失，避免把遮罩边框截进最终图片或录像；期间右键或 Esc 仍可取消。</summary>
    private async Task<CaptureCancelSource> WaitForOverlayClosedAsync(string actionName, string captureMode)
    {
        UiLogger.Log("截图", "等待框选窗口从屏幕消失：" + actionName + " 等待毫秒=220");
        return await this.WaitForCancelableDurationAsync(TimeSpan.FromMilliseconds(220),
            captureMode, "遮罩关闭等待", false);
    }

    /// <summary>
    /// 可取消的延迟等待：每 40 毫秒轮询一次右键与 Esc，命中后立即结束等待。
    /// 延迟为 0 时也会复位按键状态并检查一次，保证紧接着按下的右键不会被吞掉。
    /// </summary>
    private async Task<CaptureCancelSource> WaitForCancelablePhaseAsync(int delaySeconds,
        string captureMode, string stage, bool resetCancelTracking = true)
    {
        if (delaySeconds <= 0)
        {
            if (resetCancelTracking)
            {
                this.captureCancelInput.Reset();
            }
            return this.captureCancelInput.PollCancelRequest();
        }
        UiLogger.Log("截图", captureMode + "进入延迟等待：延迟秒数=" + delaySeconds
            + " 取消方式=鼠标右键或Esc");
        return await this.WaitForCancelableDurationAsync(TimeSpan.FromSeconds(delaySeconds),
            captureMode, stage, resetCancelTracking);
    }

    /// <summary>在指定时长内轮询取消输入；返回非 None 表示用户按下了右键或 Esc。</summary>
    private async Task<CaptureCancelSource> WaitForCancelableDurationAsync(TimeSpan duration,
        string captureMode, string stage, bool resetCancelTracking = true)
    {
        if (resetCancelTracking)
        {
            this.captureCancelInput.Reset();
        }
        DateTime deadline = DateTime.UtcNow + duration;
        while (true)
        {
            CaptureCancelSource source = this.captureCancelInput.PollCancelRequest();
            if (source != CaptureCancelSource.None)
            {
                return source;
            }
            if (DateTime.UtcNow >= deadline)
            {
                return CaptureCancelSource.None;
            }
            await Task.Delay(40);
        }
    }

    /// <summary>
    /// 等待框选完成，同时轮询全局右键 / Esc：遮罩没有拿到焦点时也能立即取消。
    /// 取消时关闭全部遮罩并返回空区域，不进入后续截图与保存流程。
    /// </summary>
    private async Task<CaptureArea?> AwaitSelectionWithCancelAsync(Task<CaptureArea?> selectionTask,
        RegionSelectionWindow? window, RegionSelectionSession? session, string captureMode)
    {
        while (!selectionTask.IsCompleted)
        {
            CaptureCancelSource source = this.captureCancelInput.PollCancelRequest();
            if (source != CaptureCancelSource.None)
            {
                this.LogCaptureCancel(source, captureMode, "框选遮罩");
                if (session != null)
                {
                    session.Cancel(DescribeCancelSource(source), captureMode);
                }
                else if (window != null)
                {
                    window.CancelSelectionFromInput(DescribeCancelSource(source));
                }
                break;
            }
            await Task.Delay(40);
        }
        return await selectionTask;
    }

    /// <summary>记录一次取消并按约定返回取消结果，不触发产物保存、历史刷新或贴图。</summary>
    private CaptureOutcome CreateCaptureCancelOutcome(CaptureCancelSource source, string captureMode,
        string stage)
    {
        this.LogCaptureCancel(source, captureMode, stage);
        return CaptureOutcome.CreateCancelled("已取消" + captureMode + "（" + DescribeCancelSource(source) + "）");
    }

    /// <summary>写入纯中文取消日志：记录来源、截图模式与所处阶段。</summary>
    private void LogCaptureCancel(CaptureCancelSource source, string captureMode, string stage)
    {
        UiLogger.Log("截图", "截图取消：来源=" + DescribeCancelSource(source)
            + " 截图模式=" + captureMode
            + " 阶段=" + stage
            + " 结果=已立即退出截图，未生成产物、未进入历史、未生成贴图");
    }

    /// <summary>取消框选后把焦点还给取消前的窗口，避免前台停留在本程序。</summary>
    private void RestorePendingForeground(string captureMode)
    {
        IntPtr handle = this.pendingForegroundWindow;
        this.pendingForegroundWindow = IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }
        bool restored = this.foregroundWindowService.RestoreForegroundWindow(handle);
        UiLogger.Log("截图", "截图取消后恢复前台窗口：截图模式=" + captureMode
            + " 原前台句柄=" + handle.ToInt64()
            + " 结果=" + (restored ? "成功" : "失败，前台保持当前窗口"));
    }

    /// <summary>取消过程中已经写出的文件必须删除，避免留下无主产物。</summary>
    private void DeleteCancelledProduct(string filePath, string captureMode)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                UiLogger.Log("截图", "截图取消：截图模式=" + captureMode
                    + " 阶段=产物清理 结果=已删除取消过程中生成的临时文件 路径=" + filePath);
            }
        }
        catch (Exception error)
        {
            UiLogger.LogError("截图", "截图取消后删除临时产物失败：路径=" + filePath, error);
        }
    }

    private static string DescribeCancelSource(CaptureCancelSource source)
    {
        if (source == CaptureCancelSource.RightButton)
        {
            return "鼠标右键";
        }
        if (source == CaptureCancelSource.Escape)
        {
            return "Esc 键";
        }
        return "未知来源";
    }

    private static string DescribeCaptureMode(CaptureTarget target)
    {
        if (target == CaptureTarget.FullScreen)
        {
            return "全屏截图";
        }
        if (target == CaptureTarget.PrimaryScreen)
        {
            return "主屏截图";
        }
        if (target == CaptureTarget.ActiveWindow)
        {
            return "活动窗口截图";
        }
        return "区域截图";
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
    /// <param name="directory">产物目录。</param>
    /// <param name="activateWindow">是否把主窗口带到前台；截图流程传 false，避免抢走用户当前焦点。</param>
    public void OpenOutputFolderTab(string directory, bool activateWindow = true)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            UiLogger.Log("截图录屏", "打开产物文件夹被跳过：目录为空");
            return;
        }
        MainWindow? window = WindowRegistry.Primary;
        if (window != null && activateWindow)
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
        PixelPoint position = this.ResolvePinPosition(window);
        PinWindowManager.ShowPinnedImage(bitmap, position, mainViewModel.Settings, this.ResolveOcrService());
        UiLogger.Log("贴图", "剪贴板图片已贴到屏幕：像素宽度=" + bitmap.PixelSize.Width
            + " 像素高度=" + bitmap.PixelSize.Height
            + " 贴图横坐标=" + position.X
            + " 贴图纵坐标=" + position.Y);
        return CaptureOutcome.CreateSuccess(string.Empty, "剪贴板图片已贴到屏幕");
    }

    /// <summary>
    /// 把已有图片文件贴到屏幕（截图区域再次贴图 / 产物历史贴图）：
    /// 在鼠标当前位置附近显示，可拖动、缩放、翻转、调透明度与再次标注。
    /// </summary>
    public CaptureOutcome PinImageFile(string filePath)
    {
        MainWindowViewModel? mainViewModel = this.viewModel;
        MainWindow? window = WindowRegistry.Primary;
        if (mainViewModel == null || window == null)
        {
            UiLogger.Log("贴图", "图片文件贴图被拒绝：主窗口或视图模型未就绪 路径=" + filePath);
            return CaptureOutcome.CreateFailure("主窗口尚未就绪，无法贴图");
        }
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            UiLogger.Log("贴图", "图片文件贴图被拒绝：文件不存在 路径=" + filePath);
            return CaptureOutcome.CreateFailure("图片文件不存在：" + filePath);
        }
        Bitmap bitmap;
        try
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bitmap = new Bitmap(stream);
            }
        }
        catch (Exception error)
        {
            UiLogger.LogError("贴图", "读取图片文件失败：路径=" + filePath, error);
            return CaptureOutcome.CreateFailure("读取图片失败：" + error.Message);
        }
        PixelPoint position = this.ResolvePinPosition(window);
        PinWindowManager.ShowPinnedImage(bitmap, position, mainViewModel.Settings, this.ResolveOcrService());
        UiLogger.Log("贴图", "图片文件已贴到屏幕：路径=" + filePath
            + " 像素宽度=" + bitmap.PixelSize.Width
            + " 像素高度=" + bitmap.PixelSize.Height
            + " 贴图横坐标=" + position.X
            + " 贴图纵坐标=" + position.Y);
        return CaptureOutcome.CreateSuccess(string.Empty, "已贴到屏幕：" + filePath);
    }

    /// <summary>解析新贴图的初始位置：鼠标当前位置优先，读不到指针时使用主屏工作区左上角偏移。</summary>
    private PixelPoint ResolvePinPosition(MainWindow window)
    {
        int pointerX;
        int pointerY;
        if (this.screenRegionDetector.TryGetPointerPosition(out pointerX, out pointerY))
        {
            foreach (Screen candidateScreen in window.Screens.All)
            {
                PixelRect bounds = candidateScreen.Bounds;
                if (pointerX >= bounds.X && pointerX < bounds.X + bounds.Width
                    && pointerY >= bounds.Y && pointerY < bounds.Y + bounds.Height)
                {
                    return new PixelPoint(pointerX, pointerY);
                }
            }
        }
        Screen? primary = window.Screens.Primary;
        if (primary != null)
        {
            PixelRect workingArea = primary.WorkingArea;
            return new PixelPoint(workingArea.X + 60, workingArea.Y + 60);
        }
        return new PixelPoint(60, 60);
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
            + " 自动复制到剪贴板=已停用（设置值=" + mainViewModel.Settings.CaptureCopyToClipboard + "）"
            + " 完成打开文件夹=" + mainViewModel.Settings.CaptureOpenFolderAfter
            + " 包含鼠标指针=" + mainViewModel.Settings.CaptureIncludeCursor
            + " 延迟秒数=" + mainViewModel.Settings.CaptureDelaySeconds
            + " 录屏帧率=" + mainViewModel.Settings.CaptureRecordFps
            + " 录屏画质=" + mainViewModel.Settings.CaptureRecordQuality
            + " 贴图截图保存产物=" + mainViewModel.Settings.CapturePinShotSaveFile
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

    /// <summary>
    /// 截图失败提示：按设置分三档——托盘气泡（默认，不激活）、仅日志、弹窗（会抢焦点）。
    /// 默认档使用无焦点浮层，避免把主窗口顶到前台。
    /// </summary>
    private void ShowFailure(string title, string message)
    {
        string mode = this.viewModel == null ? "balloon" : this.viewModel.Settings.CaptureFailureNotice;
        if (string.IsNullOrEmpty(mode))
        {
            mode = "balloon";
        }
        if (string.Equals(mode, "log", StringComparison.Ordinal))
        {
            UiLogger.Log("截图", "截图失败提示：标题=" + title + " 内容=" + message
                + " 截图失败提示方式=仅日志 结果=未显示任何界面提示");
            return;
        }
        if (string.Equals(mode, "dialog", StringComparison.Ordinal))
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
            UiLogger.Log("截图", "截图失败提示：标题=" + title + " 内容=" + message
                + " 截图失败提示方式=弹窗（会抢焦点） 结果=已显示模态提示");
            MessageDialog.Show(window, title, message);
            return;
        }
        Dispatcher.UIThread.Post(delegate
        {
            try
            {
                PixelRect workingArea = this.ResolveToastWorkingArea();
                FailureToastWindow toast = new FailureToastWindow(title, message, workingArea);
                toast.ShowToast();
            }
            catch (Exception error)
            {
                UiLogger.LogError("截图", "截图失败提示浮层显示失败：标题=" + title, error);
            }
        });
        UiLogger.Log("截图", "截图失败提示：标题=" + title + " 内容=" + message
            + " 截图失败提示方式=托盘气泡 结果=已显示无焦点浮层");
    }

    /// <summary>取鼠标所在显示器的工作区：气泡出现在该屏右下角，而不是固定主屏。</summary>
    private PixelRect ResolveToastWorkingArea()
    {
        MainWindow? window = WindowRegistry.Primary;
        int pointerX = 0;
        int pointerY = 0;
        bool hasPointer = this.screenRegionDetector.TryGetPointerPosition(out pointerX, out pointerY);
        if (window != null && hasPointer)
        {
            foreach (Screen candidate in window.Screens.All)
            {
                PixelRect bounds = candidate.Bounds;
                if (pointerX >= bounds.X && pointerX < bounds.X + bounds.Width
                    && pointerY >= bounds.Y && pointerY < bounds.Y + bounds.Height)
                {
                    return candidate.WorkingArea;
                }
            }
        }
        if (window != null && window.Screens.Primary != null)
        {
            return window.Screens.Primary.WorkingArea;
        }
        return new PixelRect(0, 0, 1920, 1080);
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
