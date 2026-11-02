using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 程序徽标：蓝青渐变圆角方块 + 白色立体文件夹 + 分类色书页线条。
/// 使用矢量绘制，标题栏、窗口图标与托盘图标共用同一造型，任意尺寸都保持清晰。
/// </summary>
public class LogoMark : Control
{
    private static readonly Color StartColor = Color.Parse("#2F7CF6");

    private static readonly Color EndColor = Color.Parse("#12C2E9");

    private static readonly Color FolderShadowColor = Color.Parse("#2B6FE0");

    private static readonly IBrush BackBrush = new SolidColorBrush(Color.Parse("#55FFFFFF"));

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = this.Bounds.Width;
        double height = this.Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }
        double side = Math.Min(width, height);
        double scale = side / 24.0;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            DrawBrandMark(context);
        }
    }

    /// <summary>在 24×24 设计坐标内绘制徽标本体。</summary>
    private static void DrawBrandMark(DrawingContext context)
    {
        LinearGradientBrush background = new LinearGradientBrush();
        background.StartPoint = new RelativePoint(0.1, 0, RelativeUnit.Relative);
        background.EndPoint = new RelativePoint(0.9, 1, RelativeUnit.Relative);
        background.GradientStops.Add(new GradientStop(StartColor, 0));
        background.GradientStops.Add(new GradientStop(EndColor, 1));
        context.DrawRectangle(background, null, new Rect(0, 0, 24, 24), 5.4, 5.4);

        // 顶部高光：让渐变方块在深色与浅色标题栏上都有层次
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#1AFFFFFF")), null, new Rect(1.2, 1.2, 21.6, 9.2), 4.6, 4.6);

        // 文件夹后板与页签
        context.DrawGeometry(BackBrush, null, StreamGeometry.Parse(
            "M4.2 18.8 L4.2 8.1 C4.2 7.3 4.8 6.7 5.6 6.7 L9.2 6.7 L10.8 8.4 L18.4 8.4 C19.2 8.4 19.8 9 19.8 9.8 L19.8 11.1 L4.2 11.1 Z"));
        context.DrawRectangle(new SolidColorBrush(Colors.White), null, new Rect(4.2, 11, 15.6, 8.6), 2.2, 2.2);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#F2FFFFFF")), null, new Rect(4.2, 11, 15.6, 1.2), 0.6, 0.6);

        // 前板上的两条"目录"线条，使用徽标主色，形成清晰的识别符号
        context.DrawRectangle(new SolidColorBrush(StartColor), null, new Rect(6.6, 13.5, 8, 1.4), 0.7, 0.7);
        context.DrawRectangle(new SolidColorBrush(FolderShadowColor), null, new Rect(6.6, 16, 5.2, 1.4), 0.7, 0.7);
    }
}
