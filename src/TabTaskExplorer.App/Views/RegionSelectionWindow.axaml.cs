using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using TabTaskExplorer.App.Services;
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
    private readonly TaskCompletionSource<CaptureArea?> completion = new TaskCompletionSource<CaptureArea?>();
    private Point pointerDownPoint;
    private bool isPointerDown;
    private bool isFreeformDrag;
    private bool hintHiddenByMove;
    private ScreenRegionCandidate? hoverCandidate;
    private Rect selectionRect = new Rect(0, 0, 0, 0);

    public RegionSelectionWindow(Bitmap previewImage, CaptureArea screenBounds, double screenScaling,
        IReadOnlyList<ScreenRegionCandidate> regionCandidates, IScreenRegionDetector regionDetector)
    {
        this.InitializeComponent();
        this.Cursor = new Cursor(StandardCursorType.Cross);
        this.screenArea = screenBounds;
        this.scaling = screenScaling <= 0 ? 1.0 : screenScaling;
        this.previewImage = previewImage;
        this.regionDetector = regionDetector;
        this.candidates = this.BuildCandidateList(regionCandidates);
        this.fullScreenCandidate = this.ResolveFullScreenCandidate();
        this.hoverCandidate = this.fullScreenCandidate;
        this.ScreenImage.Source = previewImage;
        this.Position = new PixelPoint(screenBounds.X, screenBounds.Y);
        this.Width = screenBounds.Width / this.scaling;
        this.Height = screenBounds.Height / this.scaling;
        this.HintPanel.IsVisible = true;
        this.HintText.Text = regionDetector.IsSupported
            ? "移动鼠标自动预选窗口 / 子视图 / 全屏，单击截图；按住拖动可自由框选，Esc 或右键取消"
            : "按住拖动鼠标框选截图区域；单击选择全屏，Esc 或右键取消";
        this.Opened += this.OnWindowOpened;
        this.Closed += this.OnWindowClosed;
        UiLogger.Log("截图", "区域框选窗口已创建：屏幕区域=" + screenBounds
            + " 缩放比例=" + this.scaling
            + " 窗口宽度=" + this.Width
            + " 窗口高度=" + this.Height
            + " 窗口子视图预选支持=" + regionDetector.IsSupported
            + " 候选区域条数=" + this.candidates.Count);
    }

    /// <summary>显示框选窗口并等待用户完成或取消，返回物理像素区域。</summary>
    public Task<CaptureArea?> SelectAreaAsync()
    {
        this.Show();
        this.Activate();
        this.Focus();
        UiLogger.Log("截图", "区域框选窗口已显示：等待用户移动鼠标预选或拖动框选");
        return this.completion.Task;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        this.ScreenImage.Width = this.Bounds.Width;
        this.ScreenImage.Height = this.Bounds.Height;
        this.ApplyInitialCandidate();
        this.Activate();
        this.Focus();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (!this.completion.Task.IsCompleted)
        {
            this.completion.TrySetResult(null);
            UiLogger.Log("截图", "区域框选窗口已关闭：用户没有完成框选，结果=已取消");
        }
        this.previewImage.Dispose();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            UiLogger.Log("截图", "区域框选收到取消键：按键=Esc 结果=已取消区域截图");
            this.CancelSelection();
            e.Handled = true;
        }
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            UiLogger.Log("截图", "区域框选收到取消操作：来源=鼠标右键 结果=已取消区域截图");
            this.CancelSelection();
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
        UiLogger.Log("截图", "区域框选按下：起点横坐标=" + this.pointerDownPoint.X
            + " 起点纵坐标=" + this.pointerDownPoint.Y
            + " 当前预选=" + this.DescribeCandidate(this.hoverCandidate)
            + " 结果=等待松开单击或拖动");
        e.Handled = true;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        Point current = this.ClampPoint(e.GetPosition(this));
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
            Point releasePoint = this.ClampPoint(e.GetPosition(this));
            this.hoverCandidate = this.SelectCandidateAtLogicalPoint(releasePoint);
            physicalArea = this.hoverCandidate == null ? CaptureArea.Empty : this.hoverCandidate.Area;
            modeText = "单击预选";
            UiLogger.Log("截图", "区域框选松开：模式=单击预选 当前预选=" + this.DescribeCandidate(this.hoverCandidate)
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
        this.completion.TrySetResult(physicalArea);
        this.Close();
    }

    /// <summary>按 Esc 或右键取消：返回空结果并关闭窗口。</summary>
    private void CancelSelection()
    {
        this.completion.TrySetResult(null);
        this.Close();
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
        if (pointerX < this.screenArea.X || pointerX >= this.screenArea.X + this.screenArea.Width
            || pointerY < this.screenArea.Y || pointerY >= this.screenArea.Y + this.screenArea.Height)
        {
            this.RestoreFullScreenCandidate();
            UiLogger.Log("截图", "区域框选初始预选：鼠标位置不在当前屏幕内 横坐标=" + pointerX
                + " 纵坐标=" + pointerY + " 结果=使用全屏预选");
            return;
        }
        this.hoverCandidate = this.SelectCandidateAtPhysicalPoint(pointerX, pointerY);
        this.ShowCandidateSelection(this.hoverCandidate);
        UiLogger.Log("截图", "区域框选初始预选：鼠标位置横坐标=" + pointerX + " 纵坐标=" + pointerY
            + " 预选区域=" + this.DescribeCandidate(this.hoverCandidate));
    }

    /// <summary>鼠标移动时更新当前预选候选区域；候选未变化时不重复刷新界面。</summary>
    private void UpdateHoverCandidate(Point logicalPoint)
    {
        ScreenRegionCandidate? candidate = this.SelectCandidateAtLogicalPoint(logicalPoint);
        if (candidate == null)
        {
            return;
        }
        if (object.ReferenceEquals(candidate, this.hoverCandidate))
        {
            return;
        }
        this.hoverCandidate = candidate;
        this.ShowCandidateSelection(candidate);
        UiLogger.Log("截图", "区域预选变化：预选区域=" + this.DescribeCandidate(candidate));
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

    /// <summary>把候选区域换算为窗口逻辑坐标并刷新遮罩、边框与尺寸标签。</summary>
    private void ShowCandidateSelection(ScreenRegionCandidate? candidate)
    {
        if (candidate == null)
        {
            this.RestoreFullScreenCandidate();
            return;
        }
        this.UpdateSelection(this.ToLogicalRect(candidate.Area), candidate.Name);
    }

    /// <summary>恢复全屏预选：整屏高亮并可单击截取全屏。</summary>
    private void RestoreFullScreenCandidate()
    {
        this.hoverCandidate = this.fullScreenCandidate;
        this.UpdateSelection(this.ToLogicalRect(this.fullScreenCandidate.Area), this.fullScreenCandidate.Name);
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
    private void UpdateSelection(Rect rect, string label)
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

        CaptureArea physicalArea = this.ToPhysicalArea(new Rect(left, top, right - left, bottom - top));
        string nameText = label.Length == 0 ? string.Empty : label + " · ";
        this.SizeText.Text = nameText + physicalArea.Width + " × " + physicalArea.Height + " 像素";
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
