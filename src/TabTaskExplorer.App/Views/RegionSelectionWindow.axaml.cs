using System;
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
/// 区域截图框选窗口：全屏显示截图预览，拖动鼠标框选区域，按 Esc 或单击右键取消。
/// 返回物理像素坐标，供平台截图 / 录屏直接使用。
/// </summary>
public partial class RegionSelectionWindow : Window
{
    private readonly CaptureArea screenArea;
    private readonly double scaling;
    private readonly Bitmap previewImage;
    private readonly TaskCompletionSource<CaptureArea?> completion = new TaskCompletionSource<CaptureArea?>();
    private Point dragStartPoint;
    private bool isDragging;
    private Rect selectionRect = new Rect(0, 0, 0, 0);

    public RegionSelectionWindow(Bitmap previewImage, CaptureArea screenBounds, double screenScaling)
    {
        this.InitializeComponent();
        this.Cursor = new Cursor(StandardCursorType.Cross);
        this.screenArea = screenBounds;
        this.scaling = screenScaling <= 0 ? 1.0 : screenScaling;
        this.previewImage = previewImage;
        this.ScreenImage.Source = previewImage;
        this.Position = new PixelPoint(screenBounds.X, screenBounds.Y);
        this.Width = screenBounds.Width / this.scaling;
        this.Height = screenBounds.Height / this.scaling;
        this.HintPanel.IsVisible = true;
        this.Opened += this.OnWindowOpened;
        this.Closed += this.OnWindowClosed;
        UiLogger.Log("截图", "区域框选窗口已创建：屏幕区域=" + screenBounds
            + " 缩放比例=" + this.scaling
            + " 窗口宽度=" + this.Width
            + " 窗口高度=" + this.Height);
    }

    /// <summary>显示框选窗口并等待用户完成或取消，返回物理像素区域。</summary>
    public Task<CaptureArea?> SelectAreaAsync()
    {
        this.Show();
        this.Activate();
        this.Focus();
        UiLogger.Log("截图", "区域框选窗口已显示：等待用户拖动鼠标选择区域");
        return this.completion.Task;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        this.ScreenImage.Width = this.Bounds.Width;
        this.ScreenImage.Height = this.Bounds.Height;
        this.UpdateSelection(new Rect(0, 0, 0, 0));
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
        this.dragStartPoint = this.ClampPoint(e.GetPosition(this));
        this.isDragging = true;
        this.HintPanel.IsVisible = false;
        e.Pointer.Capture(this);
        this.UpdateSelection(new Rect(this.dragStartPoint, new Size(0, 0)));
        UiLogger.Log("截图", "区域框选开始：起点横坐标=" + this.dragStartPoint.X
            + " 起点纵坐标=" + this.dragStartPoint.Y);
        e.Handled = true;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!this.isDragging)
        {
            return;
        }
        Point current = this.ClampPoint(e.GetPosition(this));
        double left = Math.Min(this.dragStartPoint.X, current.X);
        double top = Math.Min(this.dragStartPoint.Y, current.Y);
        double width = Math.Abs(current.X - this.dragStartPoint.X);
        double height = Math.Abs(current.Y - this.dragStartPoint.Y);
        this.selectionRect = new Rect(left, top, width, height);
        this.UpdateSelection(this.selectionRect);
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!this.isDragging)
        {
            return;
        }
        this.isDragging = false;
        e.Pointer.Capture(null);
        CaptureArea physicalArea = this.ToPhysicalArea(this.selectionRect);
        UiLogger.Log("截图", "区域框选结束：逻辑区域宽度=" + this.selectionRect.Width
            + " 逻辑区域高度=" + this.selectionRect.Height
            + " 物理区域=" + physicalArea);
        if (!physicalArea.IsValid || physicalArea.Width < 8 || physicalArea.Height < 8)
        {
            UiLogger.Log("截图", "区域框选结果过小已忽略：物理宽度=" + physicalArea.Width
                + " 物理高度=" + physicalArea.Height + " 结果=保持窗口等待重新框选");
            this.SelectionBorder.IsVisible = false;
            this.SizeBadge.IsVisible = false;
            this.HintPanel.IsVisible = true;
            this.UpdateSelection(new Rect(0, 0, 0, 0));
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

    /// <summary>刷新遮罩、选择框与尺寸标签的位置。</summary>
    private void UpdateSelection(Rect rect)
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
        this.SizeText.Text = physicalArea.Width + " × " + physicalArea.Height + " 像素";
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

    private static void SetRectangle(Rectangle rectangle, double left, double top, double width, double height)
    {
        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, top);
        rectangle.Width = Math.Max(0, width);
        rectangle.Height = Math.Max(0, height);
        rectangle.IsVisible = width > 0 && height > 0;
    }
}
