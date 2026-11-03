using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.Services.Capture;
using TabTaskExplorer.Kernel.Capture;
using TabTaskExplorer.Kernel.Ocr;

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

    private readonly ITextRecognitionService? ocrService;

    private double screenScaling = 1.0;

    private double baseWidth;

    private double baseHeight;

    private double zoom = 1.0;

    private int rotationQuarter;

    private bool flipHorizontal;

    private bool flipVertical;

    private bool isUpdatingScreenScale;

    private bool isDragging;

    private PixelPoint dragStartWindowPosition;

    private Point dragStartPointerPosition;

    public PinnedImageWindow(Bitmap image, PixelPoint position, AppSettings appSettings,
        ITextRecognitionService? textRecognitionService = null)
    {
        this.InitializeComponent();
        // 贴图是独立工具窗口：不进入任务栏与 Alt+Tab；不设置 Owner，
        // 保证主窗口关闭到托盘时贴图仍留在屏幕上并可单独关闭，不会变成不可见孤儿窗口
        WindowHostStyler.MakeAuxiliary(this);
        this.image = image;
        this.settings = appSettings;
        this.initialPosition = position;
        this.ocrService = textRecognitionService;
        this.Picture.Source = image;
        this.Topmost = appSettings.CapturePinTopmost;
        // 贴图出现时不激活自身，避免抢占目标程序的键盘焦点；用户主动点击贴图后才接收输入
        this.ShowActivated = false;
        RenderOptions.SetBitmapInterpolationMode(this.Picture, BitmapInterpolationMode.HighQuality);
        this.UpdateTopmostMenu();
        this.Opened += this.OnWindowOpened;
        this.PointerPressed += this.OnPointerPressed;
        this.PointerMoved += this.OnPointerMoved;
        this.PointerReleased += this.OnPointerReleased;
        this.PointerWheelChanged += this.OnPointerWheelChanged;
        this.KeyDown += this.OnWindowKeyDown;
        this.PositionChanged += this.OnWindowPositionChanged;
        this.Screens.Changed += this.OnScreensChanged;
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
        this.UpdateTopmostMenu();
        UiLogger.Log("贴图", "贴图窗口已打开：屏幕缩放比例=" + this.screenScaling
            + " 原始逻辑宽度=" + this.baseWidth
            + " 原始逻辑高度=" + this.baseHeight
            + " 初始缩放=" + this.zoom
            + " 窗口宽度=" + this.Width
            + " 窗口高度=" + this.Height
            + " 窗口横坐标=" + this.Position.X
            + " 窗口纵坐标=" + this.Position.Y
            + " 置顶=" + this.Topmost
            + " 显示时激活=False（不抢焦点）");
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
        // 用户主动点击贴图后才激活并接收快捷键；只是显示贴图时不抢焦点
        this.Activate();
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
        if (e.Key == Key.H)
        {
            this.FlipHorizontal();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.V)
        {
            this.FlipVertical();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.T)
        {
            this.ToggleTopmost();
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
        this.ApplyTransform();
        this.ApplyZoom(this.zoom);
        UiLogger.Log("贴图", "贴图旋转：旋转角度=" + (this.rotationQuarter * 90)
            + " 窗口宽度=" + this.Width + " 窗口高度=" + this.Height);
    }

    /// <summary>水平翻转：与旋转叠加，保持窗口尺寸与缩放比例。</summary>
    private void FlipHorizontal()
    {
        this.flipHorizontal = !this.flipHorizontal;
        this.ApplyTransform();
        UiLogger.Log("贴图", "贴图水平翻转：水平翻转=" + this.flipHorizontal
            + " 垂直翻转=" + this.flipVertical);
    }

    /// <summary>垂直翻转：与旋转叠加，保持窗口尺寸与缩放比例。</summary>
    private void FlipVertical()
    {
        this.flipVertical = !this.flipVertical;
        this.ApplyTransform();
        UiLogger.Log("贴图", "贴图垂直翻转：水平翻转=" + this.flipHorizontal
            + " 垂直翻转=" + this.flipVertical);
    }

    /// <summary>组合翻转与旋转：先按图片自身坐标翻转，再绕中心旋转。</summary>
    private void ApplyTransform()
    {
        TransformGroup group = new TransformGroup();
        group.Children.Add(new ScaleTransform(this.flipHorizontal ? -1.0 : 1.0,
            this.flipVertical ? -1.0 : 1.0));
        group.Children.Add(new RotateTransform(this.rotationQuarter * 90.0));
        this.Picture.RenderTransform = group;
        this.Picture.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
    }

    /// <summary>切换置顶状态并同步右键菜单文字。</summary>
    private void ToggleTopmost()
    {
        this.Topmost = !this.Topmost;
        this.UpdateTopmostMenu();
        UiLogger.Log("贴图", "贴图置顶状态已切换：置顶=" + this.Topmost);
    }

    /// <summary>把当前置顶状态写入右键菜单，保证菜单与实际窗口一致。</summary>
    private void UpdateTopmostMenu()
    {
        if (this.TopmostMenuItem == null)
        {
            return;
        }
        this.TopmostMenuItem.Header = this.Topmost ? "置顶：开（T）" : "置顶：关（T）";
    }

    /// <summary>窗口位置变化：跨显示器拖动时按新屏幕 DPI 重算逻辑尺寸，保持物理像素清晰与比例正确。</summary>
    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (this.isUpdatingScreenScale)
        {
            return;
        }
        int centerX = e.Point.X + (int)Math.Round(this.Width * this.screenScaling * 0.5);
        int centerY = e.Point.Y + (int)Math.Round(this.Height * this.screenScaling * 0.5);
        Screen? screen = this.Screens.ScreenFromPoint(new PixelPoint(centerX, centerY));
        if (screen == null)
        {
            screen = this.Screens.ScreenFromWindow(this);
        }
        this.UpdateScreenScale(screen, "窗口位置变化");
    }

    /// <summary>显示器增删或缩放变化：重新按当前屏幕 DPI 计算贴图尺寸。</summary>
    private void OnScreensChanged(object? sender, EventArgs e)
    {
        this.UpdateScreenScale(this.Screens.ScreenFromWindow(this), "显示器变化");
    }

    /// <summary>按屏幕缩放比例重算贴图逻辑尺寸：同一张图在不同 DPI 屏幕上保持相同的物理像素与宽高比。</summary>
    private void UpdateScreenScale(Screen? screen, string reason)
    {
        if (screen == null || this.isUpdatingScreenScale)
        {
            return;
        }
        double scaling = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        if (Math.Abs(scaling - this.screenScaling) < 0.001)
        {
            return;
        }
        double previousScaling = this.screenScaling;
        this.isUpdatingScreenScale = true;
        try
        {
            this.screenScaling = scaling;
            this.baseWidth = this.image.PixelSize.Width / scaling;
            this.baseHeight = this.image.PixelSize.Height / scaling;
            this.ApplyZoom(this.zoom);
            UiLogger.Log("贴图", "贴图已按新显示器 DPI 重算：原因=" + reason
                + " 原缩放比例=" + previousScaling
                + " 新缩放比例=" + scaling
                + " 窗口宽度=" + this.Width
                + " 窗口高度=" + this.Height
                + " 缩放比例=" + Math.Round(this.zoom, 2));
        }
        finally
        {
            this.isUpdatingScreenScale = false;
        }
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

    /// <summary>右键菜单：识别贴图文字（吸收 Snipaste 贴图上的「复制文字」）。</summary>
    private async void OnOcrClick(object? sender, RoutedEventArgs e)
    {
        if (this.ocrService == null || !this.ocrService.IsAvailable)
        {
            string description = this.ocrService == null
                ? "当前没有可用的文字识别引擎"
                : this.ocrService.DescribeAvailability();
            MessageDialog.Show(this, "文字识别不可用", description);
            UiLogger.Log("贴图", "贴图文字识别被拒绝：原因=" + description);
            return;
        }
        byte[] pngBytes;
        using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
        {
            this.image.Save(stream);
            pngBytes = stream.ToArray();
        }
        TextRecognitionResult result = await this.ocrService.RecognizeAsync(pngBytes, this.settings.OcrLanguage);
        if (!result.Success)
        {
            MessageDialog.Show(this, "文字识别失败", result.ErrorMessage);
            UiLogger.Log("贴图", "贴图文字识别失败：引擎=" + result.ProviderName + " 原因=" + result.ErrorMessage);
            return;
        }
        UiLogger.Log("贴图", "贴图文字识别完成：引擎=" + result.ProviderName
            + " 行数=" + result.Lines.Count
            + " 文字长度=" + result.Text.Length);
        OcrResultWindow window = new OcrResultWindow(result);
        window.Show(this);
    }

    /// <summary>右键菜单：识别贴图中的二维码 / 条码。</summary>
    private void OnBarcodeClick(object? sender, RoutedEventArgs e)
    {
        BarcodeScanResult result = BarcodeDetectionService.Decode(this.image);
        if (!result.Success)
        {
            MessageDialog.Show(this, "二维码 / 条码识别", result.ErrorMessage);
            UiLogger.Log("贴图", "贴图扫码失败：原因=" + result.ErrorMessage);
            return;
        }
        UiLogger.Log("贴图", "贴图扫码成功：码制=" + result.Format
            + " 内容长度=" + result.Text.Length
            + " 内容=" + result.Text);
        BarcodeResultWindow window = new BarcodeResultWindow(result);
        window.Show(this);
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

    /// <summary>右键菜单：水平翻转。</summary>
    private void OnFlipHorizontalClick(object? sender, RoutedEventArgs e)
    {
        this.FlipHorizontal();
    }

    /// <summary>右键菜单：垂直翻转。</summary>
    private void OnFlipVerticalClick(object? sender, RoutedEventArgs e)
    {
        this.FlipVertical();
    }

    /// <summary>右键菜单：切换置顶。</summary>
    private void OnToggleTopmostClick(object? sender, RoutedEventArgs e)
    {
        this.ToggleTopmost();
    }

    /// <summary>右键菜单：把贴图送入标注编辑器继续标注（吸收 Snipaste 贴图上继续编辑的流程）。</summary>
    private void OnAnnotateClick(object? sender, RoutedEventArgs e)
    {
        if (this.ocrService == null)
        {
            UiLogger.Log("贴图", "贴图标注被拒绝：没有可用的文字识别服务上下文");
            MessageDialog.Show(this, "无法标注", "当前贴图窗口没有可用的文字识别服务，无法打开标注编辑器。");
            return;
        }
        try
        {
            string directory = Path.Combine(Path.GetTempPath(), "TabTaskExplorer");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "贴图标注-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                this.image.Save(stream);
            }
            CaptureArea area = new CaptureArea(this.Position.X, this.Position.Y,
                this.image.PixelSize.Width, this.image.PixelSize.Height);
            CaptureEditorWindow.Open(path, area, this.settings, this.ocrService, this);
            UiLogger.Log("贴图", "贴图已送入标注编辑器：临时文件=" + path
                + " 像素宽度=" + this.image.PixelSize.Width
                + " 像素高度=" + this.image.PixelSize.Height);
        }
        catch (Exception error)
        {
            UiLogger.LogError("贴图", "打开贴图标注编辑器失败", error);
            MessageDialog.Show(this, "无法标注", "写入临时图片失败：" + error.Message);
        }
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

    /// <summary>窗口关闭时释放位图并取消屏幕与位置订阅。</summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        this.PositionChanged -= this.OnWindowPositionChanged;
        this.Screens.Changed -= this.OnScreensChanged;
        this.Picture.Source = null;
        this.image.Dispose();
    }
}
