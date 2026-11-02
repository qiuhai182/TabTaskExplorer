using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TabTaskExplorer.App.Services.Capture;

/// <summary>
/// 取色放大镜：显示光标附近的像素放大图（最近邻放大，保证每个像素清晰可见），
/// 供截图编辑器取色时精确判断颜色位置。
/// </summary>
public class ColorMagnifierControl : Control
{
    private Bitmap? source;

    private Point imagePoint;

    private double zoom = 8.0;

    private int sampleSize = 13;

    /// <summary>设置放大源图。</summary>
    public void SetSource(Bitmap? image)
    {
        this.source = image;
        this.InvalidateVisual();
    }

    /// <summary>设置光标在原图中的像素坐标。</summary>
    public void SetImagePoint(Point point)
    {
        this.imagePoint = point;
        this.InvalidateVisual();
    }

    /// <summary>设置放大倍数。</summary>
    public void SetZoom(double value)
    {
        this.zoom = Math.Max(1.0, value);
        this.InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = this.Bounds.Width;
        double height = this.Bounds.Height;
        if (this.source == null || width <= 0 || height <= 0)
        {
            return;
        }
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        double half = this.sampleSize * 0.5;
        Rect sourceRect = new Rect(
            this.imagePoint.X - half, this.imagePoint.Y - half,
            this.sampleSize, this.sampleSize);
        context.DrawImage(this.source, new Rect(0, 0, width, height), sourceRect);
        IPen gridPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 0.6);
        double cell = width / this.sampleSize;
        for (int index = 1; index < this.sampleSize; index++)
        {
            context.DrawLine(gridPen, new Point(cell * index, 0), new Point(cell * index, height));
            context.DrawLine(gridPen, new Point(0, cell * index), new Point(width, cell * index));
        }
        IPen centerPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 59, 48)), 1.4);
        context.DrawRectangle(null, centerPen, new Rect(width * 0.5 - cell * 0.5, height * 0.5 - cell * 0.5, cell, cell));
    }
}
