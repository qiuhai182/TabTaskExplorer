using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.Services.Media;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 空格快速预览窗：图片、文本、音频/视频与文件信息预览。
/// 窗口不进入任务栏、不激活抢焦点；Esc 与再次按空格由文件列表的键盘处理负责关闭。
/// PDF 暂无跨平台渲染器，显示提示与文件信息，不伪造预览。
/// </summary>
public sealed class QuickLookWindow : Window
{
    private static QuickLookWindow? current;

    private readonly string filePath;

    private readonly Bitmap? image;

    private VlcPlaybackEngine? playback;

    private MediaSurfaceHost? surface;

    /// <summary>当前打开的预览窗；没有则为 null。</summary>
    public static bool IsOpen
    {
        get { return current != null; }
    }

    private QuickLookWindow(string path, Bitmap? bitmap)
    {
        this.filePath = path;
        this.image = bitmap;
        this.Title = "快速预览 - " + Path.GetFileName(path);
        this.Width = 760;
        this.Height = 580;
        this.MinWidth = 420;
        this.MinHeight = 320;
        this.ShowInTaskbar = false;
        this.ShowActivated = false;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Background = new SolidColorBrush(Color.Parse("#101418"));
        this.Content = this.BuildContent(path);
        if (this.surface != null && this.playback != null)
        {
            this.Opened += delegate
            {
                this.playback.AttachSurface(this.surface);
                this.playback.Open(this.filePath);
                this.playback.Play();
            };
        }
        this.Closed += delegate
        {
            if (current == this)
            {
                current = null;
            }
            this.playback?.Dispose();
            this.image?.Dispose();
        };
    }

    /// <summary>打开指定文件的快速预览；已有预览先关闭。</summary>
    public static void ShowFor(Window? owner, string path)
    {
        CloseCurrent();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }
        FileInfo info = new FileInfo(path);
        bool isImage = FilePreviewClassifier.IsImage(path);
        Bitmap? bitmap = null;
        if (isImage)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                bitmap = Bitmap.DecodeToWidth(stream, 1600, BitmapInterpolationMode.HighQuality);
            }
            catch (Exception error)
            {
                UiLogger.Log("快速预览", "图片解码失败：文件=" + path + " 原因=" + error.Message);
            }
        }
        QuickLookWindow window = new QuickLookWindow(path, bitmap);
        FilePreviewKind kind = FilePreviewClassifier.Detect(path);
        if (kind == FilePreviewKind.Audio || kind == FilePreviewKind.Video)
        {
            // 音频与视频在构造后追加播放引擎与渲染面
            window.AttachMedia(kind);
        }
        current = window;
        if (owner != null)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>关闭当前快速预览窗。</summary>
    public static void CloseCurrent()
    {
        QuickLookWindow? window = current;
        current = null;
        if (window != null)
        {
            try
            {
                window.Close();
            }
            catch (Exception)
            {
                // 关闭失败不影响后续预览
            }
        }
    }

    private void AttachMedia(FilePreviewKind kind)
    {
        Grid grid = (Grid)this.Content!;
        this.surface = new MediaSurfaceHost();
        this.surface.HorizontalAlignment = HorizontalAlignment.Stretch;
        this.surface.VerticalAlignment = VerticalAlignment.Stretch;
        grid.Children.Add(this.surface);
        Grid.SetRow(this.surface, 0);
        this.playback = new VlcPlaybackEngine();
        if (!this.playback.IsAvailable)
        {
            this.surface.IsVisible = false;
            TextBlock error = new TextBlock();
            error.Text = "音视频解码库不可用：" + VlcPlaybackEngine.InitializationError;
            error.Foreground = Brushes.Orange;
            error.Margin = new Thickness(16);
            error.TextWrapping = TextWrapping.Wrap;
            grid.Children.Add(error);
            Grid.SetRow(error, 0);
        }
    }

    private Control BuildContent(string path)
    {
        Grid root = new Grid();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        FilePreviewKind kind = FilePreviewClassifier.Detect(path);
        if (this.image != null && kind == FilePreviewKind.Image)
        {
            Image viewer = new Image();
            viewer.Source = this.image;
            viewer.Stretch = Stretch.Uniform;
            viewer.Margin = new Thickness(10);
            root.Children.Add(viewer);
            Grid.SetRow(viewer, 0);
        }
        else if (kind == FilePreviewKind.Text)
        {
            TextBox text = new TextBox();
            text.IsReadOnly = true;
            text.AcceptsReturn = true;
            text.TextWrapping = TextWrapping.NoWrap;
            text.FontFamily = new FontFamily("Cascadia Code, Consolas, monospace");
            text.FontSize = 13;
            text.Margin = new Thickness(10);
            text.Text = FilePreviewClassifier.ReadTextPreview(path);
            root.Children.Add(text);
            Grid.SetRow(text, 0);
        }
        else if (kind == FilePreviewKind.Pdf)
        {
            TextBlock hint = new TextBlock();
            hint.Text = FilePreviewClassifier.DescribeUnsupported(FilePreviewKind.Pdf);
            hint.Foreground = Brushes.Gainsboro;
            hint.Margin = new Thickness(20);
            hint.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(hint);
            Grid.SetRow(hint, 0);
        }
        else if (kind != FilePreviewKind.Audio && kind != FilePreviewKind.Video)
        {
            TextBlock hint = new TextBlock();
            hint.Text = FilePreviewClassifier.DescribeUnsupported(kind);
            hint.Foreground = Brushes.Gainsboro;
            hint.Margin = new Thickness(20);
            root.Children.Add(hint);
            Grid.SetRow(hint, 0);
        }

        Border footer = new Border();
        footer.Background = new SolidColorBrush(Color.Parse("#1A2026"));
        footer.Padding = new Thickness(12, 8);
        TextBlock info = new TextBlock();
        info.Foreground = Brushes.Gainsboro;
        info.TextTrimming = TextTrimming.CharacterEllipsis;
        FileInfo fileInfo = new FileInfo(path);
        info.Text = Path.GetFileName(path) + "    " + FormatSize(fileInfo.Length)
            + "    " + fileInfo.LastWriteTime.ToString("yyyy/MM/dd HH:mm")
            + "    Esc 或空格关闭";
        footer.Child = info;
        root.Children.Add(footer);
        Grid.SetRow(footer, 1);
        return root;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return index == 0 ? value.ToString("0") + " B" : value.ToString("0.##") + " " + units[index];
    }

}
