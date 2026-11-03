using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.Services.Capture;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 区域截图框选窗口：全屏显示截图预览，鼠标移动时自动预选下方窗口、窗口内子视图或全屏，
/// 单击直接按预选区域截图；按住拖动可自由框选，按 Esc 或单击右键取消。
/// 返回物理像素坐标，供平台截图 / 录屏直接使用。
/// </summary>
public partial class RegionSelectionWindow : Window
{
    private readonly CaptureArea screenArea;
    private readonly double scaling;
    private readonly Bitmap previewImage;
    private readonly List<ScreenRegionCandidate> candidates;
    private readonly ScreenRegionCandidate fullScreenCandidate;
    private readonly IScreenRegionDetector regionDetector;
    private readonly RegionSelectionSession? session;
    private readonly string captureMode;
    private readonly PixelSampler magnifierSampler;
    private readonly TaskCompletionSource<CaptureArea?> completion = new TaskCompletionSource<CaptureArea?>();
    private readonly DispatcherTimer hoverTimer;
    private readonly List<ScreenRegionCandidate> hoverLevels = new List<ScreenRegionCandidate>();
    private Point pointerDownPoint;
    private IPointer? capturedPointer;
    private bool isPointerDown;
    private bool isFreeformDrag;
    private bool hintHiddenByMove;
    private ScreenRegionCandidate? hoverCandidate;
    private Rect selectionRect = new Rect(0, 0, 0, 0);
    private int hoverLevelIndex;
    private int lastHoverPhysicalX = int.MinValue;
    private int lastHoverPhysicalY = int.MinValue;
    private string lastHoverSignature = string.Empty;
    private string lastDisplayedKey = string.Empty;

    public RegionSelectionWindow(Bitmap previewImage, CaptureArea screenBounds, double screenScaling,
        IReadOnlyList<ScreenRegionCandidate> regionCandidates, IScreenRegionDetector regionDetector,
        RegionSelectionSession? multiScreenSession = null, string captureMode = "区域截图")
    {
        this.InitializeComponent();
        // 截图遮罩与窗口识别高亮层：工具窗口 + 归属主窗口，不进入任务栏与 Alt+Tab
        WindowHostStyler.MakeAuxiliary(this);
        this.Cursor = new Cursor(StandardCursorType.Cross);
        this.screenArea = screenBounds;
        this.scaling = screenScaling <= 0 ? 1.0 : screenScaling;
        this.previewImage = previewImage;
        this.regionDetector = regionDetector;
        this.session = multiScreenSession;
        this.captureMode = captureMode;
        this.magnifierSampler = new PixelSampler(previewImage);
        this.Magnifier.SetSource(previewImage);
        this.Magnifier.SetZoom(8.0);
        this.candidates = this.BuildCandidateList(regionCandidates);
        this.fullScreenCandidate = this.ResolveFullScreenCandidate();
        this.hoverCandidate = this.fullScreenCandidate;
        this.hoverLevels.Add(this.fullScreenCandidate);
        this.ScreenImage.Source = previewImage;
        this.Position = new PixelPoint(screenBounds.X, screenBounds.Y);
        this.Width = screenBounds.Width / this.scaling;
        this.Height = screenBounds.Height / this.scaling;
        this.HintPanel.IsVisible = true;
        this.HintText.Text = regionDetector.IsSupported
            ? "移动鼠标实时预选窗口 / 子视图 / 全屏；Tab 或滚轮切换层级，单击或 Enter 截图；按住拖动可自由框选，Esc 或右键取消"
            : "按住拖动鼠标框选截图区域；单击选择全屏，Esc 或右键取消";
        this.Opened += this.OnWindowOpened;
        this.Closed += this.OnWindowClosed;
        // Tab 会被 Avalonia 的焦点导航先行处理，必须在隧道阶段拦截，才能用于切换预选层级
        this.AddHandler(InputElement.KeyDownEvent, this.OnWindowKeyDown, RoutingStrategies.Tunnel);
        // 鼠标静止时窗口状态仍可能变化（打开 / 关闭 / 切换虚拟桌面），定时重新命中保证高亮及时刷新
        this.hoverTimer = new DispatcherTimer();
        this.hoverTimer.Interval = TimeSpan.FromMilliseconds(140);
        this.hoverTimer.Tick += this.OnHoverTimerTick;
        this.session?.Register(this);
        UiLogger.Log("截图", "区域框选窗口已创建：截图模式=" + this.captureMode
            + " 屏幕区域=" + screenBounds
            + " 缩放比例=" + this.scaling
            + " 窗口宽度=" + this.Width
            + " 窗口高度=" + this.Height
            + " 窗口子视图预选支持=" + regionDetector.IsSupported
            + " 候选区域条数=" + this.candidates.Count);
    }

    /// <summary>显示框选窗口并等待用户完成或取消，返回物理像素区域。</summary>
    public Task<CaptureArea?> SelectAreaAsync()
    {
        // 有主窗口时以 Owner 方式显示，工具窗口不进 Alt+Tab；主窗口隐藏到托盘时退化为无 Owner 显示
        MainWindow? owner = WindowRegistry.Primary;
        if (owner != null && owner.IsVisible)
        {
            this.Show(owner);
        }
        else
        {
            this.Show();
        }
        this.Activate();
        this.Focus();
        UiLogger.Log("截图", "区域框选窗口已显示：等待用户移动鼠标预选或拖动框选");
        return this.completion.Task;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        this.ScreenImage.Width = this.Bounds.Width;
        this.ScreenImage.Height = this.Bounds.Height;
        if (this.session != null)
        {
            // 多屏会话统一管理活动屏幕、命中与跨屏拖拽；单窗口只负责显示本屏内容
            return;
        }
        this.ApplyInitialCandidate();
        this.Activate();
        this.Focus();
        this.hoverTimer.Start();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        this.hoverTimer.Stop();
        this.hoverTimer.Tick -= this.OnHoverTimerTick;
        this.isPointerDown = false;
        this.isFreeformDrag = false;
        if (this.capturedPointer != null)
        {
            this.capturedPointer.Capture(null);
            this.capturedPointer = null;
        }
        if (this.session != null)
        {
            this.session.OnWindowClosed(this);
        }
        else if (!this.completion.Task.IsCompleted)
        {
            this.completion.TrySetResult(null);
            UiLogger.Log("截图", "区域框选窗口已关闭：用户没有完成框选，结果=已取消");
        }
        this.Magnifier.SetSource(null);
        this.magnifierSampler.Dispose();
        this.previewImage.Dispose();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.CancelSelection("Esc 键");
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Tab)
        {
            string source = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "Shift+Tab" : "Tab";
            this.CycleHoverLevel(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1, source);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            UiLogger.Log("截图", "区域框选收到确认键：按键=Enter 当前预选=" + DescribeCandidate(this.hoverCandidate)
                + " 结果=确认截图");
            this.AcceptHoverCandidate();
            e.Handled = true;
        }
    }

    /// <summary>滚轮切换预选层级：向上回到更大范围，向下进入更深层子视图。</summary>
    private void OnWindowPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        this.CycleHoverLevel(e.Delta.Y > 0 ? -1 : 1, "滚轮");
        e.Handled = true;
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            // 右键按下即退出，不等待抬起；同时释放可能存在的鼠标捕获
            e.Pointer.Capture(null);
            this.CancelSelection("鼠标右键");
            e.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }
        this.pointerDownPoint = this.ClampPoint(e.GetPosition(this));
        this.isPointerDown = true;
        this.isFreeformDrag = false;
        this.HintPanel.IsVisible = false;
        e.Pointer.Capture(this);
        this.capturedPointer = e.Pointer;
        this.session?.NotifyPointerPressed();
        UiLogger.Log("截图", "区域框选按下：起点横坐标=" + this.pointerDownPoint.X
            + " 起点纵坐标=" + this.pointerDownPoint.Y
            + " 当前预选=" + DescribeCandidate(this.hoverCandidate)
            + " 结果=等待松开单击或拖动");
        e.Handled = true;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        Point current = this.ClampPoint(e.GetPosition(this));
        this.UpdateMagnifier(current);
        if (this.session != null)
        {
            if (this.isPointerDown)
            {
                this.session.NotifyPointerMoved();
                return;
            }
            this.session.NotifyPointerMovedHover();
        }
        if (this.isPointerDown)
        {
            if (!this.isFreeformDrag)
            {
                double deltaX = current.X - this.pointerDownPoint.X;
                double deltaY = current.Y - this.pointerDownPoint.Y;
                if (Math.Abs(deltaX) < 5 && Math.Abs(deltaY) < 5)
                {
                    return;
                }
                this.isFreeformDrag = true;
                this.HintPanel.IsVisible = false;
                UiLogger.Log("截图", "区域框选进入自由框选：起点横坐标=" + this.pointerDownPoint.X
                    + " 起点纵坐标=" + this.pointerDownPoint.Y
                    + " 结果=已从预选模式切换为拖动框选");
            }
            double left = Math.Min(this.pointerDownPoint.X, current.X);
            double top = Math.Min(this.pointerDownPoint.Y, current.Y);
            double width = Math.Abs(current.X - this.pointerDownPoint.X);
            double height = Math.Abs(current.Y - this.pointerDownPoint.Y);
            this.selectionRect = new Rect(left, top, width, height);
            this.UpdateSelection(this.selectionRect, "自由框选");
            return;
        }

        if (!this.hintHiddenByMove)
        {
            this.hintHiddenByMove = true;
            this.HintPanel.IsVisible = false;
        }
        this.UpdateHoverCandidate(current);
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!this.isPointerDown)
        {
            return;
        }
        this.isPointerDown = false;
        e.Pointer.Capture(null);
        this.capturedPointer = null;
        if (this.session != null && this.session.NotifyPointerReleased())
        {
            return;
        }

        CaptureArea physicalArea;
        string modeText;
        if (this.isFreeformDrag)
        {
            physicalArea = this.ToPhysicalArea(this.selectionRect);
            modeText = "自由框选";
            UiLogger.Log("截图", "区域框选松开：模式=自由框选 逻辑区域宽度=" + this.selectionRect.Width
                + " 逻辑区域高度=" + this.selectionRect.Height
                + " 物理区域=" + physicalArea);
        }
        else
        {
            // 直接使用当前高亮层级，保证 Tab / 滚轮切换后的窗口、子视图或全屏被原样确认
            physicalArea = this.hoverCandidate == null ? CaptureArea.Empty : this.hoverCandidate.Area;
            modeText = "单击预选";
            UiLogger.Log("截图", "区域框选松开：模式=单击预选 当前预选=" + DescribeCandidate(this.hoverCandidate)
                + " 物理区域=" + physicalArea);
        }
        if (!physicalArea.IsValid || physicalArea.Width < 8 || physicalArea.Height < 8)
        {
            UiLogger.Log("截图", "区域框选结果过小已忽略：模式=" + modeText
                + " 物理宽度=" + physicalArea.Width
                + " 物理高度=" + physicalArea.Height + " 结果=保持窗口等待重新框选");
            this.isFreeformDrag = false;
            this.hintHiddenByMove = false;
            this.RestoreFullScreenCandidate();
            this.HintPanel.IsVisible = true;
            return;
        }
        this.CompleteResult(physicalArea);
    }

    /// <summary>完成选区：多屏会话关闭全部遮罩，单屏直接关闭当前窗口。</summary>
    private void CompleteResult(CaptureArea area)
    {
        if (this.session != null)
        {
            this.session.Complete(area);
            return;
        }
        this.completion.TrySetResult(area);
        this.Close();
    }

    /// <summary>按 Esc 或右键取消：立即释放捕获并撤销遮罩，多屏会话取消全部遮罩。</summary>
    private void CancelSelection(string sourceText)
    {
        this.ReleaseInputState();
        if (this.session != null)
        {
            this.session.Cancel(sourceText, this.captureMode);
            return;
        }
        if (this.completion.Task.IsCompleted)
        {
            return;
        }
        UiLogger.Log("截图", "截图取消：来源=" + sourceText
            + " 截图模式=" + this.captureMode
            + " 阶段=框选遮罩"
            + " 结果=已撤销遮罩、高亮与放大镜，未生成产物");
        this.completion.TrySetResult(null);
        this.Close();
    }

    /// <summary>全局取消轮询兜底：遮罩没有拿到焦点时也能按右键 / Esc 退出。</summary>
    internal void CancelSelectionFromInput(string sourceText)
    {
        this.CancelSelection(sourceText);
    }

    /// <summary>释放鼠标捕获与所有临时视觉状态，避免取消后残留点击层或卡住输入。</summary>
    private void ReleaseInputState()
    {
        this.isPointerDown = false;
        this.isFreeformDrag = false;
        if (this.capturedPointer != null)
        {
            this.capturedPointer.Capture(null);
            this.capturedPointer = null;
        }
        this.hoverTimer.Stop();
        this.HintPanel.IsVisible = false;
        this.MagnifierPanel.IsVisible = false;
        this.SelectionBorder.IsVisible = false;
        this.SizeBadge.IsVisible = false;
    }

    /// <summary>窗口打开时按当前鼠标位置立即预选下方区域；读取不到指针位置时保持全屏预选。</summary>
    private void ApplyInitialCandidate()
    {
        int pointerX;
        int pointerY;
        if (!this.regionDetector.IsSupported || !this.regionDetector.TryGetPointerPosition(out pointerX, out pointerY))
        {
            this.RestoreFullScreenCandidate();
            UiLogger.Log("截图", "区域框选初始预选：鼠标位置不可用 结果=使用全屏预选");
            return;
        }
        if (!this.IsPointInsideScreen(pointerX, pointerY))
        {
            this.RestoreFullScreenCandidate();
            UiLogger.Log("截图", "区域框选初始预选：鼠标位置不在当前屏幕内 横坐标=" + pointerX
                + " 纵坐标=" + pointerY + " 结果=使用全屏预选");
            return;
        }
        this.RefreshHoverAtPhysical(pointerX, pointerY, true, "初始预选");
        UiLogger.Log("截图", "区域框选初始预选：鼠标位置横坐标=" + pointerX + " 纵坐标=" + pointerY
            + " 预选区域=" + DescribeCandidate(this.hoverCandidate));
    }

    /// <summary>鼠标静止时定时重新命中：窗口被打开、关闭或切换虚拟桌面后高亮能自动跟随。</summary>
    private void OnHoverTimerTick(object? sender, EventArgs e)
    {
        if (this.isPointerDown)
        {
            return;
        }
        int pointerX;
        int pointerY;
        if (!this.regionDetector.TryGetPointerPosition(out pointerX, out pointerY))
        {
            return;
        }
        if (!this.IsPointInsideScreen(pointerX, pointerY))
        {
            return;
        }
        this.RefreshHoverAtPhysical(pointerX, pointerY, false, "定时刷新");
    }

    /// <summary>
    /// 按物理坐标刷新命中链：优先使用平台的实时命中（窗口 + 嵌套子视图），
    /// 枚举不到时回落全屏；鼠标移动时重置到窗口层级，窗口状态变化时保留用户用 Tab 选中的层级。
    /// </summary>
    private void RefreshHoverAtPhysical(int x, int y, bool resetLevel, string reason)
    {
        List<ScreenRegionCandidate> chain = new List<ScreenRegionCandidate>();
        if (this.regionDetector.IsSupported)
        {
            IReadOnlyList<ScreenRegionCandidate> hit = this.regionDetector.HitTest(x, y);
            foreach (ScreenRegionCandidate candidate in hit)
            {
                if (candidate != null && candidate.Area.IsValid)
                {
                    chain.Add(candidate);
                }
            }
        }
        if (chain.Count == 0 && !this.regionDetector.IsSupported)
        {
            ScreenRegionCandidate? fallback = this.SelectCandidateAtPhysicalPoint(x, y);
            if (fallback != null)
            {
                chain.Add(fallback);
            }
        }
        string signature = BuildCandidateSignature(chain);
        bool chainChanged = signature != this.lastHoverSignature;
        this.lastHoverSignature = signature;
        this.lastHoverPhysicalX = x;
        this.lastHoverPhysicalY = y;
        if (!chainChanged && !resetLevel)
        {
            return;
        }
        this.RebuildHoverLevels(chain);
        if (resetLevel)
        {
            this.hoverLevelIndex = 0;
        }
        else if (this.hoverLevelIndex >= this.hoverLevels.Count)
        {
            this.hoverLevelIndex = this.hoverLevels.Count - 1;
        }
        if (this.hoverLevelIndex < 0)
        {
            this.hoverLevelIndex = 0;
        }
        this.ShowHoverLevel(reason, chainChanged);
    }

    /// <summary>把命中链接上全屏候选，形成「窗口 → 子视图 → 全屏」的可循环层级列表。</summary>
    private void RebuildHoverLevels(List<ScreenRegionCandidate> chain)
    {
        this.hoverLevels.Clear();
        foreach (ScreenRegionCandidate candidate in chain)
        {
            this.hoverLevels.Add(candidate);
        }
        bool hasFullScreen = this.hoverLevels.Count > 0
            && this.hoverLevels[this.hoverLevels.Count - 1].Kind == ScreenRegionKind.FullScreen;
        if (!hasFullScreen)
        {
            this.hoverLevels.Add(this.fullScreenCandidate);
        }
    }

    /// <summary>显示当前层级并写入切换日志；仅层级或命中区域变化时刷新界面。</summary>
    private void ShowHoverLevel(string reason, bool chainChanged)
    {
        if (this.hoverLevels.Count == 0)
        {
            this.RestoreFullScreenCandidate();
            return;
        }
        if (this.hoverLevelIndex < 0)
        {
            this.hoverLevelIndex = 0;
        }
        if (this.hoverLevelIndex >= this.hoverLevels.Count)
        {
            this.hoverLevelIndex = this.hoverLevels.Count - 1;
        }
        ScreenRegionCandidate candidate = this.hoverLevels[this.hoverLevelIndex];
        this.hoverCandidate = candidate;
        this.ShowCandidateSelection(candidate);
        if (chainChanged)
        {
            UiLogger.Log("截图", "区域预选实时更新：原因=" + reason
                + " 鼠标横坐标=" + this.lastHoverPhysicalX
                + " 鼠标纵坐标=" + this.lastHoverPhysicalY
                + " 层级=" + (this.hoverLevelIndex + 1) + "/" + this.hoverLevels.Count
                + " 预选=" + DescribeCandidate(candidate));
        }
    }

    /// <summary>在层级列表中前后切换：+1 进入更深的子视图或全屏，-1 回到更大范围。</summary>
    private void CycleHoverLevel(int delta, string source)
    {
        if (this.hoverLevels.Count <= 1)
        {
            return;
        }
        int next = (this.hoverLevelIndex + delta) % this.hoverLevels.Count;
        if (next < 0)
        {
            next += this.hoverLevels.Count;
        }
        this.hoverLevelIndex = next;
        ScreenRegionCandidate candidate = this.hoverLevels[next];
        this.hoverCandidate = candidate;
        this.ShowCandidateSelection(candidate);
        UiLogger.Log("截图", "区域预选层级切换：来源=" + source
            + " 层级=" + (next + 1) + "/" + this.hoverLevels.Count
            + " 预选=" + DescribeCandidate(candidate));
    }

    /// <summary>Enter 确认当前高亮区域：与单击预选走同一套完成路径。</summary>
    private void AcceptHoverCandidate()
    {
        ScreenRegionCandidate? candidate = this.hoverCandidate;
        if (candidate == null || !candidate.Area.IsValid
            || candidate.Area.Width < 8 || candidate.Area.Height < 8)
        {
            UiLogger.Log("截图", "区域框选 Enter 确认被忽略：当前预选区域过小 预选=" + DescribeCandidate(candidate));
            return;
        }
        UiLogger.Log("截图", "区域框选 Enter 确认：物理区域=" + candidate.Area + " 结果=完成区域截图");
        this.CompleteResult(candidate.Area);
    }

    /// <summary>判断物理坐标是否落在当前框选窗口覆盖的屏幕区域内。</summary>
    private bool IsPointInsideScreen(int x, int y)
    {
        return x >= this.screenArea.X && x < this.screenArea.X + this.screenArea.Width
            && y >= this.screenArea.Y && y < this.screenArea.Y + this.screenArea.Height;
    }

    /// <summary>生成命中链签名，用于判断高亮是否需要刷新，避免同一窗口反复重绘造成闪烁。</summary>
    private static string BuildCandidateSignature(List<ScreenRegionCandidate> chain)
    {
        if (chain.Count == 0)
        {
            return string.Empty;
        }
        string signature = string.Empty;
        foreach (ScreenRegionCandidate candidate in chain)
        {
            signature = signature + candidate.Kind + ":" + candidate.NativeHandle + ":"
                + candidate.Area.X + "," + candidate.Area.Y + "," + candidate.Area.Width + ","
                + candidate.Area.Height + "|";
        }
        return signature;
    }

    /// <summary>刷新截图放大镜：显示光标附近的像素放大图、颜色值与屏幕坐标。</summary>
    private void UpdateMagnifier(Point logicalPoint)
    {
        double physicalX = logicalPoint.X * this.scaling;
        double physicalY = logicalPoint.Y * this.scaling;
        double maximumX = Math.Max(0, this.screenArea.Width - 1);
        double maximumY = Math.Max(0, this.screenArea.Height - 1);
        physicalX = Math.Max(0, Math.Min(physicalX, maximumX));
        physicalY = Math.Max(0, Math.Min(physicalY, maximumY));
        this.Magnifier.SetImagePoint(new Point(physicalX, physicalY));
        Color color = this.magnifierSampler.GetPixel(physicalX, physicalY);
        this.MagnifierHex.Text = this.magnifierSampler.GetHex(physicalX, physicalY, true);
        this.MagnifierRgb.Text = "RGB " + color.R + ", " + color.G + ", " + color.B;
        this.MagnifierPosition.Text = "X " + (int)(this.screenArea.X + physicalX)
            + "  Y " + (int)(this.screenArea.Y + physicalY);
        this.MagnifierPanel.IsVisible = true;
        double panelWidth = 150;
        double panelHeight = 230;
        double panelLeft = logicalPoint.X + 24;
        double panelTop = logicalPoint.Y + 24;
        if (panelLeft + panelWidth > this.Bounds.Width)
        {
            panelLeft = logicalPoint.X - panelWidth - 24;
        }
        if (panelTop + panelHeight > this.Bounds.Height)
        {
            panelTop = logicalPoint.Y - panelHeight - 24;
        }
        if (panelLeft < 0)
        {
            panelLeft = 0;
        }
        if (panelTop < 0)
        {
            panelTop = 0;
        }
        Canvas.SetLeft(this.MagnifierPanel, panelLeft);
        Canvas.SetTop(this.MagnifierPanel, panelTop);
    }

    /// <summary>鼠标移动时实时命中窗口与子视图；移动超过阈值时回到窗口层级。</summary>
    private void UpdateHoverCandidate(Point logicalPoint)
    {
        double physicalX = this.screenArea.X + logicalPoint.X * this.scaling;
        double physicalY = this.screenArea.Y + logicalPoint.Y * this.scaling;
        int x = (int)Math.Floor(physicalX);
        int y = (int)Math.Floor(physicalY);
        bool moved = Math.Abs(x - this.lastHoverPhysicalX) >= 3
            || Math.Abs(y - this.lastHoverPhysicalY) >= 3
            || this.lastHoverSignature.Length == 0;
        if (!moved)
        {
            return;
        }
        this.RefreshHoverAtPhysical(x, y, true, "鼠标移动");
    }

    /// <summary>按逻辑坐标选择候选区域：优先鼠标下方最靠前的窗口，再取该窗口内最小子视图，桌面空白处回落全屏。</summary>
    private ScreenRegionCandidate? SelectCandidateAtLogicalPoint(Point logicalPoint)
    {
        double physicalX = this.screenArea.X + logicalPoint.X * this.scaling;
        double physicalY = this.screenArea.Y + logicalPoint.Y * this.scaling;
        int x = (int)Math.Floor(physicalX);
        int y = (int)Math.Floor(physicalY);
        return this.SelectCandidateAtPhysicalPoint(x, y);
    }

    /// <summary>按物理像素坐标选择候选区域。</summary>
    private ScreenRegionCandidate? SelectCandidateAtPhysicalPoint(int x, int y)
    {
        ScreenRegionCandidate? topWindow = null;
        foreach (ScreenRegionCandidate candidate in this.candidates)
        {
            if (candidate.Kind != ScreenRegionKind.Window)
            {
                continue;
            }
            if (!ContainsPoint(candidate.Area, x, y))
            {
                continue;
            }
            if (topWindow == null || candidate.ZOrder < topWindow.ZOrder)
            {
                topWindow = candidate;
            }
        }
        if (topWindow == null)
        {
            return this.fullScreenCandidate;
        }
        ScreenRegionCandidate? bestChild = null;
        foreach (ScreenRegionCandidate candidate in this.candidates)
        {
            if (candidate.Kind != ScreenRegionKind.ChildView)
            {
                continue;
            }
            if (candidate.ParentHandle != topWindow.NativeHandle)
            {
                continue;
            }
            if (!ContainsPoint(candidate.Area, x, y))
            {
                continue;
            }
            if (bestChild == null || IsSmallerArea(candidate.Area, bestChild.Area)
                || (IsSameArea(candidate.Area, bestChild.Area) && candidate.Depth > bestChild.Depth))
            {
                bestChild = candidate;
            }
        }
        return bestChild ?? topWindow;
    }

    /// <summary>把候选区域换算为窗口逻辑坐标并刷新遮罩、边框与尺寸标签；同一区域不重复刷新以避免闪烁。</summary>
    private void ShowCandidateSelection(ScreenRegionCandidate? candidate)
    {
        if (candidate == null)
        {
            this.RestoreFullScreenCandidate();
            return;
        }
        string key = candidate.Kind + "|" + candidate.NativeHandle + "|"
            + candidate.Area.X + "," + candidate.Area.Y + "," + candidate.Area.Width + ","
            + candidate.Area.Height + "|" + candidate.Name;
        if (key == this.lastDisplayedKey)
        {
            return;
        }
        this.lastDisplayedKey = key;
        this.UpdateSelection(this.ToLogicalRect(candidate.Area), candidate.Name);
    }

    /// <summary>恢复全屏预选：整屏高亮并可单击截取全屏。</summary>
    private void RestoreFullScreenCandidate()
    {
        this.hoverCandidate = this.fullScreenCandidate;
        this.hoverLevels.Clear();
        this.hoverLevels.Add(this.fullScreenCandidate);
        this.hoverLevelIndex = 0;
        this.ShowCandidateSelection(this.fullScreenCandidate);
    }

    /// <summary>本窗口覆盖的屏幕物理区域（多屏会话用于判断光标所在屏幕）。</summary>
    internal CaptureArea ScreenArea
    {
        get { return this.screenArea; }
    }

    /// <summary>本窗口所在显示器的缩放比例。</summary>
    internal double ScreenScaling
    {
        get { return this.scaling; }
    }

    /// <summary>多屏会话切换活动屏幕：激活时恢复定时命中，非激活时清除高亮与放大镜。</summary>
    internal void SetSessionActive(bool active)
    {
        if (active)
        {
            this.Cursor = new Cursor(StandardCursorType.Cross);
            if (!this.hintHiddenByMove)
            {
                this.HintPanel.IsVisible = true;
            }
            this.hoverTimer.Start();
            return;
        }
        this.hoverTimer.Stop();
        this.HintPanel.IsVisible = false;
        this.MagnifierPanel.IsVisible = false;
        this.ShowNoHover();
    }

    /// <summary>多屏会话在活动屏幕上立即刷新命中，用于光标跨屏后的即时高亮。</summary>
    internal void RefreshHoverAtPhysical(int x, int y, string reason)
    {
        if (!this.IsPointInsideScreen(x, y))
        {
            return;
        }
        this.RefreshHoverAtPhysical(x, y, true, reason);
    }

    /// <summary>
    /// 多屏跨屏拖拽时，把全局物理选区画到本屏：只画与本屏的交集，尺寸标签仅活动屏显示。
    /// </summary>
    internal void ApplyExternalSelection(CaptureArea selection, string label, bool showBadge)
    {
        int left = Math.Max(selection.X, this.screenArea.X);
        int top = Math.Max(selection.Y, this.screenArea.Y);
        int right = Math.Min(selection.X + selection.Width, this.screenArea.X + this.screenArea.Width);
        int bottom = Math.Min(selection.Y + selection.Height, this.screenArea.Y + this.screenArea.Height);
        if (right <= left || bottom <= top)
        {
            this.ShowNoHover();
            return;
        }
        CaptureArea intersection = new CaptureArea(left, top, right - left, bottom - top);
        Rect rect = this.ToLogicalRect(intersection);
        this.UpdateSelection(rect, string.Empty, label, showBadge);
        this.MagnifierPanel.IsVisible = false;
    }

    /// <summary>清除本屏高亮、边框与尺寸标签，只保留整屏遮罩（多屏会话中的非活动屏）。</summary>
    internal void ShowNoHover()
    {
        this.hoverCandidate = null;
        this.lastDisplayedKey = string.Empty;
        this.UpdateSelection(new Rect(0, 0, 0, 0), string.Empty, null, false);
        this.MagnifierPanel.IsVisible = false;
    }

    /// <summary>把逻辑坐标点限制在窗口范围内。</summary>
    private Point ClampPoint(Point point)
    {
        double width = this.CurrentCanvasWidth();
        double height = this.CurrentCanvasHeight();
        double x = Math.Max(0, Math.Min(point.X, width));
        double y = Math.Max(0, Math.Min(point.Y, height));
        return new Point(x, y);
    }

    /// <summary>把逻辑像素矩形换算为屏幕物理像素区域。</summary>
    private CaptureArea ToPhysicalArea(Rect rect)
    {
        int x = this.screenArea.X + (int)Math.Round(rect.X * this.scaling);
        int y = this.screenArea.Y + (int)Math.Round(rect.Y * this.scaling);
        int width = (int)Math.Round(rect.Width * this.scaling);
        int height = (int)Math.Round(rect.Height * this.scaling);
        return new CaptureArea(x, y, width, height);
    }

    /// <summary>把物理像素区域换算为窗口逻辑像素矩形。</summary>
    private Rect ToLogicalRect(CaptureArea area)
    {
        double left = (area.X - this.screenArea.X) / this.scaling;
        double top = (area.Y - this.screenArea.Y) / this.scaling;
        double width = area.Width / this.scaling;
        double height = area.Height / this.scaling;
        return new Rect(left, top, width, height);
    }

    /// <summary>刷新遮罩、选择框与尺寸标签的位置。</summary>
    private void UpdateSelection(Rect rect, string label, string? sizeOverride = null, bool showBadge = true)
    {
        double canvasWidth = this.CurrentCanvasWidth();
        double canvasHeight = this.CurrentCanvasHeight();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            SetRectangle(this.DimTop, 0, 0, canvasWidth, canvasHeight);
            SetRectangle(this.DimBottom, 0, 0, 0, 0);
            SetRectangle(this.DimLeft, 0, 0, 0, 0);
            SetRectangle(this.DimRight, 0, 0, 0, 0);
            this.SelectionBorder.IsVisible = false;
            this.SizeBadge.IsVisible = false;
            return;
        }
        double left = Math.Max(0, rect.X);
        double top = Math.Max(0, rect.Y);
        double right = Math.Min(canvasWidth, rect.X + rect.Width);
        double bottom = Math.Min(canvasHeight, rect.Y + rect.Height);
        SetRectangle(this.DimTop, 0, 0, canvasWidth, top);
        SetRectangle(this.DimBottom, 0, bottom, canvasWidth, canvasHeight - bottom);
        SetRectangle(this.DimLeft, 0, top, left, bottom - top);
        SetRectangle(this.DimRight, right, top, canvasWidth - right, bottom - top);

        Canvas.SetLeft(this.SelectionBorder, left);
        Canvas.SetTop(this.SelectionBorder, top);
        this.SelectionBorder.Width = right - left;
        this.SelectionBorder.Height = bottom - top;
        this.SelectionBorder.IsVisible = true;
        if (!showBadge)
        {
            this.SizeBadge.IsVisible = false;
            return;
        }

        CaptureArea physicalArea = this.ToPhysicalArea(new Rect(left, top, right - left, bottom - top));
        string nameText = label.Length == 0 ? string.Empty : label + " · ";
        this.SizeText.Text = sizeOverride != null
            ? sizeOverride
            : nameText + physicalArea.Width + " × " + physicalArea.Height + " 像素";
        this.SizeBadge.IsVisible = true;
        this.SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double badgeWidth = this.SizeBadge.DesiredSize.Width;
        double badgeHeight = this.SizeBadge.DesiredSize.Height;
        double badgeLeft = right - badgeWidth;
        if (badgeLeft < 0)
        {
            badgeLeft = 0;
        }
        double badgeTop = bottom + 6;
        if (badgeTop + badgeHeight > canvasHeight)
        {
            badgeTop = top - badgeHeight - 6;
        }
        if (badgeTop < 0)
        {
            badgeTop = 0;
        }
        Canvas.SetLeft(this.SizeBadge, badgeLeft);
        Canvas.SetTop(this.SizeBadge, badgeTop);
    }

    private double CurrentCanvasWidth()
    {
        double width = this.Bounds.Width;
        if (width <= 0 || double.IsNaN(width))
        {
            width = this.Width;
        }
        if (width <= 0 || double.IsNaN(width))
        {
            width = this.screenArea.Width / this.scaling;
        }
        return width;
    }

    private double CurrentCanvasHeight()
    {
        double height = this.Bounds.Height;
        if (height <= 0 || double.IsNaN(height))
        {
            height = this.Height;
        }
        if (height <= 0 || double.IsNaN(height))
        {
            height = this.screenArea.Height / this.scaling;
        }
        return height;
    }

    /// <summary>把候选列表整理为可连续使用的本地副本，过滤无效区域。</summary>
    private List<ScreenRegionCandidate> BuildCandidateList(IReadOnlyList<ScreenRegionCandidate> regionCandidates)
    {
        List<ScreenRegionCandidate> merged = new List<ScreenRegionCandidate>();
        if (regionCandidates != null)
        {
            foreach (ScreenRegionCandidate candidate in regionCandidates)
            {
                if (candidate != null && candidate.Area.IsValid)
                {
                    merged.Add(candidate);
                }
            }
        }
        return merged;
    }

    /// <summary>取全屏候选；候选列表中没有时按当前屏幕区域补一个。</summary>
    private ScreenRegionCandidate ResolveFullScreenCandidate()
    {
        foreach (ScreenRegionCandidate candidate in this.candidates)
        {
            if (candidate.Kind == ScreenRegionKind.FullScreen)
            {
                return candidate;
            }
        }
        ScreenRegionCandidate fullScreen = new ScreenRegionCandidate();
        fullScreen.Area = this.screenArea;
        fullScreen.Kind = ScreenRegionKind.FullScreen;
        fullScreen.Name = "全屏（当前显示器）";
        fullScreen.Depth = 0;
        fullScreen.ZOrder = -1;
        this.candidates.Add(fullScreen);
        return fullScreen;
    }

    /// <summary>判断区域是否包含物理像素点。</summary>
    private static bool ContainsPoint(CaptureArea area, int x, int y)
    {
        return x >= area.X && x < area.X + area.Width && y >= area.Y && y < area.Y + area.Height;
    }

    /// <summary>判断两个区域面积是否相同。</summary>
    private static bool IsSameArea(CaptureArea first, CaptureArea second)
    {
        return first.Width == second.Width && first.Height == second.Height;
    }

    /// <summary>判断第一个区域面积是否小于第二个区域。</summary>
    private static bool IsSmallerArea(CaptureArea first, CaptureArea second)
    {
        long firstArea = (long)first.Width * first.Height;
        long secondArea = (long)second.Width * second.Height;
        return firstArea < secondArea;
    }

    /// <summary>把候选区域转换成便于日志阅读的文字。</summary>
    private static string DescribeCandidate(ScreenRegionCandidate? candidate)
    {
        if (candidate == null)
        {
            return "无";
        }
        return "类型=" + DescribeKind(candidate.Kind)
            + " 名称=" + candidate.Name
            + " 区域=" + candidate.Area
            + " 层级=" + candidate.Depth
            + " 层叠序号=" + candidate.ZOrder;
    }

    /// <summary>把候选类型转换为中文名称。</summary>
    private static string DescribeKind(ScreenRegionKind kind)
    {
        if (kind == ScreenRegionKind.Window)
        {
            return "窗口";
        }
        if (kind == ScreenRegionKind.ChildView)
        {
            return "子视图";
        }
        return "全屏";
    }

    private static void SetRectangle(Rectangle rectangle, double left, double top, double width, double height)
    {
        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, top);
        rectangle.Width = Math.Max(0, width);
        rectangle.Height = Math.Max(0, height);
        rectangle.IsVisible = width > 0 && height > 0;
    }
}
