using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using TabTaskExplorer.App.Services;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 贴图窗口（吸收 Snipaste 贴图）：把截图 / 剪贴板图片贴到屏幕最上层，可拖动、滚轮缩放、
/// Ctrl+滚轮调不透明度、旋转、复制、保存与关闭；跨平台一致，不依赖系统贴图组件。
/// </summary>
public partial class PinnedImageWindow : Window
{
    private readonly Bitmap image;

    private readonly AppSettings settings;

    private readonly PixelPoint initialPosition;

    private double screenScaling = 1.0;

    private double baseWidth;

    private double baseHeight;

    private double zoom = 1.0;

    private int rotationQuarter;

    private bool isDragging;

    private PixelPoint dragStartWindowPosition;

    private Point dragStartPointerPosition;

    public PinnedImageWindow(Bitmap image, PixelPoint position, AppSettings appSettings)
    {
        this.InitializeComponent();
        this.image = image;
        this.settings = appSettings;
        this.initialPosition = position;
        this.Picture.Source = image;
        this.Topmost = appSettings.CapturePinTopmost;
        this.Opened += this.OnWindowOpened;
        this.PointerPressed += this.OnPointerPressed;
        this.PointerMoved += this.OnPointerMoved;
        this.PointerReleased += this.OnPointerReleased;
        this.PointerWheelChanged += this.OnPointerWheelChanged;
        this.KeyDown += this.OnWindowKeyDown;
        this.Closed += this.OnWindowClosed;
    }

    /// <summary>窗口打开后计算初始尺寸、位置并应用设置。</summary>
    private void OnWindowOpened(object? sender, EventArgs e)
    {
        Screen? screen = this.Screens.ScreenFromPoint(this.initialPosition);
        if (screen == null)
        {
            screen = this.Screens.Primary;
        }
        this.screenScaling = screen == null ? 1.0 : screen.Scaling;
        if (this.screenScaling <= 0)
        {
            this.screenScaling = 1.0;
        }
        this.baseWidth = this.image.PixelSize.Width / this.screenScaling;
        this.baseHeight = this.image.PixelSize.Height / this.screenScaling;
        PixelRect workingArea = screen == null ? new PixelRect(0, 0, 1920, 1080) : screen.WorkingArea;
        double maximumWidth = workingArea.Width / this.screenScaling * 0.9;
        double maximumHeight = workingArea.Height / this.screenScaling * 0.9;
        double fit = 1.0;
        if (this.baseWidth > maximumWidth && this.baseWidth > 0)
        {
            fit = maximumWidth / this.baseWidth;
        }
        if (this.baseHeight > maximumHeight && this.baseHeight > 0)
        {
            double heightFit = maximumHeight / this.baseHeight;
            if (heightFit < fit)
            {
                fit = heightFit;
            }
        }
        this.zoom = fit;
        this.Width = Math.Max(80.0, this.baseWidth * this.zoom);
        this.Height = Math.Max(60.0, this.baseHeight * this.zoom);
        this.Position = this.ClampPosition(this.initialPosition);
        this.Activate();
        UiLogger.Log("贴图", "贴图窗口已打开：屏幕缩放比例=" + this.screenScaling
            + " 原始逻辑宽度=" + this.baseWidth
            + " 原始逻辑高度=" + this.baseHeight
            + " 初始缩放=" + this.zoom
            + " 窗口宽度=" + this.Width
            + " 窗口高度=" + this.Height
            + " 窗口横坐标=" + this.Position.X
            + " 窗口纵坐标=" + this.Position.Y
            + " 置顶=" + this.Topmost);
    }

    /// <summary>把窗口位置限制在工作区范围内，避免贴图出现在屏幕外。</summary>
    private PixelPoint ClampPosition(PixelPoint position)
    {
        Screen? screen = this.Screens.ScreenFromPoint(position);
        if (screen == null)
        {
            screen = this.Screens.Primary;
        }
        if (screen == null)
        {
            return position;
        }
        PixelRect area = screen.WorkingArea;
        int x = Math.Max(area.X, Math.Min(position.X, area.X + area.Width - 40));
        int y = Math.Max(area.Y, Math.Min(position.Y, area.Y + area.Height - 30));
        return new PixelPoint(x, y);
    }

    /// <summary>左键开始拖动；中键直接关闭（与 Snipaste 貼图操作一致）。</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsMiddleButtonPressed)
        {
            UiLogger.Log("贴图", "贴图窗口收到中键：结果=关闭当前贴图");
            this.Close();
            e.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }
        this.isDragging = true;
        this.dragStartPointerPosition = e.GetPosition(this);
        this.dragStartWindowPosition = this.Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>拖动窗口。</summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!this.isDragging)
        {
            return;
        }
        Point current = e.GetPosition(this);
        double deltaX = (current.X - this.dragStartPointerPosition.X) * this.screenScaling;
        double deltaY = (current.Y - this.dragStartPointerPosition.Y) * this.screenScaling;
        this.Position = new PixelPoint(
            this.dragStartWindowPosition.X + (int)Math.Round(deltaX),
            this.dragStartWindowPosition.Y + (int)Math.Round(deltaY));
    }

    /// <summary>结束拖动。</summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!this.isDragging)
        {
            return;
        }
        this.isDragging = false;
        e.Pointer.Capture(null);
        UiLogger.Log("贴图", "贴图窗口拖动结束：窗口横坐标=" + this.Position.X
            + " 窗口纵坐标=" + this.Position.Y);
    }

    /// <summary>滚轮缩放；按住 Ctrl 时调整不透明度。</summary>
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control)
        {
            double newOpacity = this.Opacity + (e.Delta.Y > 0 ? 0.1 : -0.1);
            if (newOpacity < 0.2)
            {
                newOpacity = 0.2;
            }
            if (newOpacity > 1.0)
            {
                newOpacity = 1.0;
            }
            this.Opacity = newOpacity;
            UiLogger.Log("贴图", "贴图不透明度已调整：不透明度=" + Math.Round(this.Opacity, 2));
            e.Handled = true;
            return;
        }
        double factor = e.Delta.Y > 0 ? 1.1 : 0.9;
        double newZoom = this.zoom * factor;
        if (newZoom < 0.1)
        {
            newZoom = 0.1;
        }
        if (newZoom > 8.0)
        {
            newZoom = 8.0;
        }
        this.ApplyZoom(newZoom);
        UiLogger.Log("贴图", "贴图缩放已调整：缩放比例=" + Math.Round(this.zoom, 2)
            + " 窗口宽度=" + this.Width
            + " 窗口高度=" + this.Height);
        e.Handled = true;
    }

    /// <summary>快捷键：Esc 关闭、R 旋转、Ctrl+C 复制、Ctrl+S 保存、Ctrl+0 原始大小。</summary>
    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (e.Key == Key.Escape)
        {
            this.Close();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.R)
        {
            this.Rotate();
            e.Handled = true;
            return;
        }
        if (control && e.Key == Key.C)
        {
            await this.CopyImageAsync();
            e.Handled = true;
            return;
        }
        if (control && e.Key == Key.S)
        {
            await this.SaveImageAsync();
            e.Handled = true;
            return;
        }
        if (control && e.Key == Key.D0)
        {
            this.ActualSize();
            e.Handled = true;
        }
    }

    /// <summary>应用缩放比例并同步窗口尺寸。</summary>
    private void ApplyZoom(double value)
    {
        this.zoom = value;
        double widthValue = Math.Max(60.0, this.baseWidth * this.zoom);
        double heightValue = Math.Max(40.0, this.baseHeight * this.zoom);
        if (this.rotationQuarter % 2 == 1)
        {
            this.Width = heightValue;
            this.Height = widthValue;
        }
        else
        {
            this.Width = widthValue;
            this.Height = heightValue;
        }
    }

    /// <summary>旋转 90 度：交换窗口宽高并同步图像旋转。</summary>
    private void Rotate()
    {
        this.rotationQuarter = (this.rotationQuarter + 1) % 4;
        RotateTransform rotate = new RotateTransform(this.rotationQuarter * 90.0);
        this.Picture.RenderTransform = rotate;
        this.Picture.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        this.ApplyZoom(this.zoom);
        UiLogger.Log("贴图", "贴图旋转：旋转角度=" + (this.rotationQuarter * 90) + " 窗口宽度=" + this.Width + " 窗口高度=" + this.Height);
    }

    /// <summary>原始大小（100%）。</summary>
    private void ActualSize()
    {
        this.ApplyZoom(1.0);
        UiLogger.Log("贴图", "贴图恢复原始大小：窗口宽度=" + this.Width + " 窗口高度=" + this.Height);
    }

    /// <summary>适应屏幕。</summary>
    private void FitScreen()
    {
        Screen? screen = this.Screens.ScreenFromWindow(this);
        if (screen == null)
        {
            screen = this.Screens.Primary;
        }
        if (screen == null)
        {
            return;
        }
        double maximumWidth = screen.WorkingArea.Width / this.screenScaling * 0.9;
        double maximumHeight = screen.WorkingArea.Height / this.screenScaling * 0.9;
        double fit = Math.Min(maximumWidth / Math.Max(1.0, this.baseWidth),
            maximumHeight / Math.Max(1.0, this.baseHeight));
        this.ApplyZoom(Math.Max(0.1, Math.Min(1.0, fit)));
        UiLogger.Log("贴图", "贴图适应屏幕：缩放比例=" + Math.Round(this.zoom, 2));
    }

    /// <summary>复制贴图到系统剪贴板。</summary>
    private async Task CopyImageAsync()
    {
        IClipboard? clipboard = this.Clipboard;
        if (clipboard == null)
        {
            UiLogger.Log("贴图", "复制贴图失败：系统剪贴板不可用");
            return;
        }
        try
        {
            await clipboard.SetBitmapAsync(this.image);
            UiLogger.Log("贴图", "贴图已复制到系统剪贴板：像素宽度=" + this.image.PixelSize.Width
                + " 像素高度=" + this.image.PixelSize.Height + " 结果=成功");
        }
        catch (Exception error)
        {
            UiLogger.LogError("贴图", "复制贴图到剪贴板失败", error);
        }
    }

    /// <summary>保存贴图到用户选择的 PNG 文件。</summary>
    private async Task SaveImageAsync()
    {
        FilePickerSaveOptions options = new FilePickerSaveOptions();
        options.Title = "保存贴图";
        options.SuggestedFileName = "贴图-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        options.DefaultExtension = "png";
        IStorageFile? file = await this.StorageProvider.SaveFilePickerAsync(options);
        if (file == null)
        {
            UiLogger.Log("贴图", "保存贴图：结果=取消，用户关闭了保存文件选择框");
            return;
        }
        try
        {
            using (System.IO.FileStream stream = new System.IO.FileStream(file.Path.LocalPath,
                System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
            {
                this.image.Save(stream);
            }
            UiLogger.Log("贴图", "贴图已保存：路径=" + file.Path.LocalPath + " 结果=成功");
        }
        catch (Exception error)
        {
            UiLogger.LogError("贴图", "保存贴图失败：路径=" + file.Path.LocalPath, error);
        }
    }

    /// <summary>右键菜单：复制图片。</summary>
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        await this.CopyImageAsync();
    }

    /// <summary>右键菜单：保存图片。</summary>
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        await this.SaveImageAsync();
    }

    /// <summary>右键菜单：旋转。</summary>
    private void OnRotateClick(object? sender, RoutedEventArgs e)
    {
        this.Rotate();
    }

    /// <summary>右键菜单：原始大小。</summary>
    private void OnActualSizeClick(object? sender, RoutedEventArgs e)
    {
        this.ActualSize();
    }

    /// <summary>右键菜单：适应屏幕。</summary>
    private void OnFitScreenClick(object? sender, RoutedEventArgs e)
    {
        this.FitScreen();
    }

    /// <summary>右键菜单：调整不透明度。</summary>
    private void OnOpacityClick(object? sender, RoutedEventArgs e)
    {
        MenuItem? item = sender as MenuItem;
        if (item == null || item.Tag == null)
        {
            return;
        }
        int percent;
        if (!int.TryParse(item.Tag.ToString(), out percent))
        {
            return;
        }
        this.Opacity = percent / 100.0;
        UiLogger.Log("贴图", "贴图不透明度已设置：不透明度=" + Math.Round(this.Opacity, 2));
    }

    /// <summary>右键菜单：关闭当前贴图。</summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

    /// <summary>右键菜单：关闭全部贴图。</summary>
    private void OnCloseAllClick(object? sender, RoutedEventArgs e)
    {
        PinWindowManager.CloseAll();
    }

    /// <summary>窗口关闭时释放位图。</summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        this.Picture.Source = null;
        this.image.Dispose();
    }
}
