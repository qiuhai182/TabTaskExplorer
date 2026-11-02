using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 矢量界面图标：按图标键绘制 24×24 设计稿的线条图形，随控件尺寸等比缩放。
/// 图形全部内置于代码中，不依赖 Segoe MDL2 等系统字体，Windows / Linux / macOS 表现一致。
/// </summary>
public class VectorIcon : Control
{
    /// <summary>图标键。</summary>
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<VectorIcon, string>(nameof(Kind), "file");

    /// <summary>线条颜色。</summary>
    public static readonly StyledProperty<IBrush?> IconBrushProperty =
        AvaloniaProperty.Register<VectorIcon, IBrush?>(nameof(IconBrush));

    /// <summary>线条宽度（24×24 设计坐标）。</summary>
    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<VectorIcon, double>(nameof(Thickness), 1.7);

    /// <summary>是否用实心填充绘制（播放、暂停等按钮使用）。</summary>
    public static readonly StyledProperty<bool> FilledProperty =
        AvaloniaProperty.Register<VectorIcon, bool>(nameof(Filled), false);

    private static readonly Dictionary<string, Geometry> GeometryCache = new Dictionary<string, Geometry>();

    static VectorIcon()
    {
        AffectsRender<VectorIcon>(KindProperty, IconBrushProperty, ThicknessProperty, FilledProperty);
    }

    /// <summary>图标键，取值见 <see cref="VectorIconPaths"/> 中的路径表。</summary>
    public string Kind
    {
        get { return this.GetValue(KindProperty); }
        set { this.SetValue(KindProperty, value); }
    }

    /// <summary>线条颜色。</summary>
    public IBrush? IconBrush
    {
        get { return this.GetValue(IconBrushProperty); }
        set { this.SetValue(IconBrushProperty, value); }
    }

    /// <summary>线条宽度。</summary>
    public double Thickness
    {
        get { return this.GetValue(ThicknessProperty); }
        set { this.SetValue(ThicknessProperty, value); }
    }

    /// <summary>是否实心填充。</summary>
    public bool Filled
    {
        get { return this.GetValue(FilledProperty); }
        set { this.SetValue(FilledProperty, value); }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Geometry? geometry = GetGeometry(this.Kind);
        if (geometry == null)
        {
            return;
        }
        double width = this.Bounds.Width;
        double height = this.Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }
        IBrush brush = this.IconBrush ?? Brushes.Gray;
        double scale = Math.Min(width, height) / 24.0;
        // 小尺寸图标保证至少 1 像素的视觉线宽，避免 10~14 像素图标发虚
        double thickness = this.Thickness;
        double minimumThickness = 1.0 / scale;
        if (thickness < minimumThickness)
        {
            thickness = minimumThickness;
        }
        Pen pen = new Pen(brush, thickness, null, PenLineCap.Round, PenLineJoin.Round, 10);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            if (this.Filled)
            {
                context.DrawGeometry(brush, null, geometry);
            }
            else
            {
                context.DrawGeometry(null, pen, geometry);
            }
        }
    }

    /// <summary>按图标键取几何图形；首次使用时解析并缓存。</summary>
    private static Geometry? GetGeometry(string kind)
    {
        string? data = VectorIconPaths.GetPath(kind);
        if (string.IsNullOrEmpty(data))
        {
            return null;
        }
        if (GeometryCache.TryGetValue(kind, out Geometry cached))
        {
            return cached;
        }
        Geometry parsed = StreamGeometry.Parse(data);
        GeometryCache[kind] = parsed;
        return parsed;
    }
}
