using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace TabTaskExplorer.App.Services.Capture;

/// <summary>
/// 标注画布：显示截图原图并绘制标注形状，负责指针交互（矩形、椭圆、箭头、画笔、
/// 荧光笔、马赛克、模糊、文字、序号、放大镜、橡皮擦）与取色。坐标换算统一使用原图像素。
/// </summary>
public class AnnotationCanvas : Control
{
    private readonly AnnotationDocument document = new AnnotationDocument();

    private Bitmap? source;

    private PixelSampler? sourceSampler;

    private PixelSampler? blurSampler;

    private AnnotationShape? activeShape;

    private Point dragStartImagePoint;

    private bool isDragging;

    private bool isColorPicking;

    private double imageDisplayScale = 1.0;

    private double imageOffsetX;

    private double imageOffsetY;

    private Color activeColor = Color.FromRgb(255, 59, 48);

    private double activeThickness = 3.0;

    private double brushSize = 14.0;

    private int nextNumber = 1;

    /// <summary>标注文档发生变化（新增、撤销、重做、删除）。</summary>
    public event Action? DocumentChanged;

    /// <summary>取色完成：参数为十六进制颜色、颜色值与图像像素坐标。</summary>
    public event Action<string, Color, Point>? ColorSampled;

    /// <summary>指针在图像上的坐标变化（用于放大镜与坐标显示）。</summary>
    public event Action<Point>? ImagePointerMoved;

    /// <summary>需要输入文字：参数为落点的图像像素坐标。</summary>
    public event Action<Point>? TextRequested;

    /// <summary>构造标注画布。</summary>
    public AnnotationCanvas()
    {
        this.ClipToBounds = true;
        this.Focusable = true;
        this.PointerPressed += this.OnPointerPressed;
        this.PointerMoved += this.OnPointerMoved;
        this.PointerReleased += this.OnPointerReleased;
    }

    /// <summary>当前标注文档。</summary>
    public AnnotationDocument Document
    {
        get { return this.document; }
    }

    /// <summary>当前工具。</summary>
    public AnnotationTool ActiveTool { get; set; } = AnnotationTool.Pen;

    /// <summary>当前颜色。</summary>
    public Color ActiveColor
    {
        get { return this.activeColor; }
        set { this.activeColor = value; }
    }

    /// <summary>线条宽度（原图像素）。</summary>
    public double ActiveThickness
    {
        get { return this.activeThickness; }
        set { this.activeThickness = Math.Max(1.0, value); }
    }

    /// <summary>画笔 / 马赛克 / 模糊的笔刷宽度（原图像素）。</summary>
    public double BrushSize
    {
        get { return this.brushSize; }
        set { this.brushSize = Math.Max(2.0, value); }
    }

    /// <summary>是否填充矩形与椭圆。</summary>
    public bool FillShapes { get; set; }

    /// <summary>文字字号（原图像素）。</summary>
    public double TextFontSize { get; set; } = 24.0;

    /// <summary>是否处于取色模式：点击画布只取色，不绘制。</summary>
    public bool IsColorPicking
    {
        get { return this.isColorPicking; }
        set { this.isColorPicking = value; this.Cursor = value ? new Cursor(StandardCursorType.Cross) : null; }
    }

    /// <summary>原图位图。</summary>
    public Bitmap? Source
    {
        get { return this.source; }
    }

    /// <summary>原图像素尺寸。</summary>
    public PixelSize ImagePixelSize
    {
        get { return this.source == null ? new PixelSize(0, 0) : this.source.PixelSize; }
    }

    /// <summary>装载图片并重建取色采样器；旧图片与采样器会被释放。</summary>
    public void LoadImage(Bitmap image)
    {
        this.DisposeImage();
        this.source = image;
        this.sourceSampler = new PixelSampler(image);
        Bitmap blurred = AnnotationRenderer.CreateBlurredBitmap(image);
        this.blurSampler = new PixelSampler(blurred);
        blurred.Dispose();
        this.document.Clear();
        this.nextNumber = 1;
        this.InvalidateVisual();
    }

    /// <summary>追加一条文字标注（由编辑器在用户输入后调用）。</summary>
    public void AddText(Point imagePoint, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        AnnotationShape shape = new AnnotationShape();
        shape.Tool = AnnotationTool.Text;
        shape.Start = imagePoint;
        shape.Text = text;
        shape.FontSize = Math.Max(10.0, this.TextFontSize);
        shape.Color = this.activeColor;
        this.document.Add(shape);
        this.InvalidateVisual();
        this.DocumentChanged?.Invoke();
    }

    /// <summary>撤销一步。</summary>
    public bool Undo()
    {
        bool changed = this.document.Undo();
        if (changed)
        {
            this.InvalidateVisual();
            this.DocumentChanged?.Invoke();
        }
        return changed;
    }

    /// <summary>重做一步。</summary>
    public bool Redo()
    {
        bool changed = this.document.Redo();
        if (changed)
        {
            this.InvalidateVisual();
            this.DocumentChanged?.Invoke();
        }
        return changed;
    }

    /// <summary>清空全部标注。</summary>
    public void ClearShapes()
    {
        this.document.Clear();
        this.nextNumber = 1;
        this.InvalidateVisual();
        this.DocumentChanged?.Invoke();
    }

    /// <summary>按原图像素尺寸渲染并返回合成图（用于保存、复制与贴图）。</summary>
    public RenderTargetBitmap Flatten()
    {
        PixelSize size = this.ImagePixelSize;
        int width = Math.Max(1, size.Width);
        int height = Math.Max(1, size.Height);
        RenderTargetBitmap target = new RenderTargetBitmap(new PixelSize(width, height));
        using (DrawingContext context = target.CreateDrawingContext(true))
        {
            if (this.source != null)
            {
                context.DrawImage(this.source, new Rect(0, 0, width, height));
            }
            AnnotationRenderer.DrawAll(context, this.document.Shapes, null,
                this.sourceSampler!, this.blurSampler);
        }
        return target;
    }

    /// <summary>在指定图像坐标处取色，返回十六进制颜色文本。</summary>
    public string SampleHex(Point imagePoint)
    {
        if (this.sourceSampler == null)
        {
            return "#000000";
        }
        return this.sourceSampler.GetHex(imagePoint.X, imagePoint.Y, true);
    }

    /// <summary>读取指定图像坐标处的颜色。</summary>
    public Color SampleColor(Point imagePoint)
    {
        if (this.sourceSampler == null)
        {
            return Colors.Black;
        }
        return this.sourceSampler.GetPixel(imagePoint.X, imagePoint.Y);
    }

    /// <summary>把视图坐标换算为原图像素坐标。</summary>
    public Point ViewToImage(Point viewPoint)
    {
        if (this.imageDisplayScale <= 0)
        {
            return viewPoint;
        }
        return new Point(
            (viewPoint.X - this.imageOffsetX) / this.imageDisplayScale,
            (viewPoint.Y - this.imageOffsetY) / this.imageDisplayScale);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double viewWidth = this.Bounds.Width;
        double viewHeight = this.Bounds.Height;
        if (this.source == null || viewWidth <= 0 || viewHeight <= 0)
        {
            return;
        }
        PixelSize size = this.source.PixelSize;
        double scale = Math.Min(viewWidth / size.Width, viewHeight / size.Height);
        this.imageDisplayScale = scale;
        this.imageOffsetX = (viewWidth - size.Width * scale) * 0.5;
        this.imageOffsetY = (viewHeight - size.Height * scale) * 0.5;
        using (context.PushTransform(Matrix.CreateTranslation(this.imageOffsetX, this.imageOffsetY)))
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawImage(this.source, new Rect(0, 0, size.Width, size.Height));
            AnnotationRenderer.DrawAll(context, this.document.Shapes, this.activeShape,
                this.sourceSampler!, this.blurSampler);
        }
    }

    /// <summary>指针按下：取色、橡皮擦、文字、序号或开始拖动形状。</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (this.source == null)
        {
            return;
        }
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }
        Point imagePoint = this.ViewToImage(e.GetPosition(this));
        this.Focus();
        if (this.isColorPicking)
        {
            this.RaiseColorSampled(imagePoint);
            e.Handled = true;
            return;
        }
        if (this.ActiveTool == AnnotationTool.None)
        {
            return;
        }
        if (this.ActiveTool == AnnotationTool.Eraser)
        {
            this.EraseAt(imagePoint);
            e.Handled = true;
            return;
        }
        if (this.ActiveTool == AnnotationTool.Text)
        {
            this.TextRequested?.Invoke(imagePoint);
            e.Handled = true;
            return;
        }
        if (this.ActiveTool == AnnotationTool.Number)
        {
            AnnotationShape numberShape = new AnnotationShape();
            numberShape.Tool = AnnotationTool.Number;
            numberShape.Start = imagePoint;
            numberShape.FontSize = Math.Max(14.0, this.TextFontSize);
            numberShape.Color = this.activeColor;
            numberShape.Number = this.nextNumber;
            this.nextNumber++;
            this.document.Add(numberShape);
            this.InvalidateVisual();
            this.DocumentChanged?.Invoke();
            e.Handled = true;
            return;
        }
        this.dragStartImagePoint = imagePoint;
        this.isDragging = true;
        this.activeShape = this.CreateActiveShape(imagePoint);
        e.Pointer.Capture(this);
        this.InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>指针移动：更新拖动中的形状或取色预览。</summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (this.source == null)
        {
            return;
        }
        Point imagePoint = this.ViewToImage(e.GetPosition(this));
        this.ImagePointerMoved?.Invoke(imagePoint);
        if (this.isColorPicking && !this.isDragging)
        {
            return;
        }
        if (!this.isDragging || this.activeShape == null)
        {
            return;
        }
        this.activeShape.End = imagePoint;
        if (this.activeShape.Tool == AnnotationTool.Pen
            || this.activeShape.Tool == AnnotationTool.Highlight
            || this.activeShape.Tool == AnnotationTool.Mosaic
            || this.activeShape.Tool == AnnotationTool.Blur)
        {
            Point last = this.activeShape.Points.Count == 0
                ? this.dragStartImagePoint
                : this.activeShape.Points[this.activeShape.Points.Count - 1];
            double dx = imagePoint.X - last.X;
            double dy = imagePoint.Y - last.Y;
            if (dx * dx + dy * dy >= 4.0)
            {
                this.activeShape.Points.Add(imagePoint);
            }
        }
        this.InvalidateVisual();
    }

    /// <summary>指针松开：提交形状。</summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!this.isDragging || this.activeShape == null)
        {
            return;
        }
        this.isDragging = false;
        e.Pointer.Capture(null);
        AnnotationShape shape = this.activeShape;
        this.activeShape = null;
        bool valid = this.IsShapeValid(shape);
        if (valid)
        {
            this.document.Add(shape);
            this.DocumentChanged?.Invoke();
        }
        this.InvalidateVisual();
    }

    /// <summary>创建拖动中的形状。</summary>
    private AnnotationShape CreateActiveShape(Point start)
    {
        AnnotationShape shape = new AnnotationShape();
        shape.Tool = this.ActiveTool;
        shape.Start = start;
        shape.End = start;
        shape.Color = this.activeColor;
        shape.Thickness = this.ActiveTool == AnnotationTool.Pen || this.ActiveTool == AnnotationTool.Arrow
            ? this.activeThickness
            : this.brushSize;
        shape.Filled = this.FillShapes && (this.ActiveTool == AnnotationTool.Rectangle
            || this.ActiveTool == AnnotationTool.Ellipse);
        shape.BlockSize = Math.Max(4.0, this.brushSize);
        shape.FontSize = Math.Max(14.0, this.TextFontSize);
        if (this.ActiveTool == AnnotationTool.Pen
            || this.ActiveTool == AnnotationTool.Highlight
            || this.ActiveTool == AnnotationTool.Mosaic
            || this.ActiveTool == AnnotationTool.Blur)
        {
            shape.Points.Add(start);
        }
        return shape;
    }

    /// <summary>判断形状是否达到最小尺寸，过小视为单击误触。</summary>
    private bool IsShapeValid(AnnotationShape shape)
    {
        if (shape.Tool == AnnotationTool.Number || shape.Tool == AnnotationTool.Text)
        {
            return true;
        }
        if (shape.Points.Count >= 2)
        {
            return true;
        }
        double dx = shape.End.X - shape.Start.X;
        double dy = shape.End.Y - shape.Start.Y;
        return dx * dx + dy * dy >= 16.0;
    }

    /// <summary>橡皮擦：删除命中点所在的最上层形状。</summary>
    private void EraseAt(Point imagePoint)
    {
        IReadOnlyList<AnnotationShape> shapes = this.document.Shapes;
        for (int index = shapes.Count - 1; index >= 0; index--)
        {
            Rect extent = shapes[index].GetExtent();
            double padding = 6.0;
            Rect expanded = new Rect(extent.X - padding, extent.Y - padding,
                extent.Width + padding * 2.0, extent.Height + padding * 2.0);
            if (expanded.Contains(imagePoint))
            {
                if (this.document.RemoveAt(index))
                {
                    this.InvalidateVisual();
                    this.DocumentChanged?.Invoke();
                }
                return;
            }
        }
    }

    /// <summary>触发取色事件。</summary>
    private void RaiseColorSampled(Point imagePoint)
    {
        if (this.sourceSampler == null)
        {
            return;
        }
        Color color = this.sourceSampler.GetPixel(imagePoint.X, imagePoint.Y);
        string hex = this.sourceSampler.GetHex(imagePoint.X, imagePoint.Y, true);
        this.ColorSampled?.Invoke(hex, color, imagePoint);
    }

    /// <summary>释放图片与采样器。</summary>
    public void DisposeImage()
    {
        if (this.sourceSampler != null)
        {
            this.sourceSampler.Dispose();
            this.sourceSampler = null;
        }
        if (this.blurSampler != null)
        {
            this.blurSampler.Dispose();
            this.blurSampler = null;
        }
        if (this.source != null)
        {
            this.source.Dispose();
            this.source = null;
        }
    }
}
