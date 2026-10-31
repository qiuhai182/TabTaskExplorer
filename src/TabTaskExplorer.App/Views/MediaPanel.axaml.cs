using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.App.ViewModels;

namespace TabTaskExplorer.App.Views;

/// <summary>
/// 媒体面板交互逻辑：控制条按钮、播放列表选中、键盘快捷键与全屏切换。
/// 所有播放状态都由视图模型持有，这里只做「界面动作 → 视图模型调用」的翻译。
/// </summary>
public partial class MediaPanel : UserControl
{
    /// <summary>正在把视图模型的当前项同步到列表控件（避免选中事件反向触发切换）。</summary>
    private bool syncingSelection;

    /// <summary>用户正在拖动进度条（拖动期间不接受引擎回写）。</summary>
    private bool seeking;

    /// <summary>视频渲染面宿主：仅视频标签才创建（NativeControlHost 挂载即建原生窗口）。</summary>
    private MediaSurfaceHost? surfaceHost;

    /// <summary>构造媒体面板并挂接生命周期事件。</summary>
    public MediaPanel()
    {
        this.InitializeComponent();
        this.AttachedToVisualTree += this.OnAttachedToVisualTree;
        this.DetachedFromVisualTree += this.OnDetachedFromVisualTree;
        this.KeyDown += this.OnPanelKeyDown;
        this.ImageViewer.ZoomChanged += this.OnImageViewerZoomChanged;
        this.Focusable = true;
    }

    /// <summary>当前媒体视图模型。</summary>
    private MediaTabViewModel? ViewModel
    {
        get { return this.DataContext as MediaTabViewModel; }
    }

    /// <summary>挂载到视觉树：订阅视图模型变化并把焦点交给面板，保证快捷键生效。</summary>
    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        MediaTabViewModel? viewModel = this.ViewModel;
        if (viewModel != null)
        {
            viewModel.PropertyChanged += this.OnViewModelPropertyChanged;
            viewModel.Playlist.PropertyChanged += this.OnPlaylistPropertyChanged;
            if (viewModel.IsVideo)
            {
                this.EnsureSurfaceHost();
            }
            this.SyncSelection(viewModel.Playlist.CurrentItem);
        }
        this.Focus();
    }

    /// <summary>离开视觉树：解除订阅，避免标签关闭后仍收到通知。</summary>
    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        MediaTabViewModel? viewModel = this.ViewModel;
        if (viewModel != null)
        {
            viewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
            viewModel.Playlist.PropertyChanged -= this.OnPlaylistPropertyChanged;
            viewModel.DetachSurface();
        }
    }

    /// <summary>视图模型属性变化：图片缩放百分比与播放列表当前项需要回写控件。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        MediaTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        if (e.PropertyName == nameof(MediaTabViewModel.ZoomPercent))
        {
            // 缩放百分比由控件回写，这里不再反向设置，避免抖动
            return;
        }
        if (e.PropertyName == nameof(MediaTabViewModel.IsImage) && viewModel.IsImage)
        {
            this.ImageViewer.Reset();
        }
        if (e.PropertyName == nameof(MediaTabViewModel.IsVideo) && viewModel.IsVideo)
        {
            this.EnsureSurfaceHost();
        }
    }

    /// <summary>播放列表当前项变化：同步列表选中态。</summary>
    private void OnPlaylistPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaPlaylistViewModel.CurrentItem))
        {
            MediaTabViewModel? viewModel = this.ViewModel;
            if (viewModel != null)
            {
                this.SyncSelection(viewModel.Playlist.CurrentItem);
            }
        }
    }

    /// <summary>
    /// 创建视频渲染面并挂到视图模型：只在视频标签里调用，
    /// 图片 / 音频标签保持不创建，避免无谓的原生子窗口。
    /// </summary>
    private void EnsureSurfaceHost()
    {
        if (this.surfaceHost != null)
        {
            return;
        }
        MediaSurfaceHost host = new MediaSurfaceHost();
        host.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        host.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        this.surfaceHost = host;
        this.SurfaceContainer.Children.Add(host);
        MediaTabViewModel? viewModel = this.ViewModel;
        if (viewModel != null)
        {
            viewModel.AttachSurface(host);
        }
    }

    /// <summary>把视图模型的当前项同步为列表选中项（不触发切换）。</summary>
    private void SyncSelection(MediaPlaylistItemViewModel? item)
    {
        this.syncingSelection = true;
        try
        {
            this.PlaylistList.SelectedItem = item;
            if (item != null)
            {
                this.PlaylistList.ScrollIntoView(item);
            }
        }
        finally
        {
            this.syncingSelection = false;
        }
    }

    /// <summary>播放列表点击：在当前标签内切换媒体。</summary>
    private void OnPlaylistSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.syncingSelection)
        {
            return;
        }
        MediaPlaylistItemViewModel? item = this.PlaylistList.SelectedItem as MediaPlaylistItemViewModel;
        MediaTabViewModel? viewModel = this.ViewModel;
        if (item == null || viewModel == null)
        {
            return;
        }
        if (string.Equals(item.FullPath, viewModel.CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            // 由程序同步选中态引起的选中变化，不是用户点击，不切换
            return;
        }
        UiLogger.Log("媒体", "播放列表切换：目标=" + item.FullPath);
        _ = viewModel.OpenItemAsync(item.FullPath);
    }

    /// <summary>播放 / 暂停。</summary>
    private void OnPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.TogglePlayPause();
    }

    /// <summary>上一个媒体。</summary>
    private void OnPreviousClick(object? sender, RoutedEventArgs e)
    {
        MediaTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        _ = viewModel.PlayNeighborAsync(-1);
    }

    /// <summary>下一个媒体。</summary>
    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        MediaTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        _ = viewModel.PlayNeighborAsync(1);
    }

    /// <summary>开始拖动进度条。</summary>
    private void OnProgressPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        this.seeking = true;
    }

    /// <summary>结束拖动进度条：按松手位置跳转。</summary>
    private void OnProgressPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!this.seeking)
        {
            return;
        }
        this.seeking = false;
        MediaTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        viewModel.SeekTo(this.ProgressSlider.Value);
    }

    /// <summary>静音切换。</summary>
    private void OnMuteClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.ToggleMute();
    }

    /// <summary>倍速循环。</summary>
    private void OnRateClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.CycleRate();
    }

    /// <summary>显示 / 隐藏播放列表。</summary>
    private void OnTogglePlaylistClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.TogglePlaylist();
    }

    /// <summary>放大图片。</summary>
    private void OnZoomInClick(object? sender, RoutedEventArgs e)
    {
        this.ImageViewer.ZoomBy(1.25);
    }

    /// <summary>缩小图片。</summary>
    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
    {
        this.ImageViewer.ZoomBy(1.0 / 1.25);
    }

    /// <summary>旋转图片 90 度。</summary>
    private void OnRotateClick(object? sender, RoutedEventArgs e)
    {
        this.ImageViewer.Rotate(90);
    }

    /// <summary>图片适应窗口。</summary>
    private void OnFitClick(object? sender, RoutedEventArgs e)
    {
        this.ImageViewer.FitToWindow();
    }

    /// <summary>全屏 / 退出全屏。</summary>
    private void OnFullScreenClick(object? sender, RoutedEventArgs e)
    {
        this.ToggleFullScreen();
    }

    /// <summary>图片缩放变化：回写视图模型的百分比文本。</summary>
    private void OnImageViewerZoomChanged(object? sender, double percent)
    {
        MediaTabViewModel? viewModel = this.ViewModel;
        if (viewModel != null)
        {
            viewModel.ZoomPercent = percent;
        }
    }

    /// <summary>用系统默认程序打开当前媒体。</summary>
    private void OnOpenWithSystemClick(object? sender, RoutedEventArgs e)
    {
        MediaTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null || string.IsNullOrEmpty(viewModel.CurrentPath))
        {
            return;
        }
        try
        {
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.FileName = viewModel.CurrentPath;
            startInfo.UseShellExecute = true;
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception error)
        {
            UiLogger.Log("媒体", "用系统程序打开失败：路径=" + viewModel.CurrentPath + " 原因=" + error.Message);
        }
    }

    /// <summary>关闭当前媒体标签。</summary>
    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.RequestClose();
    }

    /// <summary>键盘快捷键：播放控制、切换媒体、图片缩放旋转。</summary>
    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        MediaTabViewModel? viewModel = this.ViewModel;
        if (viewModel == null)
        {
            return;
        }
        bool handled = true;
        switch (e.Key)
        {
            case Key.Space:
                viewModel.TogglePlayPause();
                break;
            case Key.Left:
                if (viewModel.IsImage)
                {
                    _ = viewModel.PlayNeighborAsync(-1);
                }
                else
                {
                    viewModel.SeekRelative(-5);
                }
                break;
            case Key.Right:
                if (viewModel.IsImage)
                {
                    _ = viewModel.PlayNeighborAsync(1);
                }
                else
                {
                    viewModel.SeekRelative(5);
                }
                break;
            case Key.PageUp:
                _ = viewModel.PlayNeighborAsync(-1);
                break;
            case Key.PageDown:
                _ = viewModel.PlayNeighborAsync(1);
                break;
            case Key.Up:
                if (viewModel.IsImage)
                {
                    this.ImageViewer.ZoomBy(1.25);
                }
                else
                {
                    viewModel.AdjustVolume(5);
                }
                break;
            case Key.Down:
                if (viewModel.IsImage)
                {
                    this.ImageViewer.ZoomBy(1.0 / 1.25);
                }
                else
                {
                    viewModel.AdjustVolume(-5);
                }
                break;
            case Key.M:
                viewModel.ToggleMute();
                break;
            case Key.R:
                this.ImageViewer.Rotate(90);
                break;
            case Key.D0:
            case Key.NumPad0:
                this.ImageViewer.FitToWindow();
                break;
            case Key.D1:
            case Key.NumPad1:
                this.ImageViewer.ActualSize();
                break;
            case Key.F:
                this.ToggleFullScreen();
                break;
            case Key.Escape:
                this.ExitFullScreen();
                break;
            default:
                handled = false;
                break;
        }
        if (handled)
        {
            e.Handled = true;
        }
    }

    /// <summary>在普通与全屏窗口状态之间切换。</summary>
    private void ToggleFullScreen()
    {
        Window? window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
        {
            return;
        }
        if (window.WindowState == WindowState.FullScreen)
        {
            window.WindowState = WindowState.Normal;
            return;
        }
        window.WindowState = WindowState.FullScreen;
    }

    /// <summary>退出全屏（Esc）。</summary>
    private void ExitFullScreen()
    {
        Window? window = TopLevel.GetTopLevel(this) as Window;
        if (window != null && window.WindowState == WindowState.FullScreen)
        {
            window.WindowState = WindowState.Normal;
        }
    }
}
