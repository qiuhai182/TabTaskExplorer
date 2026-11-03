using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.Services.Capture;
using TabTaskExplorer.Kernel.Capture;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 全屏取色窗口（吸收 Snipaste 取色器）：冻结整屏画面，鼠标移动时显示放大镜与
/// 十六进制 / RGB 数值，单击复制颜色并关闭，Esc 或右键取消。
/// </summary>
public partial class ScreenColorPickerWindow : Window
{
    private readonly CaptureArea screenArea;

    private readonly double scaling;

    private readonly Bitmap preview;

    private readonly PixelSampler sampler;

    private string lastLoggedHex = string.Empty;

    public ScreenColorPickerWindow(Bitmap previewImage, CaptureArea area, double screenScaling)
    {
        this.InitializeComponent();
        // 取色浮层：工具窗口 + 归属主窗口，不进入任务栏与 Alt+Tab
        WindowHostStyler.MakeAuxiliary(this);
        this.preview = previewImage;
        this.screenArea = area;
        this.scaling = screenScaling <= 0 ? 1.0 : screenScaling;
        this.sampler = new PixelSampler(previewImage);
        this.ScreenImage.Source = previewImage;
        this.Magnifier.SetSource(previewImage);
        this.Magnifier.SetZoom(8.0);
        this.Position = new PixelPoint(area.X, area.Y);
        this.Width = area.Width / this.scaling;
        this.Height = area.Height / this.scaling;
        this.Cursor = new Cursor(StandardCursorType.Cross);
        this.Opened += this.OnWindowOpened;
        this.Closed += this.OnWindowClosed;
        UiLogger.Log("屏幕取色", "取色窗口已创建：屏幕区域=" + area
            + " 缩放比例=" + this.scaling
            + " 窗口宽度=" + this.Width
            + " 窗口高度=" + this.Height);
    }

    /// <summary>显示取色窗口并等待用户操作。</summary>
    public void ShowPicker()
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
        UiLogger.Log("屏幕取色", "取色窗口已显示：等待用户移动鼠标或选择颜色");
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        this.ScreenImage.Width = this.Bounds.Width;
        this.ScreenImage.Height = this.Bounds.Height;
        this.Activate();
        this.Focus();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        this.sampler.Dispose();
        this.preview.Dispose();
        this.Magnifier.SetSource(null);
        UiLogger.Log("屏幕取色", "取色窗口已关闭：取色采样器与预览图已释放");
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            UiLogger.Log("屏幕取色", "收到取消键：按键=Esc 结果=已取消取色");
            this.Close();
            e.Handled = true;
        }
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            UiLogger.Log("屏幕取色", "收到取消操作：来源=鼠标右键 结果=已取消取色");
            this.Close();
            e.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }
        Point imagePoint = this.ToImagePoint(e.GetPosition(this));
        string hex = this.sampler.GetHex(imagePoint.X, imagePoint.Y, true);
        Color color = this.sampler.GetPixel(imagePoint.X, imagePoint.Y);
        this.CopyColorAsync(hex, color, imagePoint);
        e.Handled = true;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        Point viewPoint = e.GetPosition(this);
        Point imagePoint = this.ToImagePoint(viewPoint);
        Color color = this.sampler.GetPixel(imagePoint.X, imagePoint.Y);
        string hex = this.sampler.GetHex(imagePoint.X, imagePoint.Y, true);
        this.HexText.Text = hex;
        this.RgbText.Text = "RGB " + color.R + ", " + color.G + ", " + color.B;
        this.Swatch.Background = new SolidColorBrush(color);
        this.Magnifier.SetImagePoint(imagePoint);
        this.InfoPanel.IsVisible = true;
        this.UpdatePanelPosition(viewPoint);
        this.HintPanel.IsVisible = true;
        if (!string.Equals(this.lastLoggedHex, hex, StringComparison.Ordinal))
        {
            this.lastLoggedHex = hex;
            UiLogger.Log("屏幕取色", "鼠标移动取色：图像横坐标=" + (int)imagePoint.X
                + " 图像纵坐标=" + (int)imagePoint.Y
                + " 颜色值=" + hex
                + " 红色=" + color.R + " 绿色=" + color.G + " 蓝色=" + color.B);
        }
    }

    /// <summary>把界面坐标换算为屏幕物理像素坐标。</summary>
    private Point ToImagePoint(Point viewPoint)
    {
        double x = Math.Max(0, Math.Min(viewPoint.X * this.scaling, this.screenArea.Width - 1));
        double y = Math.Max(0, Math.Min(viewPoint.Y * this.scaling, this.screenArea.Height - 1));
        return new Point(x, y);
    }

    /// <summary>把信息面板放到光标附近，靠近屏幕边缘时自动翻转。</summary>
    private void UpdatePanelPosition(Point viewPoint)
    {
        double panelWidth = 160;
        double panelHeight = 260;
        double x = viewPoint.X + 24;
        double y = viewPoint.Y + 24;
        if (x + panelWidth > this.Bounds.Width)
        {
            x = viewPoint.X - panelWidth - 24;
        }
        if (y + panelHeight > this.Bounds.Height)
        {
            y = viewPoint.Y - panelHeight - 24;
        }
        if (x < 0)
        {
            x = 0;
        }
        if (y < 0)
        {
            y = 0;
        }
        Canvas.SetLeft(this.InfoPanel, x);
        Canvas.SetTop(this.InfoPanel, y);
    }

    /// <summary>把颜色写入系统剪贴板并关闭窗口。</summary>
    private async void CopyColorAsync(string hex, Color color, Point imagePoint)
    {
        bool copied = false;
        IClipboard? clipboard = this.Clipboard;
        if (clipboard != null)
        {
            try
            {
                await clipboard.SetTextAsync(hex);
                copied = true;
            }
            catch (Exception error)
            {
                UiLogger.LogError("屏幕取色", "复制颜色到剪贴板失败", error);
            }
        }
        UiLogger.Log("屏幕取色", "取色完成：颜色值=" + hex
            + " 红色=" + color.R + " 绿色=" + color.G + " 蓝色=" + color.B
            + " 屏幕横坐标=" + (int)(this.screenArea.X + imagePoint.X)
            + " 屏幕纵坐标=" + (int)(this.screenArea.Y + imagePoint.Y)
            + " 复制到剪贴板=" + copied);
        this.Close();
    }
}
