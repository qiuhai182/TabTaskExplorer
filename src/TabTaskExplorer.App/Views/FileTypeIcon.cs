using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TabTaskExplorer.App.Services;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 文件类型图标：在 24×24 设计坐标内绘制文件夹、磁盘分区与各类文件图标。
/// 小尺寸使用实色紧凑造型，大尺寸使用白色纸面 + 分类符号的精细造型，缩放时保持锐利。
/// </summary>
public class FileTypeIcon : Control
{
    /// <summary>图标分类键，取值见 <see cref="FileIconCatalog"/>。</summary>
    public static readonly StyledProperty<string> CategoryProperty =
        AvaloniaProperty.Register<FileTypeIcon, string>(nameof(Category), FileIconCatalog.File);

    /// <summary>强调色（十六进制）；留空时使用分类默认色。</summary>
    public static readonly StyledProperty<string> AccentColorProperty =
        AvaloniaProperty.Register<FileTypeIcon, string>(nameof(AccentColor), string.Empty);

    private static readonly Dictionary<string, IBrush> BrushCache = new Dictionary<string, IBrush>();

    private static readonly Dictionary<string, Geometry> SymbolCache = new Dictionary<string, Geometry>();

    private static readonly Color PaperColor = Color.Parse("#FBFCFE");

    private static readonly Color PaperBorderColor = Color.Parse("#330E1721");

    static FileTypeIcon()
    {
        AffectsRender<FileTypeIcon>(CategoryProperty, AccentColorProperty);
    }

    /// <summary>图标分类键。</summary>
    public string Category
    {
        get { return this.GetValue(CategoryProperty); }
        set { this.SetValue(CategoryProperty, value); }
    }

    /// <summary>强调色。</summary>
    public string AccentColor
    {
        get { return this.GetValue(AccentColorProperty); }
        set { this.SetValue(AccentColorProperty, value); }
    }

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
        string category = this.Category;
        if (string.IsNullOrEmpty(category))
        {
            category = FileIconCatalog.File;
        }
        Color accent = ParseAccent(this.AccentColor, category);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            if (string.Equals(category, FileIconCatalog.Folder, StringComparison.Ordinal))
            {
                DrawFolder(context, accent);
            }
            else if (string.Equals(category, FileIconCatalog.Drive, StringComparison.Ordinal))
            {
                DrawDrive(context, accent);
            }
            else if (side < 20)
            {
                DrawCompactFile(context, accent, category);
            }
            else
            {
                DrawDetailedFile(context, accent, category);
            }
        }
    }

    /// <summary>文件夹后板路径（含页签）。</summary>
    private const string FolderBackPath =
        "M2.6 19.6 L2.6 6.4 C2.6 5.5 3.5 4.6 4.6 4.6 L9.6 4.6 L12 7 L21.2 7 L21.2 9.8 L2.6 9.8 Z";

    /// <summary>文件夹前板路径（底部圆角）。</summary>
    private const string FolderFrontPath =
        "M2.6 9.6 L21.2 9.6 L21.2 18.4 C21.2 19.6 20.2 20.6 19 20.6 L4.8 20.6 C3.6 20.6 2.6 19.6 2.6 18.4 Z";

    /// <summary>磁盘分区顶板路径（只圆上沿）。</summary>
    private const string DriveCapPath =
        "M5 6.4 L19 6.4 C21 6.4 22 8 22 10.6 L2 10.6 C2 8 3 6.4 5 6.4 Z";

    /// <summary>紧凑文件右上角折角。</summary>
    private const string CompactFoldPath =
        "M14.6 2.2 L19.8 7.3 L16.4 7.3 C15.4 7.3 14.6 6.5 14.6 5.5 Z";

    /// <summary>精细文件左侧书脊。</summary>
    private const string DetailedSpinePath =
        "M4.6 4.7 C4.6 3.3 5.9 2.2 7.3 2.2 L7.6 2.2 L7.6 21.8 L7.3 21.8 C5.9 21.8 4.6 20.7 4.6 19.3 Z";

    /// <summary>精细文件右上角折角。</summary>
    private const string DetailedFoldPath =
        "M14.8 2.2 L19.8 7.3 L16.6 7.3 C15.6 7.3 14.8 6.5 14.8 5.5 Z";

    /// <summary>绘制文件夹：深色后板 + 亮色前板 + 高光。</summary>
    private static void DrawFolder(DrawingContext context, Color accent)
    {
        Color dark = Mix(accent, Color.Parse("#6B3F12"), 0.34);
        context.DrawGeometry(GetSolidBrush(dark, 1), null, ParseShape(FolderBackPath));
        context.DrawGeometry(GetGradientBrush(Mix(accent, Colors.White, 0.32), accent), null, ParseShape(FolderFrontPath));
        context.DrawRectangle(GetSolidBrush(Colors.White, 0.34), null, new Rect(4.6, 11, 15.6, 1.2), 0.6, 0.6);
        context.DrawRectangle(GetSolidBrush(Colors.White, 0.16), null, new Rect(4.6, 13.4, 9.4, 1), 0.5, 0.5);
    }

    /// <summary>绘制磁盘分区：机身渐变 + 亮色顶板 + 插槽与指示灯。</summary>
    private static void DrawDrive(DrawingContext context, Color accent)
    {
        IBrush bodyBrush = GetGradientBrush(Mix(accent, Colors.White, 0.24), Mix(accent, Colors.Black, 0.18));
        context.DrawRectangle(bodyBrush, null, new Rect(2, 6.4, 20, 11.6), 3, 3);
        context.DrawGeometry(GetSolidBrush(Mix(accent, Colors.White, 0.46), 1), null, ParseShape(DriveCapPath));
        context.DrawRectangle(GetSolidBrush(Mix(accent, Colors.Black, 0.18), 0.8), null, new Rect(2, 10.6, 20, 0.6), 0, 0);
        context.DrawRectangle(GetSolidBrush(Colors.White, 0.72), null, new Rect(5, 13.4, 8.4, 1.8), 0.9, 0.9);
        context.DrawEllipse(GetSolidBrush(Colors.White, 0.92), null, new Point(18.4, 14.3), 1.2, 1.2);
    }

    /// <summary>小尺寸文件：整个纸面使用分类色，符号反白，辨识度最高。</summary>
    private static void DrawCompactFile(DrawingContext context, Color accent, string category)
    {
        IBrush bodyBrush = GetGradientBrush(Mix(accent, Colors.White, 0.3), Mix(accent, Colors.Black, 0.08));
        context.DrawRectangle(bodyBrush,
            new Pen(GetSolidBrush(PaperBorderColor, 1), 0.8, null, PenLineCap.Flat, PenLineJoin.Miter, 10),
            new Rect(4.4, 2.2, 15.4, 19.6), 2.6, 2.6);
        context.DrawGeometry(GetSolidBrush(Colors.White, 0.82), null, ParseShape(CompactFoldPath));

        string? symbol = GetSymbolPath(category);
        if (symbol != null)
        {
            Geometry geometry = ParseSymbol(symbol);
            Pen pen = new Pen(GetSolidBrush(Colors.White, 0.95), 1.6, null, PenLineCap.Round, PenLineJoin.Round, 10);
            context.DrawGeometry(null, pen, geometry);
        }
    }

    /// <summary>大尺寸文件：白色纸面 + 分类色书脊 + 分类符号。</summary>
    private static void DrawDetailedFile(DrawingContext context, Color accent, string category)
    {
        context.DrawRectangle(GetSolidBrush(PaperColor, 1),
            new Pen(GetSolidBrush(PaperBorderColor, 1), 0.9, null, PenLineCap.Flat, PenLineJoin.Miter, 10),
            new Rect(4.6, 2.2, 15.2, 19.6), 2.5, 2.5);
        context.DrawGeometry(GetGradientBrush(Mix(accent, Colors.White, 0.24), accent), null, ParseShape(DetailedSpinePath));
        context.DrawGeometry(GetSolidBrush(Mix(accent, Colors.White, 0.72), 1),
            new Pen(GetSolidBrush(PaperBorderColor, 1), 0.7, null, PenLineCap.Flat, PenLineJoin.Miter, 10),
            ParseShape(DetailedFoldPath));

        string? symbol = GetSymbolPath(category);
        if (symbol != null)
        {
            Geometry geometry = ParseSymbol(symbol);
            Pen pen = new Pen(GetSolidBrush(accent, 0.95), 1.5, null, PenLineCap.Round, PenLineJoin.Round, 10);
            context.DrawGeometry(null, pen, geometry);
        }
    }

    /// <summary>取分类符号路径；普通文件也给出简洁线条。</summary>
    private static string? GetSymbolPath(string category)
    {
        switch (category)
        {
            case FileIconCatalog.Code:
                return "M10 9.6 L7.6 13 L10 16.4 M17 9.6 L19.4 13 L17 16.4 M14.6 8.8 L12.4 17.2";
            case FileIconCatalog.Web:
                return "M8.4 13.7 A4.6 4.6 0 1 0 17.6 13.7 A4.6 4.6 0 1 0 8.4 13.7 M8.4 13.7 L17.6 13.7 M13 9 C10.6 11 10.6 16.4 13 18.4 C15.4 16.4 15.4 11 13 9";
            case FileIconCatalog.Text:
                return "M8.4 9.6 L18.4 9.6 M8.4 12.7 L18.4 12.7 M8.4 15.8 L18.4 15.8 M8.4 18.8 L15.4 18.8";
            case FileIconCatalog.Log:
                return "M8.4 9.4 L18.4 9.4 M8.4 12.4 L18.4 12.4 M8.4 15.4 L18.4 15.4 M8.4 18.4 L13.4 18.4";
            case FileIconCatalog.Document:
                return "M8.4 9.4 L18.4 9.4 M8.4 12.3 L18.4 12.3 M8.4 15.2 L18.4 15.2 M8.4 18.1 L14.2 18.1";
            case FileIconCatalog.Markdown:
                return "M8 16 L10.2 9.4 L13 14.2 L15.8 9.4 L18 16 M13.4 8.2 L13.4 5.2 M11.6 6.8 L13.4 5.2 L15.2 6.8";
            case FileIconCatalog.Pdf:
                return "M8.4 9.2 L18.6 9.2 M8.4 12.1 L18.6 12.1 M8.4 15 L14.6 15 M8.4 17.9 L18.6 17.9";
            case FileIconCatalog.Spreadsheet:
                return "M8.2 9 L18.8 9 L18.8 17.6 L8.2 17.6 Z M8.2 12.6 L18.8 12.6 M13.5 9 L13.5 17.6";
            case FileIconCatalog.Presentation:
                return "M8.2 17.2 L8.2 13 M12.2 17.2 L12.2 9 M16.2 17.2 L16.2 11.2 M7 18.4 L18.4 18.4";
            case FileIconCatalog.Image:
                return "M7.8 16.8 L11 12.6 L13.2 15.2 L15.4 13 L18.6 16.8 Z M15.6 9.8 A1.4 1.4 0 1 0 18.4 9.8 A1.4 1.4 0 1 0 15.6 9.8";
            case FileIconCatalog.Audio:
                return "M9 17.2 A3 3 0 1 0 15 17.2 A3 3 0 1 0 9 17.2 M12 17.2 L12 6.8 L18.8 5.4";
            case FileIconCatalog.Video:
                return "M10 8.6 L18.6 13 L10 17.4 Z";
            case FileIconCatalog.Archive:
                return "M8.2 8.8 L18.6 8.8 L18.6 17.2 L8.2 17.2 Z M13.4 9 L13.4 17 M11.6 10.4 L15.2 10.4 M11.6 12.4 L15.2 12.4 M11.6 14.4 L15.2 14.4 M11.6 16.4 L15.2 16.4";
            case FileIconCatalog.Font:
                return "M8.6 17.4 L13.4 7.6 L18.2 17.4 M10.2 14 L16.6 14";
            case FileIconCatalog.Database:
                return "M8.6 9.4 C8.6 8 10.8 7.2 13.4 7.2 C16 7.2 18.2 8 18.2 9.4 C18.2 10.8 16 11.6 13.4 11.6 C10.8 11.6 8.6 10.8 8.6 9.4 Z M8.6 9.4 L8.6 15.2 C8.6 16.6 10.8 17.4 13.4 17.4 C16 17.4 18.2 16.6 18.2 15.2 L18.2 9.4 M8.6 12.3 C8.6 13.7 10.8 14.5 13.4 14.5 C16 14.5 18.2 13.7 18.2 12.3";
            case FileIconCatalog.Config:
                return "M8.4 9.4 L18.4 9.4 M8.4 13 L18.4 13 M8.4 16.6 L18.4 16.6 M10.6 9.4 A1.6 1.6 0 1 0 13.8 9.4 A1.6 1.6 0 1 0 10.6 9.4 M13.8 13 A1.6 1.6 0 1 0 17 13 A1.6 1.6 0 1 0 13.8 13 M9 16.6 A1.6 1.6 0 1 0 12.2 16.6 A1.6 1.6 0 1 0 9 16.6";
            case FileIconCatalog.Executable:
                return "M8 8.4 L19 8.4 L19 17 L8 17 Z M8 11 L19 11 M10 9.7 L10 9.9 M12 9.7 L12 9.9";
            case FileIconCatalog.Script:
                return "M8.4 9.6 L11.6 13 L8.4 16.4 M12.6 16.8 L17.6 16.8";
            case FileIconCatalog.DiskImage:
                return "M8.6 13 A4.8 4.8 0 1 0 18.2 13 A4.8 4.8 0 1 0 8.6 13 M11.6 13 A1.8 1.8 0 1 0 15.2 13 A1.8 1.8 0 1 0 11.6 13";
            case FileIconCatalog.Subtitle:
                return "M7.6 8.6 L19.2 8.6 C20 8.6 20.6 9.2 20.6 10 L20.6 15 C20.6 15.8 20 16.4 19.2 16.4 L13 16.4 L9.4 19.2 L9.4 16.4 L7.6 16.4 C6.8 16.4 6.2 15.8 6.2 15 L6.2 10 C6.2 9.2 6.8 8.6 7.6 8.6 Z M9.4 11.4 L17.4 11.4 M9.4 13.8 L14.6 13.8";
            case FileIconCatalog.Torrent:
                return "M9 15.6 A4 4 0 1 0 17 15.6 A4 4 0 1 0 9 15.6 M13 16.4 L13 8.2 M10.2 11 L13 8.2 L15.8 11";
            case FileIconCatalog.Email:
                return "M7 9 L19.4 9 L19.4 17 L7 17 Z M7 9 L13.2 13.6 L19.4 9";
            case FileIconCatalog.Certificate:
                return "M8.8 10.6 A3.4 3.4 0 1 0 15.6 10.6 A3.4 3.4 0 1 0 8.8 10.6 M11 13.6 L9.6 18.2 L12.2 16.6 L14.8 18.2 L13.4 13.6";
            case FileIconCatalog.Design:
                return "M7.8 17.6 L8.8 12.6 L14.4 7 L17.2 9.8 L11.6 15.4 Z M8.8 12.6 L11.6 15.4 M14.4 7 L17.2 9.8";
            case FileIconCatalog.Binary:
                return "M8.6 8.6 L18.2 8.6 L18.2 17 L8.6 17 Z M11 8.6 L11 6.4 M15.8 8.6 L15.8 6.4 M11 17 L11 19.2 M15.8 17 L15.8 19.2 M8.6 11 L6.4 11 M8.6 14.8 L6.4 14.8 M18.2 11 L20.4 11 M18.2 14.8 L20.4 14.8 M11.6 11.4 L15.2 11.4 M11.6 14.6 L15.2 14.6";
            case FileIconCatalog.Shortcut:
                return "M7.8 16.6 L13.4 16.6 C15 16.6 16.4 15.2 16.4 13.6 L16.4 8.6 M13 5.4 L16.6 8.6 L20.2 5.4";
            case FileIconCatalog.Ebook:
                return "M7.4 8.6 L11.4 8.6 C12.2 8.6 13 9.2 13.4 10 L13.4 17 C12.9 16.2 12.2 15.8 11.4 15.8 L7.4 15.8 Z M13.4 10 C13.9 9.2 14.6 8.8 15.4 8.8 L19.4 8.8 L19.4 15.8 L15.4 15.8 C14.6 15.8 13.9 16.2 13.4 17 Z";
            case FileIconCatalog.Package:
                return "M12.6 5.8 L19 9.2 L19 15.4 L12.6 18.8 L6.2 15.4 L6.2 9.2 Z M6.2 9.2 L12.6 12.6 L19 9.2 M12.6 12.6 L12.6 18.8";
            default:
                return "M8.4 10 L18.4 10 M8.4 13 L18.4 13 M8.4 16 L15.4 16";
        }
    }

    /// <summary>解析并缓存分类符号图形。</summary>
    private static Geometry ParseSymbol(string path)
    {
        return ParseShape(path);
    }

    /// <summary>解析并缓存任意路径图形（文件夹、折角、书脊与符号共用）。</summary>
    private static Geometry ParseShape(string path)
    {
        if (SymbolCache.TryGetValue(path, out Geometry cached))
        {
            return cached;
        }
        Geometry geometry = StreamGeometry.Parse(path);
        SymbolCache[path] = geometry;
        return geometry;
    }

    /// <summary>解析强调色；无效值时回退到分类默认色。</summary>
    private static Color ParseAccent(string accent, string category)
    {
        Color color;
        if (!string.IsNullOrEmpty(accent) && Color.TryParse(accent, out color))
        {
            return color;
        }
        Color fallback;
        if (Color.TryParse(FileIconCatalog.GetAccent(category), out fallback))
        {
            return fallback;
        }
        return Color.Parse("#8A94A6");
    }

    /// <summary>混合两种颜色；amount 为第二种颜色的占比（0 到 1）。</summary>
    private static Color Mix(Color first, Color second, double amount)
    {
        double clamped = amount < 0 ? 0 : (amount > 1 ? 1 : amount);
        byte red = (byte)Math.Round(first.R * (1 - clamped) + second.R * clamped);
        byte green = (byte)Math.Round(first.G * (1 - clamped) + second.G * clamped);
        byte blue = (byte)Math.Round(first.B * (1 - clamped) + second.B * clamped);
        byte alpha = (byte)Math.Round(first.A * (1 - clamped) + second.A * clamped);
        return Color.FromArgb(alpha, red, green, blue);
    }

    /// <summary>取纯色画刷，按颜色与透明度缓存。</summary>
    private static IBrush GetSolidBrush(Color color, double opacity)
    {
        int alpha = (int)Math.Round(color.A * opacity);
        if (alpha < 0)
        {
            alpha = 0;
        }
        if (alpha > 255)
        {
            alpha = 255;
        }
        string key = "s" + color.R + "_" + color.G + "_" + color.B + "_" + alpha;
        IBrush? cached;
        if (BrushCache.TryGetValue(key, out cached))
        {
            return cached;
        }
        IBrush brush = new SolidColorBrush(Color.FromArgb((byte)alpha, color.R, color.G, color.B));
        BrushCache[key] = brush;
        return brush;
    }

    /// <summary>取自上而下的线性渐变画刷，按起止颜色缓存。</summary>
    private static IBrush GetGradientBrush(Color top, Color bottom)
    {
        string key = "g" + top.R + "_" + top.G + "_" + top.B + "_" + bottom.R + "_" + bottom.G + "_" + bottom.B;
        IBrush? cached;
        if (BrushCache.TryGetValue(key, out cached))
        {
            return cached;
        }
        LinearGradientBrush gradient = new LinearGradientBrush();
        gradient.StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        gradient.EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative);
        gradient.GradientStops.Add(new GradientStop(top, 0));
        gradient.GradientStops.Add(new GradientStop(bottom, 1));
        BrushCache[key] = gradient;
        return gradient;
    }
}
