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
using Avalonia.Platform.Storage;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.Services.Capture;
using TabTaskExplorer.App.ViewModels;
using TabTaskExplorer.Kernel.Capture;
using TabTaskExplorer.Kernel.Ocr;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 截图标注编辑器（吸收 Snipaste 截图编辑流程）：形状标注、文字、序号、马赛克、模糊、
/// 放大镜、橡皮擦、取色器、撤销重做、复制、保存、另存为、贴图与文字识别。
/// </summary>
public partial class CaptureEditorWindow : Window
{
    private readonly string sourcePath;

    private readonly CaptureArea captureArea;

    private readonly AppSettings settings;

    private readonly ITextRecognitionService ocrService;

    private Bitmap? loadedBitmap;

    private bool isPickingColor;

    private double brushSize = 14.0;

    private Color currentColor = Color.FromRgb(229, 57, 53);

    private double textFontSize = 24.0;

    public CaptureEditorWindow(string imagePath, CaptureArea area, AppSettings appSettings,
        ITextRecognitionService textRecognitionService)
    {
        this.InitializeComponent();
        // 标注编辑器是主窗口的附属工具窗口：Show(owner) 已设置 Owner，这里只去掉任务栏按钮
        WindowHostStyler.MakeAuxiliary(this);
        this.sourcePath = imagePath;
        this.captureArea = area;
        this.settings = appSettings;
        this.ocrService = textRecognitionService;
        this.Title = "标注截图 - " + Path.GetFileName(imagePath);
        this.Canvas.DocumentChanged += this.OnDocumentChanged;
        this.Canvas.ColorSampled += this.OnColorSampled;
        this.Canvas.ImagePointerMoved += this.OnImagePointerMoved;
        this.Canvas.TextRequested += this.OnTextRequested;
        this.KeyDown += this.OnWindowKeyDown;
        this.Closed += this.OnEditorClosed;
        this.LoadImage(imagePath);
        this.SetStatus("就绪：拖动绘制形状，右侧取色器可精确取色；Ctrl+C 复制，Ctrl+S 保存，Ctrl+T 贴图");
        UiLogger.Log("截图标注", "标注编辑器已打开：图片路径=" + imagePath
            + " 来源区域=" + area
            + " 文字识别可用=" + this.ocrService.IsAvailable
            + " 识别引擎=" + this.ocrService.ProviderName);
    }

    /// <summary>从已有图片打开标注编辑器时使用的入口。</summary>
    public static void Open(string imagePath, CaptureArea area, AppSettings settings,
        ITextRecognitionService ocrService, Window? owner)
    {
        CaptureEditorWindow window = new CaptureEditorWindow(imagePath, area, settings, ocrService);
        if (owner != null)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
        // 截图完成后立即出现在最前，与 Snipaste 的编辑流程一致；Show(owner) 只建立所属关系，不保证激活
        window.Activate();
    }

    /// <summary>读取图片并装载到画布与放大镜。</summary>
    private void LoadImage(string imagePath)
    {
        try
        {
            using (FileStream stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                this.loadedBitmap = new Bitmap(stream);
            }
            this.Canvas.LoadImage(this.loadedBitmap);
            this.Magnifier.SetSource(this.loadedBitmap);
            this.Magnifier.SetZoom(8.0);
            UiLogger.Log("截图标注", "图片已装载：路径=" + imagePath
                + " 像素宽度=" + this.loadedBitmap.PixelSize.Width
                + " 像素高度=" + this.loadedBitmap.PixelSize.Height);
        }
        catch (Exception error)
        {
            UiLogger.LogError("截图标注", "装载图片失败：路径=" + imagePath, error);
            MessageDialog.Show(null, "无法打开标注编辑器", "读取图片失败：" + error.Message);
            this.Close();
        }
    }

    /// <summary>工具按钮：按 Tag 切换当前标注工具。</summary>
    private void OnToolClick(object? sender, RoutedEventArgs e)
    {
        RadioButton? button = sender as RadioButton;
        if (button == null)
        {
            return;
        }
        string tag = button.Tag == null ? string.Empty : button.Tag.ToString() ?? string.Empty;
        AnnotationTool tool = ParseTool(tag);
        this.Canvas.ActiveTool = tool;
        this.SetStatus("当前工具：" + DescribeTool(tool));
        UiLogger.Log("截图标注", "标注工具已切换：工具=" + DescribeTool(tool)
            + " 工具标识=" + tag);
    }

    /// <summary>颜色按钮：切换当前标注颜色。</summary>
    private void OnColorClick(object? sender, RoutedEventArgs e)
    {
        Button? button = sender as Button;
        if (button == null || button.Tag == null)
        {
            return;
        }
        this.ApplyColor(button.Tag.ToString() ?? string.Empty, "调色板");
    }

    /// <summary>更多颜色：手工输入十六进制颜色。</summary>
    private async void OnCustomColorClick(object? sender, RoutedEventArgs e)
    {
        string? value = await InputDialog.Show(this, "自定义颜色", "请输入十六进制颜色（例如 #12AB34 或 12AB34）", "#12AB34");
        if (value == null)
        {
            UiLogger.Log("截图标注", "自定义颜色已取消：用户没有提交输入");
            return;
        }
        this.ApplyColor(value, "手工输入");
    }

    /// <summary>把十六进制颜色应用到画布与右侧色块。</summary>
    private void ApplyColor(string hex, string source)
    {
        string normalized = hex.Trim();
        if (normalized.Length == 0)
        {
            return;
        }
        if (!normalized.StartsWith("#"))
        {
            normalized = "#" + normalized;
        }
        try
        {
            Color color = Color.Parse(normalized);
            this.currentColor = color;
            this.Canvas.ActiveColor = color;
            this.ColorSwatch.Background = new SolidColorBrush(color);
            this.ColorHexText.Text = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
            this.ColorRgbText.Text = "RGB " + color.R + ", " + color.G + ", " + color.B;
            UiLogger.Log("截图标注", "标注颜色已更新：来源=" + source
                + " 颜色值=" + this.ColorHexText.Text
                + " 红色=" + color.R + " 绿色=" + color.G + " 蓝色=" + color.B);
        }
        catch (Exception error)
        {
            UiLogger.LogError("截图标注", "颜色解析失败：来源=" + source + " 输入值=" + hex, error);
            this.SetStatus("颜色格式无效：" + hex);
        }
    }

    /// <summary>粗细选择：同时影响画笔、马赛克、模糊笔刷与文字 / 序号大小。</summary>
    private void OnSizeChanged(object? sender, SelectionChangedEventArgs e)
    {
        // XAML 初始化阶段 SelectedIndex 会提前触发本事件，此时命名控件尚未赋值，
        // 直接返回并使用构造函数与画布的默认粗细，避免空引用异常。
        if (this.SizeCombo == null || this.Canvas == null)
        {
            return;
        }
        int index = this.SizeCombo.SelectedIndex;
        double[] brushSizes = new double[] { 6.0, 14.0, 24.0, 40.0 };
        double[] lineSizes = new double[] { 2.0, 3.0, 5.0, 8.0 };
        double[] textSizes = new double[] { 18.0, 24.0, 32.0, 44.0 };
        if (index < 0 || index >= brushSizes.Length)
        {
            index = 1;
        }
        this.brushSize = brushSizes[index];
        this.textFontSize = textSizes[index];
        this.Canvas.BrushSize = this.brushSize;
        this.Canvas.ActiveThickness = lineSizes[index];
        this.Canvas.TextFontSize = this.textFontSize;
        UiLogger.Log("截图标注", "笔刷粗细已切换：索引=" + index
            + " 笔刷宽度=" + this.brushSize
            + " 线条宽度=" + lineSizes[index]
            + " 文字字号=" + this.textFontSize);
    }

    /// <summary>填充开关：矩形与椭圆是否填充。</summary>
    private void OnFillChanged(object? sender, RoutedEventArgs e)
    {
        this.Canvas.FillShapes = this.FillCheck.IsChecked == true;
        UiLogger.Log("截图标注", "图形填充开关已切换：填充=" + this.Canvas.FillShapes);
    }

    /// <summary>取色模式开关。</summary>
    private void OnTogglePickColorClick(object? sender, RoutedEventArgs e)
    {
        this.isPickingColor = !this.isPickingColor;
        this.Canvas.IsColorPicking = this.isPickingColor;
        this.PickColorButton.Content = this.isPickingColor
            ? "取色模式：开启（点击画布取色）"
            : "取色模式：关闭（点击开启）";
        this.SetStatus(this.isPickingColor
            ? "取色模式已开启：点击画布任意位置复制十六进制颜色"
            : "取色模式已关闭");
        UiLogger.Log("截图标注", "取色模式已切换：开启=" + this.isPickingColor);
    }

    /// <summary>取色完成：刷新色块、数值并把十六进制颜色写入系统剪贴板。</summary>
    private async void OnColorSampled(string hex, Color color, Point imagePoint)
    {
        this.currentColor = color;
        this.Canvas.ActiveColor = color;
        this.ColorSwatch.Background = new SolidColorBrush(color);
        this.ColorHexText.Text = hex;
        this.ColorRgbText.Text = "RGB " + color.R + ", " + color.G + ", " + color.B;
        this.SetStatus("已取色：" + hex + "（RGB " + color.R + ", " + color.G + ", " + color.B + "）已复制到剪贴板");
        bool copied = await this.WriteTextToClipboardAsync(hex);
        UiLogger.Log("截图标注", "取色完成：十六进制=" + hex
            + " 红色=" + color.R + " 绿色=" + color.G + " 蓝色=" + color.B
            + " 图像横坐标=" + (int)imagePoint.X + " 图像纵坐标=" + (int)imagePoint.Y
            + " 复制到剪贴板=" + copied);
    }

    /// <summary>指针移动：刷新放大镜与坐标显示。</summary>
    private void OnImagePointerMoved(Point imagePoint)
    {
        this.Magnifier.SetImagePoint(imagePoint);
        this.PositionText.Text = "坐标：" + (int)imagePoint.X + ", " + (int)imagePoint.Y
            + "（原图像素）";
    }

    /// <summary>需要输入文字：弹出输入框后在落点追加文字标注。</summary>
    private async void OnTextRequested(Point imagePoint)
    {
        string? text = await InputDialog.Show(this, "添加文字", "请输入要标注的文字（可输入多行）", string.Empty);
        if (text == null || text.Length == 0)
        {
            UiLogger.Log("截图标注", "文字标注已取消：用户没有输入内容");
            return;
        }
        this.Canvas.AddText(imagePoint, text);
        this.SetStatus("已添加文字标注：" + text);
        UiLogger.Log("截图标注", "文字标注已添加：内容=" + text
            + " 图像横坐标=" + (int)imagePoint.X + " 图像纵坐标=" + (int)imagePoint.Y
            + " 字号=" + this.textFontSize);
    }

    /// <summary>撤销。</summary>
    private void OnUndoClick(object? sender, RoutedEventArgs e)
    {
        bool changed = this.Canvas.Undo();
        this.SetStatus(changed ? "已撤销一步" : "没有可以撤销的标注");
        UiLogger.Log("截图标注", "撤销操作：结果=" + (changed ? "成功" : "没有可撤销的标注"));
    }

    /// <summary>重做。</summary>
    private void OnRedoClick(object? sender, RoutedEventArgs e)
    {
        bool changed = this.Canvas.Redo();
        this.SetStatus(changed ? "已重做一步" : "没有可以重做的标注");
        UiLogger.Log("截图标注", "重做操作：结果=" + (changed ? "成功" : "没有可重做的标注"));
    }

    /// <summary>清空全部标注。</summary>
    private async void OnClearClick(object? sender, RoutedEventArgs e)
    {
        if (this.Canvas.Document.Shapes.Count == 0)
        {
            this.SetStatus("当前没有标注可清空");
            return;
        }
        bool confirmed = await MessageDialog.ConfirmAsync(this, "清空标注",
            "确定清空全部标注吗？清空后仍可通过撤销恢复。", "清空", "取消");
        if (!confirmed)
        {
            UiLogger.Log("截图标注", "清空标注已取消：用户选择取消");
            return;
        }
        this.Canvas.ClearShapes();
        this.SetStatus("全部标注已清空，可使用撤销恢复");
        UiLogger.Log("截图标注", "清空标注：结果=成功，全部标注已移除");
    }

    /// <summary>复制合成图到系统剪贴板。</summary>
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        await this.CopyToClipboardAsync();
    }

    /// <summary>保存到原文件（覆盖截图原图）。</summary>
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        await this.SaveToFileAsync(this.sourcePath, false);
    }

    /// <summary>另存为新的 PNG 文件。</summary>
    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        FilePickerSaveOptions options = new FilePickerSaveOptions();
        options.Title = "另存标注图片";
        options.SuggestedFileName = Path.GetFileNameWithoutExtension(this.sourcePath) + "-标注";
        options.DefaultExtension = "png";
        IStorageFile? file = await this.StorageProvider.SaveFilePickerAsync(options);
        if (file == null)
        {
            this.SetStatus("另存为已取消");
            UiLogger.Log("截图标注", "另存为：结果=取消，用户关闭了保存文件选择框");
            return;
        }
        await this.SaveToFileAsync(file.Path.LocalPath, true);
    }

    /// <summary>识别当前合成图中的二维码 / 条码（吸收 Snipaste BarcodeDecoder 能力）。</summary>
    private void OnBarcodeClick(object? sender, RoutedEventArgs e)
    {
        RenderTargetBitmap image = this.Canvas.Flatten();
        BarcodeScanResult result = BarcodeDetectionService.Decode(image);
        if (!result.Success)
        {
            this.SetStatus("扫码失败：" + result.ErrorMessage);
            MessageDialog.Show(this, "二维码 / 条码识别", result.ErrorMessage);
            UiLogger.Log("扫码", "编辑器扫码失败：原因=" + result.ErrorMessage
                + " 像素宽度=" + image.PixelSize.Width + " 像素高度=" + image.PixelSize.Height);
            return;
        }
        this.SetStatus("扫码成功：" + result.Format + " " + result.Text);
        UiLogger.Log("扫码", "编辑器扫码成功：码制=" + result.Format
            + " 内容长度=" + result.Text.Length
            + " 内容=" + result.Text
            + " 像素宽度=" + image.PixelSize.Width + " 像素高度=" + image.PixelSize.Height);
        BarcodeResultWindow window = new BarcodeResultWindow(result);
        window.Show(this);
    }

    /// <summary>贴图：把合成图贴到屏幕最上层。</summary>
    private void OnPinClick(object? sender, RoutedEventArgs e)
    {
        this.PinCurrentImage();
    }

    /// <summary>识别当前合成图中的文字。</summary>
    private async void OnOcrClick(object? sender, RoutedEventArgs e)
    {
        if (!this.ocrService.IsAvailable)
        {
            MessageDialog.Show(this, "文字识别不可用", this.ocrService.DescribeAvailability());
            UiLogger.Log("截图标注", "文字识别被拒绝：引擎不可用 说明=" + this.ocrService.DescribeAvailability());
            return;
        }
        this.SetStatus("正在识别文字……");
        RenderTargetBitmap image = this.Canvas.Flatten();
        byte[] pngBytes;
        using (MemoryStream stream = new MemoryStream())
        {
            image.Save(stream);
            pngBytes = stream.ToArray();
        }
        TextRecognitionResult result = await this.ocrService.RecognizeAsync(pngBytes, this.settings.OcrLanguage);
        if (!result.Success)
        {
            this.SetStatus("文字识别失败：" + result.ErrorMessage);
            MessageDialog.Show(this, "文字识别失败", result.ErrorMessage);
            UiLogger.Log("截图标注", "文字识别失败：引擎=" + result.ProviderName + " 原因=" + result.ErrorMessage);
            return;
        }
        if (result.Text.Length == 0)
        {
            this.SetStatus("没有识别到文字");
            MessageDialog.Show(this, "文字识别结果", "当前图片中没有识别到文字。");
            UiLogger.Log("截图标注", "文字识别完成：引擎=" + result.ProviderName + " 结果=未识别到文字");
            return;
        }
        this.SetStatus("文字识别完成：" + result.Lines.Count + " 行");
        UiLogger.Log("截图标注", "文字识别完成：引擎=" + result.ProviderName
            + " 语言=" + result.Language
            + " 行数=" + result.Lines.Count
            + " 文字长度=" + result.Text.Length);
        OcrResultWindow window = new OcrResultWindow(result);
        window.Show(this);
    }

    /// <summary>关闭窗口。</summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

    /// <summary>键盘快捷键：与 Snipaste 编辑器保持一致。</summary>
    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key == Key.Escape)
        {
            this.Close();
            e.Handled = true;
            return;
        }
        if (control && e.Key == Key.C)
        {
            await this.CopyToClipboardAsync();
            e.Handled = true;
            return;
        }
        if (control && e.Key == Key.S)
        {
            await this.SaveToFileAsync(this.sourcePath, shift);
            e.Handled = true;
            return;
        }
        if (control && e.Key == Key.T)
        {
            this.PinCurrentImage();
            e.Handled = true;
            return;
        }
        if (control && e.Key == Key.Z)
        {
            bool changed = shift ? this.Canvas.Redo() : this.Canvas.Undo();
            this.SetStatus(changed ? (shift ? "已重做一步" : "已撤销一步") : "没有可用的历史记录");
            e.Handled = true;
            return;
        }
        if (control && e.Key == Key.Y)
        {
            bool changed = this.Canvas.Redo();
            this.SetStatus(changed ? "已重做一步" : "没有可以重做的标注");
            e.Handled = true;
            return;
        }
        if (control && e.Key == Key.O)
        {
            this.isPickingColor = !this.isPickingColor;
            this.Canvas.IsColorPicking = this.isPickingColor;
            this.PickColorButton.Content = this.isPickingColor
                ? "取色模式：开启（点击画布取色）"
                : "取色模式：关闭（点击开启）";
            e.Handled = true;
        }
    }

    /// <summary>复制合成图到系统剪贴板。</summary>
    private async Task CopyToClipboardAsync()
    {
        RenderTargetBitmap image = this.Canvas.Flatten();
        bool copied = await this.WriteBitmapToClipboardAsync(image);
        if (copied)
        {
            this.SetStatus("已复制合成图到剪贴板：" + image.PixelSize.Width + " × " + image.PixelSize.Height + " 像素");
        }
        else
        {
            this.SetStatus("复制失败：无法写入系统剪贴板");
        }
        UiLogger.Log("截图标注", "复制合成图到剪贴板：像素宽度=" + image.PixelSize.Width
            + " 像素高度=" + image.PixelSize.Height
            + " 结果=" + (copied ? "成功" : "失败"));
    }

    /// <summary>保存合成图到指定路径。</summary>
    private async Task SaveToFileAsync(string path, bool saveAs)
    {
        if (!saveAs && path.Length == 0)
        {
            return;
        }
        RenderTargetBitmap image = this.Canvas.Flatten();
        try
        {
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                image.Save(stream);
            }
            this.SetStatus("已保存：" + path);
            UiLogger.Log("截图标注", "保存合成图：路径=" + path
                + " 是否另存为=" + saveAs
                + " 像素宽度=" + image.PixelSize.Width
                + " 像素高度=" + image.PixelSize.Height
                + " 结果=成功");
            this.RefreshHistoryIfNeeded(path);
        }
        catch (Exception error)
        {
            this.SetStatus("保存失败：" + error.Message);
            UiLogger.LogError("截图标注", "保存合成图失败：路径=" + path, error);
            MessageDialog.Show(this, "保存失败", "无法写入图片文件：" + error.Message);
        }
    }

    /// <summary>贴图当前合成图。</summary>
    private void PinCurrentImage()
    {
        RenderTargetBitmap image = this.Canvas.Flatten();
        PixelPoint position = new PixelPoint(this.captureArea.X, this.captureArea.Y);
        PinWindowManager.ShowPinnedImage(image, position, this.settings, this.ocrService);
        this.SetStatus("已贴图到屏幕：" + image.PixelSize.Width + " × " + image.PixelSize.Height + " 像素");
        UiLogger.Log("截图标注", "贴图已完成：像素宽度=" + image.PixelSize.Width
            + " 像素高度=" + image.PixelSize.Height
            + " 贴图位置横坐标=" + position.X
            + " 贴图位置纵坐标=" + position.Y);
    }

    /// <summary>把位图写入系统剪贴板。</summary>
    private async Task<bool> WriteBitmapToClipboardAsync(Bitmap bitmap)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        IClipboard? clipboard = topLevel == null ? null : topLevel.Clipboard;
        if (clipboard == null)
        {
            return false;
        }
        try
        {
            await clipboard.SetBitmapAsync(bitmap);
            return true;
        }
        catch (Exception error)
        {
            UiLogger.LogError("截图标注", "写入系统剪贴板失败", error);
            return false;
        }
    }

    /// <summary>把纯文本写入系统剪贴板。</summary>
    private async Task<bool> WriteTextToClipboardAsync(string text)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        IClipboard? clipboard = topLevel == null ? null : topLevel.Clipboard;
        if (clipboard == null)
        {
            return false;
        }
        try
        {
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception error)
        {
            UiLogger.LogError("截图标注", "写入文本到系统剪贴板失败", error);
            return false;
        }
    }

    /// <summary>保存后刷新截图录屏历史（仅当保存到产物目录时）。</summary>
    private void RefreshHistoryIfNeeded(string path)
    {
        MainWindow? mainWindow = WindowRegistry.Primary;
        if (mainWindow == null)
        {
            return;
        }
        MainWindowViewModel? viewModel = mainWindow.DataContext as MainWindowViewModel;
        if (viewModel == null)
        {
            return;
        }
        string directory = CapturePaths.ResolveOutputDirectory(this.settings);
        string? fileDirectory = Path.GetDirectoryName(path);
        if (fileDirectory == null)
        {
            return;
        }
        if (string.Equals(Path.GetFullPath(directory), Path.GetFullPath(fileDirectory), StringComparison.OrdinalIgnoreCase))
        {
            viewModel.OpenCaptureTab();
            foreach (object item in viewModel.Tabs)
            {
                CaptureTabViewModel? captureTab = item as CaptureTabViewModel;
                if (captureTab != null)
                {
                    captureTab.RefreshHistory();
                    break;
                }
            }
        }
    }

    /// <summary>撤销栈变化时无需额外处理，撤销与重做按钮始终可用，操作后由画布返回结果。</summary>
    private void OnDocumentChanged()
    {
    }

    /// <summary>设置状态栏文字。</summary>
    private void SetStatus(string text)
    {
        this.StatusText.Text = text;
    }

    /// <summary>关闭窗口时释放图片与采样器。</summary>
    private void OnEditorClosed(object? sender, EventArgs e)
    {
        this.Canvas.DisposeImage();
        this.Magnifier.SetSource(null);
        this.loadedBitmap = null;
        UiLogger.Log("截图标注", "标注编辑器已关闭：图片路径=" + this.sourcePath);
    }

    /// <summary>把工具标识解析为枚举。</summary>
    private static AnnotationTool ParseTool(string tag)
    {
        if (string.Equals(tag, "Rectangle", StringComparison.Ordinal))
        {
            return AnnotationTool.Rectangle;
        }
        if (string.Equals(tag, "Ellipse", StringComparison.Ordinal))
        {
            return AnnotationTool.Ellipse;
        }
        if (string.Equals(tag, "Arrow", StringComparison.Ordinal))
        {
            return AnnotationTool.Arrow;
        }
        if (string.Equals(tag, "Highlight", StringComparison.Ordinal))
        {
            return AnnotationTool.Highlight;
        }
        if (string.Equals(tag, "Mosaic", StringComparison.Ordinal))
        {
            return AnnotationTool.Mosaic;
        }
        if (string.Equals(tag, "Blur", StringComparison.Ordinal))
        {
            return AnnotationTool.Blur;
        }
        if (string.Equals(tag, "Text", StringComparison.Ordinal))
        {
            return AnnotationTool.Text;
        }
        if (string.Equals(tag, "Number", StringComparison.Ordinal))
        {
            return AnnotationTool.Number;
        }
        if (string.Equals(tag, "Magnify", StringComparison.Ordinal))
        {
            return AnnotationTool.Magnify;
        }
        if (string.Equals(tag, "Eraser", StringComparison.Ordinal))
        {
            return AnnotationTool.Eraser;
        }
        return AnnotationTool.Pen;
    }

    /// <summary>把工具枚举翻译为中文名称。</summary>
    private static string DescribeTool(AnnotationTool tool)
    {
        switch (tool)
        {
            case AnnotationTool.Rectangle:
                return "矩形";
            case AnnotationTool.Ellipse:
                return "椭圆";
            case AnnotationTool.Arrow:
                return "箭头";
            case AnnotationTool.Pen:
                return "画笔";
            case AnnotationTool.Highlight:
                return "荧光笔";
            case AnnotationTool.Mosaic:
                return "马赛克";
            case AnnotationTool.Blur:
                return "模糊";
            case AnnotationTool.Text:
                return "文字";
            case AnnotationTool.Number:
                return "序号";
            case AnnotationTool.Magnify:
                return "放大镜";
            case AnnotationTool.Eraser:
                return "橡皮擦";
            default:
                return "无";
        }
    }
}
