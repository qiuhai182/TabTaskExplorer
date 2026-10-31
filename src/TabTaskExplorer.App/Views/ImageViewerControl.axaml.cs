using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 图片查看控件：滚轮缩放（以光标为锚点）、拖拽平移、旋转、双击切换「适应窗口 / 100%」。
/// 缩放与平移全部作用于 RenderTransform，不触发重新解码，保证大图拖动时的流畅度。
/// </summary>
public partial class ImageViewerControl : UserControl
{
    /// <summary>当前图片。</summary>
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<ImageViewerControl, Bitmap?>(nameof(Source));

    /// <summary>最小缩放比例。</summary>
    private const double MinScale = 0.1;

    /// <summary>最大缩放比例。</summary>
    private const double MaxScale = 16.0;

    private double scale = 1.0;
    private double offsetX;
    private double offsetY;
    private double rotation;
    private bool fitMode = true;
    private bool dragging;
    private Point dragStartPoint;
    private double dragStartOffsetX;
    private double dragStartOffsetY;

    /// <summary>缩放变换（围绕图像中心）。</summary>
    private readonly ScaleTransform imageScale = new ScaleTransform(1.0, 1.0);

    /// <summary>旋转变换（围绕图像中心）。</summary>
    private readonly RotateTransform imageRotate = new RotateTransform(0);

    /// <summary>平移变换。</summary>
    private readonly TranslateTransform imageTranslate = new TranslateTransform(0, 0);

    /// <summary>缩放比例变化（参数为百分比，供状态栏显示）。</summary>
    public event EventHandler<double>? ZoomChanged;

    /// <summary>构造图片查看控件并挂接交互事件。</summary>
    public ImageViewerControl()
    {
        this.InitializeComponent();
        TransformGroup group = new TransformGroup();
        group.Children.Add(this.imageScale);
        group.Children.Add(this.imageRotate);
        group.Children.Add(this.imageTranslate);
        this.Picture.RenderTransform = group;
        this.Picture.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        this.Root.PointerWheelChanged += this.OnPointerWheelChanged;
        this.Root.PointerPressed += this.OnPointerPressed;
        this.Root.PointerMoved += this.OnPointerMoved;
        this.Root.PointerReleased += this.OnPointerReleased;
        this.Root.DoubleTapped += this.OnDoubleTapped;
        this.Root.SizeChanged += this.OnRootSizeChanged;
    }

    /// <summary>当前图片。</summary>
    public Bitmap? Source
    {
        get { return this.GetValue(SourceProperty); }
        set { this.SetValue(SourceProperty, value); }
    }

    /// <summary>当前缩放百分比。</summary>
    public double ZoomPercent
    {
        get { return this.scale * 100.0; }
    }

    /// <summary>适应窗口：整张图缩放到可见区域内并居中。</summary>
    public void FitToWindow()
    {
        this.fitMode = true;
        this.rotation = 0;
        this.offsetX = 0;
        this.offsetY = 0;
        Bitmap? bitmap = this.Source;
        if (bitmap == null || this.Root.Bounds.Width <= 0 || this.Root.Bounds.Height <= 0)
        {
            this.scale = 1.0;
            this.ApplyTransform();
            return;
        }
        double viewWidth = this.Root.Bounds.Width;
        double viewHeight = this.Root.Bounds.Height;
        double imageWidth = bitmap.PixelSize.Width;
        double imageHeight = bitmap.PixelSize.Height;
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            this.scale = 1.0;
            this.ApplyTransform();
            return;
        }
        double fit = Math.Min(viewWidth / imageWidth, viewHeight / imageHeight);
        this.scale = Clamp(fit, MinScale, MaxScale);
        this.ApplyTransform();
    }

    /// <summary>原始像素大小（100%）。</summary>
    public void ActualSize()
    {
        this.fitMode = false;
        this.scale = 1.0;
        this.offsetX = 0;
        this.offsetY = 0;
        this.ApplyTransform();
    }

    /// <summary>按倍数缩放（以视图中心为锚点）。</summary>
    public void ZoomBy(double factor)
    {
        this.fitMode = false;
        double target = Clamp(this.scale * factor, MinScale, MaxScale);
        double ratio = target / this.scale;
        this.offsetX *= ratio;
        this.offsetY *= ratio;
        this.scale = target;
        this.ApplyTransform();
    }

    /// <summary>旋转指定角度（默认顺时针 90 度）。</summary>
    public void Rotate(double degrees)
    {
        this.rotation = (this.rotation + degrees) % 360.0;
        this.ApplyTransform();
    }

    /// <summary>重置为适应窗口并清除旋转（切换图片时调用）。</summary>
    public void Reset()
    {
        this.rotation = 0;
        this.offsetX = 0;
        this.offsetY = 0;
        this.fitMode = true;
        this.FitToWindow();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            this.Picture.Source = this.Source;
            this.LoadingText.IsVisible = this.Source == null;
            this.Reset();
        }
    }

    /// <summary>视图尺寸变化：适应模式下重新计算缩放，避免窗口拉伸后图片溢出。</summary>
    private void OnRootSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (this.fitMode)
        {
            this.FitToWindow();
        }
    }

    /// <summary>滚轮缩放：以光标位置为锚点，保持光标下的图像点不动。</summary>
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (this.Source == null)
        {
            return;
        }
        this.fitMode = false;
        double factor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        double target = Clamp(this.scale * factor, MinScale, MaxScale);
        if (Math.Abs(target - this.scale) < 0.0001)
        {
            return;
        }
        Point point = e.GetPosition(this.Root);
        double centerX = this.Root.Bounds.Width / 2.0;
        double centerY = this.Root.Bounds.Height / 2.0;
        double ratio = target / this.scale;
        this.offsetX = point.X - centerX - ((point.X - centerX - this.offsetX) * ratio);
        this.offsetY = point.Y - centerY - ((point.Y - centerY - this.offsetY) * ratio);
        this.scale = target;
        this.ApplyTransform();
        e.Handled = true;
    }

    /// <summary>按下开始拖拽（仅在可拖动时记录起点）。</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (this.Source == null)
        {
            return;
        }
        PointerPoint point = e.GetCurrentPoint(this.Root);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }
        this.dragging = true;
        this.dragStartPoint = point.Position;
        this.dragStartOffsetX = this.offsetX;
        this.dragStartOffsetY = this.offsetY;
        e.Pointer.Capture(this.Root);
    }

    /// <summary>拖拽平移。</summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!this.dragging)
        {
            return;
        }
        Point point = e.GetPosition(this.Root);
        this.offsetX = this.dragStartOffsetX + (point.X - this.dragStartPoint.X);
        this.offsetY = this.dragStartOffsetY + (point.Y - this.dragStartPoint.Y);
        this.ApplyTransform();
    }

    /// <summary>结束拖拽。</summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!this.dragging)
        {
            return;
        }
        this.dragging = false;
        e.Pointer.Capture(null);
    }

    /// <summary>双击在「适应窗口」与「100%」之间切换。</summary>
    private void OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (this.Source == null)
        {
            return;
        }
        if (this.fitMode || this.scale < 0.999)
        {
            this.ActualSize();
        }
        else
        {
            this.FitToWindow();
        }
        e.Handled = true;
    }

    /// <summary>把当前缩放 / 旋转 / 平移写入渲染变换并通知百分比变化。</summary>
    private void ApplyTransform()
    {
        this.imageScale.ScaleX = this.scale;
        this.imageScale.ScaleY = this.scale;
        this.imageRotate.Angle = this.rotation;
        this.imageTranslate.X = this.offsetX;
        this.imageTranslate.Y = this.offsetY;
        EventHandler<double>? handler = this.ZoomChanged;
        if (handler != null)
        {
            handler(this, this.scale * 100.0);
        }
    }

    /// <summary>把数值限制在指定区间内。</summary>
    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }
        if (value > max)
        {
            return max;
        }
        return value;
    }
}
