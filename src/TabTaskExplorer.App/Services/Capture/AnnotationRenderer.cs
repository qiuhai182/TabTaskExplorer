using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TabTaskExplorer.App.Services.Capture;

/// <summary>
/// 像素采样器：把位图拷贝到可读的 BGRA 缓冲区，供马赛克、模糊、放大镜按坐标取色。
/// 只创建一次，渲染过程中重复使用，避免每帧重新解码图片。
/// </summary>
public class PixelSampler
{
    private readonly WriteableBitmap bitmap;

    private readonly byte[] pixels;

    private readonly int width;

    private readonly int height;

    private readonly int stride;

    /// <summary>从位图创建采样器。</summary>
    public PixelSampler(Bitmap source)
    {
        PixelSize size = source.PixelSize;
        this.width = size.Width;
        this.height = size.Height;
        this.bitmap = new WriteableBitmap(size, new Vector(96.0, 96.0),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (ILockedFramebuffer frame = this.bitmap.Lock())
        {
            source.CopyPixels(frame);
            this.stride = frame.RowBytes;
            int total = frame.RowBytes * size.Height;
            this.pixels = new byte[total];
            System.Runtime.InteropServices.Marshal.Copy(frame.Address, this.pixels, 0, total);
        }
    }

    /// <summary>图像宽度（像素）。</summary>
    public int Width
    {
        get { return this.width; }
    }

    /// <summary>图像高度（像素）。</summary>
    public int Height
    {
        get { return this.height; }
    }

    /// <summary>读取指定像素颜色；越界时返回透明黑。</summary>
    public Color GetPixel(double x, double y)
    {
        int pixelX = (int)Math.Round(x);
        int pixelY = (int)Math.Round(y);
        if (pixelX < 0 || pixelY < 0 || pixelX >= this.width || pixelY >= this.height)
        {
            return Color.FromArgb(0, 0, 0, 0);
        }
        int offset = pixelY * this.stride + pixelX * 4;
        byte blue = this.pixels[offset];
        byte green = this.pixels[offset + 1];
        byte red = this.pixels[offset + 2];
        byte alpha = this.pixels[offset + 3];
        return Color.FromArgb(alpha, red, green, blue);
    }

    /// <summary>读取指定像素的十六进制颜色（不含透明度），用于取色显示。</summary>
    public string GetHex(double x, double y, bool uppercase)
    {
        Color color = this.GetPixel(x, y);
        string hex = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        return uppercase ? hex.ToUpperInvariant() : hex.ToLowerInvariant();
    }

    /// <summary>导出连续排列的 BGRA 像素字节（去除行间填充），供条码识别使用。</summary>
    public byte[] ToPixelBytes()
    {
        byte[] bytes = new byte[this.width * this.height * 4];
        int rowLength = this.width * 4;
        for (int y = 0; y < this.height; y++)
        {
            Array.Copy(this.pixels, y * this.stride, bytes, y * rowLength, rowLength);
        }
        return bytes;
    }

    /// <summary>释放内部位图。</summary>
    public void Dispose()
    {
        this.bitmap.Dispose();
    }
}

/// <summary>
/// 标注渲染器：把形状列表绘制到目标上下文。坐标约定为原图像素，调用方需先设置好
/// 图像到画布的缩放与平移变换。马赛克、模糊与放大镜按块取色绘制，文字与序号使用 FormattedText。
/// </summary>
public static class AnnotationRenderer
{
    /// <summary>把一张位图按比例缩小（用于高斯模糊的降采样底图）。</summary>
    public static Bitmap Downscale(Bitmap source, double factor)
    {
        double widthValue = Math.Max(1.0, source.PixelSize.Width / factor);
        double heightValue = Math.Max(1.0, source.PixelSize.Height / factor);
        RenderTargetBitmap target = new RenderTargetBitmap(new PixelSize((int)widthValue, (int)heightValue));
        using (DrawingContext context = target.CreateDrawingContext(true))
        {
            context.DrawImage(source, new Rect(0, 0, widthValue, heightValue));
        }
        return target;
    }

    /// <summary>生成高斯模糊近似底图：先缩小到原图宽度的十二分之一，再高质量放大回原尺寸。</summary>
    public static Bitmap CreateBlurredBitmap(Bitmap source)
    {
        const double factor = 12.0;
        Bitmap small = Downscale(source, factor);
        double widthValue = Math.Max(1.0, source.PixelSize.Width);
        double heightValue = Math.Max(1.0, source.PixelSize.Height);
        RenderTargetBitmap target = new RenderTargetBitmap(new PixelSize((int)widthValue, (int)heightValue));
        using (DrawingContext context = target.CreateDrawingContext(true))
        {
            context.DrawImage(small, new Rect(0, 0, widthValue, heightValue));
        }
        small.Dispose();
        return target;
    }

    /// <summary>把全部已提交形状绘制到上下文；顺带绘制正在编辑的临时形状。</summary>
    public static void DrawAll(DrawingContext context, IReadOnlyList<AnnotationShape> shapes,
        AnnotationShape? activeShape, PixelSampler sourceSampler, PixelSampler? blurSampler)
    {
        foreach (AnnotationShape shape in shapes)
        {
            DrawShape(context, shape, sourceSampler, blurSampler);
        }
        if (activeShape != null)
        {
            DrawShape(context, activeShape, sourceSampler, blurSampler);
        }
    }

    /// <summary>绘制单个形状。</summary>
    public static void DrawShape(DrawingContext context, AnnotationShape shape,
        PixelSampler sourceSampler, PixelSampler? blurSampler)
    {
        if (shape == null)
        {
            return;
        }
        switch (shape.Tool)
        {
            case AnnotationTool.Rectangle:
                DrawRectangleShape(context, shape);
                break;
            case AnnotationTool.Ellipse:
                DrawEllipseShape(context, shape);
                break;
            case AnnotationTool.Arrow:
                DrawArrowShape(context, shape);
                break;
            case AnnotationTool.Pen:
                DrawPenShape(context, shape);
                break;
            case AnnotationTool.Highlight:
                DrawHighlightShape(context, shape);
                break;
            case AnnotationTool.Mosaic:
                DrawPixelatedShape(context, shape, sourceSampler, Math.Max(4.0, shape.BlockSize));
                break;
            case AnnotationTool.Blur:
                DrawPixelatedShape(context, shape, blurSampler, Math.Max(3.0, shape.BlockSize * 0.5));
                break;
            case AnnotationTool.Text:
                DrawTextShape(context, shape);
                break;
            case AnnotationTool.Number:
                DrawNumberShape(context, shape);
                break;
            case AnnotationTool.Magnify:
                DrawMagnifyShape(context, shape, sourceSampler);
                break;
            case AnnotationTool.Eraser:
                break;
        }
    }

    /// <summary>绘制矩形（可选填充）。</summary>
    private static void DrawRectangleShape(DrawingContext context, AnnotationShape shape)
    {
        Rect rect = new Rect(shape.Start, shape.End);
        IBrush? fill = shape.Filled ? new SolidColorBrush(shape.Color) : null;
        IPen pen = new Pen(new SolidColorBrush(shape.Color), shape.Thickness,
            null, PenLineCap.Square, PenLineJoin.Miter);
        context.DrawRectangle(fill, pen, rect);
    }

    /// <summary>绘制椭圆（可选填充）。</summary>
    private static void DrawEllipseShape(DrawingContext context, AnnotationShape shape)
    {
        Rect rect = new Rect(shape.Start, shape.End);
        IBrush? fill = shape.Filled ? new SolidColorBrush(shape.Color) : null;
        IPen pen = new Pen(new SolidColorBrush(shape.Color), shape.Thickness,
            null, PenLineCap.Square, PenLineJoin.Miter);
        context.DrawEllipse(fill, pen, rect);
    }

    /// <summary>绘制箭头：直线加两条尾翼，角度由起止点方向决定。</summary>
    private static void DrawArrowShape(DrawingContext context, AnnotationShape shape)
    {
        Point start = shape.Start;
        Point end = shape.End;
        IPen pen = new Pen(new SolidColorBrush(shape.Color), shape.Thickness,
            null, PenLineCap.Round, PenLineJoin.Round);
        context.DrawLine(pen, start, end);
        double length = Math.Max(8.0, shape.Thickness * 3.0);
        double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        double wingAngle = Math.PI * 0.72;
        Point wing1 = new Point(
            end.X - Math.Cos(angle - wingAngle) * length,
            end.Y - Math.Sin(angle - wingAngle) * length);
        Point wing2 = new Point(
            end.X - Math.Cos(angle + wingAngle) * length,
            end.Y - Math.Sin(angle + wingAngle) * length);
        context.DrawLine(pen, end, wing1);
        context.DrawLine(pen, end, wing2);
    }

    /// <summary>绘制自由画笔：按采样点折线绘制。</summary>
    private static void DrawPenShape(DrawingContext context, AnnotationShape shape)
    {
        if (shape.Points.Count < 2)
        {
            return;
        }
        StreamGeometry geometry = BuildPolyline(shape.Points);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(shape.Color), shape.Thickness,
            null, PenLineCap.Round, PenLineJoin.Round), geometry);
    }

    /// <summary>绘制荧光笔：半透明粗折线。</summary>
    private static void DrawHighlightShape(DrawingContext context, AnnotationShape shape)
    {
        if (shape.Points.Count < 2)
        {
            return;
        }
        Color color = shape.Color;
        color = Color.FromArgb(110, color.R, color.G, color.B);
        StreamGeometry geometry = BuildPolyline(shape.Points);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(color), shape.Thickness,
            null, PenLineCap.Round, PenLineJoin.Round), geometry);
    }

    /// <summary>把采样点构建为折线几何。</summary>
    private static StreamGeometry BuildPolyline(List<Point> points)
    {
        StreamGeometry geometry = new StreamGeometry();
        using (StreamGeometryContext sink = geometry.Open())
        {
            sink.BeginFigure(points[0], false);
            for (int index = 1; index < points.Count; index++)
            {
                sink.LineTo(points[index], true);
            }
            sink.EndFigure(false);
        }
        return geometry;
    }

    /// <summary>按块取色绘制像素化区域：马赛克取原图颜色，模糊取模糊底图颜色。</summary>
    private static void DrawPixelatedShape(DrawingContext context, AnnotationShape shape,
        PixelSampler? sampler, double blockSize)
    {
        if (sampler == null || shape.Points.Count == 0)
        {
            return;
        }
        Rect extent = shape.GetExtent();
        int startColumn = (int)Math.Floor(extent.X / blockSize);
        int endColumn = (int)Math.Ceiling(extent.Right / blockSize);
        int startRow = (int)Math.Floor(extent.Y / blockSize);
        int endRow = (int)Math.Ceiling(extent.Bottom / blockSize);
        double radius = Math.Max(2.0, shape.Thickness * 0.5);
        for (int column = startColumn; column <= endColumn; column++)
        {
            for (int row = startRow; row <= endRow; row++)
            {
                double centerX = (column + 0.5) * blockSize;
                double centerY = (row + 0.5) * blockSize;
                double distance = DistanceToStroke(centerX, centerY, shape.Points);
                if (distance > radius)
                {
                    continue;
                }
                Color color = sampler.GetPixel(centerX, centerY);
                if (color.A == 0)
                {
                    continue;
                }
                context.DrawRectangle(new SolidColorBrush(color), null,
                    new Rect(column * blockSize, row * blockSize, blockSize, blockSize));
            }
        }
    }

    /// <summary>点到笔迹折线的最近距离。</summary>
    private static double DistanceToStroke(double x, double y, List<Point> points)
    {
        if (points.Count == 1)
        {
            return Math.Sqrt((x - points[0].X) * (x - points[0].X) + (y - points[0].Y) * (y - points[0].Y));
        }
        double best = double.MaxValue;
        for (int index = 1; index < points.Count; index++)
        {
            double distance = DistanceToSegment(x, y, points[index - 1], points[index]);
            if (distance < best)
            {
                best = distance;
            }
        }
        return best;
    }

    /// <summary>点到线段的距离。</summary>
    private static double DistanceToSegment(double x, double y, Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 0.0001)
        {
            return Math.Sqrt((x - a.X) * (x - a.X) + (y - a.Y) * (y - a.Y));
        }
        double t = ((x - a.X) * dx + (y - a.Y) * dy) / lengthSquared;
        if (t < 0)
        {
            t = 0;
        }
        else if (t > 1)
        {
            t = 1;
        }
        double closeX = a.X + t * dx;
        double closeY = a.Y + t * dy;
        return Math.Sqrt((x - closeX) * (x - closeX) + (y - closeY) * (y - closeY));
    }

    /// <summary>绘制文字。</summary>
    private static void DrawTextShape(DrawingContext context, AnnotationShape shape)
    {
        if (shape.Text.Length == 0)
        {
            return;
        }
        FormattedText text = new FormattedText(
            shape.Text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei"), FontStyle.Normal, FontWeight.Normal, FontStretch.Normal),
            shape.FontSize,
            new SolidColorBrush(shape.Color));
        FormattedText shadow = new FormattedText(
            shape.Text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei"), FontStyle.Normal, FontWeight.Normal, FontStretch.Normal),
            shape.FontSize,
            new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)));
        context.DrawText(shadow, new Point(shape.Start.X + 1.5, shape.Start.Y + 1.5));
        context.DrawText(text, shape.Start);
    }

    /// <summary>绘制序号：实心圆形加数字。</summary>
    private static void DrawNumberShape(DrawingContext context, AnnotationShape shape)
    {
        double radius = shape.FontSize * 0.75;
        Point center = shape.Start;
        IBrush fill = new SolidColorBrush(shape.Color);
        context.DrawEllipse(fill, null, center, radius, radius);
        FormattedText text = new FormattedText(
            shape.Number.ToString(),
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei"), FontStyle.Normal, FontWeight.Normal, FontStretch.Normal),
            shape.FontSize,
            Brushes.White);
        context.DrawText(text, new Point(center.X - text.Width * 0.5, center.Y - text.Height * 0.5));
    }

    /// <summary>绘制放大镜：圆形范围内按块放大显示点击位置附近的原图像素。</summary>
    private static void DrawMagnifyShape(DrawingContext context, AnnotationShape shape, PixelSampler? sampler)
    {
        if (sampler == null)
        {
            return;
        }
        double radius = Math.Max(16.0, shape.Thickness * 4.0);
        double zoom = 2.0;
        double blockSize = Math.Max(2.0, shape.FontSize * 0.16);
        double centerX = shape.Start.X;
        double centerY = shape.Start.Y;
        int startColumn = (int)Math.Floor((centerX - radius) / blockSize);
        int endColumn = (int)Math.Ceiling((centerX + radius) / blockSize);
        int startRow = (int)Math.Floor((centerY - radius) / blockSize);
        int endRow = (int)Math.Ceiling((centerY + radius) / blockSize);
        for (int column = startColumn; column <= endColumn; column++)
        {
            for (int row = startRow; row <= endRow; row++)
            {
                double blockCenterX = (column + 0.5) * blockSize;
                double blockCenterY = (row + 0.5) * blockSize;
                double distance = Math.Sqrt((blockCenterX - centerX) * (blockCenterX - centerX)
                    + (blockCenterY - centerY) * (blockCenterY - centerY));
                if (distance > radius)
                {
                    continue;
                }
                double sourceX = centerX + (blockCenterX - centerX) / zoom;
                double sourceY = centerY + (blockCenterY - centerY) / zoom;
                Color color = sampler.GetPixel(sourceX, sourceY);
                context.DrawRectangle(new SolidColorBrush(color), null,
                    new Rect(column * blockSize, row * blockSize, blockSize, blockSize));
            }
        }
        context.DrawEllipse(null, new Pen(new SolidColorBrush(shape.Color), 2.0),
            new Rect(centerX - radius, centerY - radius, radius * 2.0, radius * 2.0));
    }
}
