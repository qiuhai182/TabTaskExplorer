using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 文件缩略图显示控件：命中缓存或异步生成成功时显示真实缩略图，
/// 未命中、目录、远程文件或生成失败时回退到原有的类型图标，保证列表滚动时布局不跳动。
/// </summary>
public class ThumbnailView : Panel
{
    /// <summary>图标分类键，取值见 <see cref="FileIconCatalog"/>。</summary>
    public static readonly StyledProperty<string> CategoryProperty =
        AvaloniaProperty.Register<ThumbnailView, string>(nameof(Category), FileIconCatalog.File);

    /// <summary>强调色（十六进制）；留空时使用分类默认色。</summary>
    public static readonly StyledProperty<string> AccentColorProperty =
        AvaloniaProperty.Register<ThumbnailView, string>(nameof(AccentColor), string.Empty);

    private readonly Image image;

    private readonly FileTypeIcon icon;

    private CancellationTokenSource? loadCancellation;

    private Bitmap? currentBitmap;

    private string currentPath = string.Empty;

    private int generation;

    private int requestedTier;

    private double measuredSize;

    public ThumbnailView()
    {
        this.image = new Image();
        this.image.Stretch = Avalonia.Media.Stretch.Uniform;
        this.image.IsVisible = false;
        this.icon = new FileTypeIcon();
        this.Children.Add(this.image);
        this.Children.Add(this.icon);
        this.DataContextChanged += delegate
        {
            this.RequestThumbnail();
        };
        this.AttachedToVisualTree += delegate
        {
            this.RequestThumbnail();
        };
        this.DetachedFromVisualTree += delegate
        {
            this.Release();
        };
        this.LayoutUpdated += delegate
        {
            this.CheckSize();
        };
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
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CategoryProperty)
        {
            this.icon.Category = this.Category;
        }
        else if (change.Property == AccentColorProperty)
        {
            this.icon.AccentColor = this.AccentColor;
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsNaN(this.Width) || this.Width <= 0 ? 16.0 : this.Width;
        double height = double.IsNaN(this.Height) || this.Height <= 0 ? 16.0 : this.Height;
        Size size = new Size(width, height);
        this.image.Measure(size);
        this.icon.Measure(size);
        return size;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Rect rect = new Rect(0, 0, finalSize.Width, finalSize.Height);
        this.image.Arrange(rect);
        this.icon.Arrange(rect);
        return finalSize;
    }

    private void CheckSize()
    {
        double width = this.Bounds.Width > 0 ? this.Bounds.Width : this.Width;
        double height = this.Bounds.Height > 0 ? this.Bounds.Height : this.Height;
        double side = Math.Max(width, height);
        if (side <= 0)
        {
            return;
        }
        if (Math.Abs(side - this.measuredSize) < 0.5)
        {
            return;
        }
        this.measuredSize = side;
        int tier = ThumbnailService.ResolveTier(side);
        if (tier != this.requestedTier)
        {
            this.RequestThumbnail();
        }
    }

    private void RequestThumbnail()
    {
        FileRow? row = this.DataContext as FileRow;
        this.loadCancellation?.Cancel();
        this.loadCancellation = null;
        if (row == null || row.IsDirectory || string.IsNullOrEmpty(row.FullPath))
        {
            this.ShowIcon();
            return;
        }
        double side = this.measuredSize;
        if (side <= 0)
        {
            side = this.Bounds.Width > 0 ? this.Bounds.Width : 96.0;
        }
        int tier = ThumbnailService.ResolveTier(side);
        this.requestedTier = tier;
        if (string.Equals(this.currentPath, row.FullPath, StringComparison.Ordinal) && this.currentBitmap != null)
        {
            return;
        }
        string? cached;
        if (ThumbnailService.TryGetCachedPath(row.FullPath, side, out cached))
        {
            this.ApplyCached(row.FullPath, cached);
            return;
        }
        this.ShowIcon();
        this.currentPath = row.FullPath;
        CancellationTokenSource source = new CancellationTokenSource();
        this.loadCancellation = source;
        int requestGeneration = ++this.generation;
        string path = row.FullPath;
        _ = this.LoadAsync(path, side, requestGeneration, source.Token);
    }

    private async Task LoadAsync(string path, double side, int requestGeneration, CancellationToken cancellationToken)
    {
        string? thumbnailPath = await ThumbnailService.GetThumbnailPathAsync(path, side, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        Dispatcher.UIThread.Post(delegate
        {
            if (requestGeneration != this.generation || cancellationToken.IsCancellationRequested)
            {
                return;
            }
            if (string.IsNullOrEmpty(thumbnailPath))
            {
                this.ShowIcon();
                return;
            }
            this.ApplyCached(path, thumbnailPath);
        });
    }

    private void ApplyCached(string path, string thumbnailPath)
    {
        try
        {
            using FileStream stream = File.OpenRead(thumbnailPath);
            Bitmap bitmap = new Bitmap(stream);
            this.image.Source = bitmap;
            this.currentBitmap?.Dispose();
            this.currentBitmap = bitmap;
            this.currentPath = path;
            this.image.IsVisible = true;
            this.icon.IsVisible = false;
        }
        catch (Exception error)
        {
            UiLogger.Log("缩略图", "读取缓存失败：文件=" + path + " 缓存=" + thumbnailPath + " 原因=" + error.Message);
            this.ShowIcon();
        }
    }

    private void ShowIcon()
    {
        this.image.IsVisible = false;
        this.image.Source = null;
        this.currentBitmap?.Dispose();
        this.currentBitmap = null;
        this.currentPath = string.Empty;
        this.icon.IsVisible = true;
    }

    private void Release()
    {
        this.loadCancellation?.Cancel();
        this.loadCancellation = null;
        this.ShowIcon();
    }
}
