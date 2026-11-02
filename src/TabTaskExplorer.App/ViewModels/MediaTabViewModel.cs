using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TabTaskExplorer.App.Services;
using TabTaskExplorer.Kernel.Media;

namespace TabTaskExplorer.App.ViewModels;

/// <summary>
/// 媒体标签页：一个标签对应一个正在查看 / 播放的媒体文件，右侧播放列表展示同目录媒体。
/// 图片走 Avalonia 位图渲染，视频与音频共用 <see cref="IMediaPlaybackEngine"/>；
/// 引擎按需懒创建，因此纯图片标签不占用任何解码器资源。
/// </summary>
public class MediaTabViewModel : INotifyPropertyChanged, ITabView, IDisposable
{
    private readonly AppSettings settings;
    private readonly Func<IMediaPlaybackEngine?>? engineFactory;

    /// <summary>播放列表（同目录媒体）。</summary>
    private readonly MediaPlaylistViewModel playlist = new MediaPlaylistViewModel();

    private IMediaPlaybackEngine? engine;
    private IMediaSurface? surface;
    private Bitmap? imageSource;
    private string currentPath = string.Empty;
    private string title = "媒体";
    private MediaKind mediaType = MediaKind.Image;
    private bool playlistVisible = true;
    private bool isPlaying;
    private bool hasError;
    private string errorText = string.Empty;
    private string statusText = string.Empty;
    private string positionText = "00:00";
    private string durationText = "00:00";
    private double progress;
    private int volume = 80;
    private bool isMuted;
    private float rate = 1.0f;
    private string imageInfoText = string.Empty;
    private double zoomPercent = 100;
    private bool isBusy;
    private bool isDisposed;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>请求关闭本标签（图片加载失败等场景由界面层决定是否关闭）。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>使用设置、引擎工厂与初始文件创建媒体标签。</summary>
    public MediaTabViewModel(AppSettings settings, Func<IMediaPlaybackEngine?>? engineFactory,
        string initialPath, MediaKind initialKind)
    {
        this.settings = settings;
        this.engineFactory = engineFactory;
        this.playlistVisible = settings.MediaShowPlaylist;
        this.volume = settings.MediaVolume;
        this.rate = (float)settings.MediaRate;
        this.playlist.ItemActivated += this.OnPlaylistItemActivated;
        this.currentPath = NormalizePath(initialPath);
        this.title = BuildTitle(this.currentPath, initialKind);
        this.mediaType = initialKind;
    }

    /// <summary>标签类型标识：媒体。</summary>
    public string Kind
    {
        get { return TabKinds.Media; }
    }

    /// <summary>把媒体状态写入网页端快照（网页端暂不渲染媒体内容，仅展示路径与摘要）。</summary>
    public void FillSnapshot(DebugTabState tabState)
    {
        TabSnapshotFiller.FillMedia(this, tabState);
    }

    /// <summary>标签标题。</summary>
    public string Title
    {
        get { return this.title; }
        private set
        {
            this.title = value;
            this.RaisePropertyChanged(nameof(Title));
        }
    }

    /// <summary>当前媒体的大类。</summary>
    public MediaKind MediaType
    {
        get { return this.mediaType; }
        private set
        {
            this.mediaType = value;
            this.RaisePropertyChanged(nameof(MediaType));
            this.RaisePropertyChanged(nameof(IsImage));
            this.RaisePropertyChanged(nameof(IsVideo));
            this.RaisePropertyChanged(nameof(IsAudio));
            this.RaisePropertyChanged(nameof(IsPlayableMedia));
        }
    }

    /// <summary>当前媒体文件的完整路径。</summary>
    public string CurrentPath
    {
        get { return this.currentPath; }
        private set
        {
            this.currentPath = value;
            this.RaisePropertyChanged(nameof(CurrentPath));
            this.RaisePropertyChanged(nameof(CurrentFileName));
            this.RaisePropertyChanged(nameof(CurrentTypeText));
        }
    }

    /// <summary>当前文件名。</summary>
    public string CurrentFileName
    {
        get { return Path.GetFileName(this.currentPath); }
    }

    /// <summary>当前类型文本（"JPEG 图片"）。</summary>
    public string CurrentTypeText
    {
        get { return MediaFormatCatalog.DescribeType(this.currentPath); }
    }

    /// <summary>是否图片。</summary>
    public bool IsImage
    {
        get { return this.mediaType == MediaKind.Image; }
    }

    /// <summary>是否视频。</summary>
    public bool IsVideo
    {
        get { return this.mediaType == MediaKind.Video; }
    }

    /// <summary>是否音频。</summary>
    public bool IsAudio
    {
        get { return this.mediaType == MediaKind.Audio; }
    }

    /// <summary>是否需要播放引擎（视频 / 音频）。</summary>
    public bool IsPlayableMedia
    {
        get { return this.mediaType == MediaKind.Video || this.mediaType == MediaKind.Audio; }
    }

    /// <summary>当前图片位图（仅图片标签有值）。</summary>
    public Bitmap? ImageSource
    {
        get { return this.imageSource; }
        private set
        {
            this.imageSource = value;
            this.RaisePropertyChanged(nameof(ImageSource));
            this.RaisePropertyChanged(nameof(HasImage));
        }
    }

    /// <summary>是否已加载出图片。</summary>
    public bool HasImage
    {
        get { return this.imageSource != null; }
    }

    /// <summary>图片信息文本（"1920 × 1080 · 2.4 MB · 100%"）。</summary>
    public string ImageInfoText
    {
        get { return this.imageInfoText; }
        private set
        {
            this.imageInfoText = value;
            this.RaisePropertyChanged(nameof(ImageInfoText));
        }
    }

    /// <summary>当前缩放百分比（界面显示）。</summary>
    public double ZoomPercent
    {
        get { return this.zoomPercent; }
        set
        {
            this.zoomPercent = value;
            this.RaisePropertyChanged(nameof(ZoomPercent));
        }
    }

    /// <summary>播放列表。</summary>
    public MediaPlaylistViewModel Playlist
    {
        get { return this.playlist; }
    }

    /// <summary>是否显示播放列表侧边栏。</summary>
    public bool PlaylistVisible
    {
        get { return this.playlistVisible; }
        set
        {
            this.playlistVisible = value;
            this.RaisePropertyChanged(nameof(PlaylistVisible));
        }
    }

    /// <summary>播放列表侧边栏宽度（像素，来自设置，非法值回退 260）。</summary>
    public double PlaylistWidth
    {
        get
        {
            double width = this.settings.MediaPlaylistWidth;
            return width < 180 || width > 480 ? 260 : width;
        }
    }

    /// <summary>是否正在播放。</summary>
    public bool IsPlaying
    {
        get { return this.isPlaying; }
        private set
        {
            this.isPlaying = value;
            this.RaisePropertyChanged(nameof(IsPlaying));
            this.RaisePropertyChanged(nameof(PlayIconKind));
        }
    }

    /// <summary>播放 / 暂停按钮图标键。</summary>
    public string PlayIconKind
    {
        get { return this.isPlaying ? "pause" : "play"; }
    }

    /// <summary>是否有错误提示。</summary>
    public bool HasError
    {
        get { return this.hasError; }
        private set
        {
            this.hasError = value;
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    /// <summary>错误提示文本。</summary>
    public string ErrorText
    {
        get { return this.errorText; }
        private set
        {
            this.errorText = value;
            this.RaisePropertyChanged(nameof(ErrorText));
        }
    }

    /// <summary>状态栏提示文本。</summary>
    public string StatusText
    {
        get { return this.statusText; }
        private set
        {
            this.statusText = value;
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>当前进度文本（00:12）。</summary>
    public string PositionText
    {
        get { return this.positionText; }
        private set
        {
            this.positionText = value;
            this.RaisePropertyChanged(nameof(PositionText));
        }
    }

    /// <summary>总时长文本（03:45）。</summary>
    public string DurationText
    {
        get { return this.durationText; }
        private set
        {
            this.durationText = value;
            this.RaisePropertyChanged(nameof(DurationText));
        }
    }

    /// <summary>播放进度（0-1，进度条绑定）。</summary>
    public double Progress
    {
        get { return this.progress; }
        set
        {
            if (Math.Abs(this.progress - value) < 0.0005)
            {
                return;
            }
            this.progress = value;
            this.RaisePropertyChanged(nameof(Progress));
        }
    }

    /// <summary>音量（0-100）。</summary>
    public int Volume
    {
        get { return this.volume; }
        set
        {
            int clamped = value < 0 ? 0 : (value > 100 ? 100 : value);
            if (this.volume == clamped)
            {
                return;
            }
            this.volume = clamped;
            if (this.engine != null)
            {
                this.engine.Volume = clamped;
            }
            this.settings.MediaVolume = clamped;
            this.RaisePropertyChanged(nameof(Volume));
            this.RaisePropertyChanged(nameof(VolumeIconKind));
        }
    }

    /// <summary>是否静音。</summary>
    public bool IsMuted
    {
        get { return this.isMuted; }
        private set
        {
            this.isMuted = value;
            if (this.engine != null)
            {
                this.engine.IsMuted = value;
            }
            this.RaisePropertyChanged(nameof(IsMuted));
            this.RaisePropertyChanged(nameof(VolumeIconKind));
        }
    }

    /// <summary>音量按钮图标键。</summary>
    public string VolumeIconKind
    {
        get
        {
            if (this.isMuted || this.volume <= 0)
            {
                return "mute";
            }
            return this.volume < 50 ? "volumelow" : "volumehigh";
        }
    }

    /// <summary>播放倍速。</summary>
    public float Rate
    {
        get { return this.rate; }
        private set
        {
            this.rate = value;
            this.settings.MediaRate = value;
            if (this.engine != null)
            {
                this.engine.Rate = value;
            }
            this.RaisePropertyChanged(nameof(Rate));
            this.RaisePropertyChanged(nameof(RateText));
        }
    }

    /// <summary>倍速文本（1.0×）。</summary>
    public string RateText
    {
        get { return this.rate.ToString("0.##") + "×"; }
    }

    /// <summary>是否正在加载（显示转圈提示）。</summary>
    public bool IsBusy
    {
        get { return this.isBusy; }
        private set
        {
            this.isBusy = value;
            this.RaisePropertyChanged(nameof(IsBusy));
        }
    }

    /// <summary>播放引擎是否可用（原生库缺失时为 false）。</summary>
    public bool IsEngineAvailable
    {
        get
        {
            if (this.mediaType == MediaKind.Image)
            {
                return true;
            }
            return this.engine != null && this.engine.IsAvailable;
        }
    }

    /// <summary>首次装载：加载当前文件并扫描同目录播放列表。</summary>
    public async Task LoadAsync()
    {
        if (string.IsNullOrEmpty(this.currentPath))
        {
            return;
        }
        await this.SwitchToAsync(this.currentPath, true);
    }

    /// <summary>切换到指定媒体文件（同一标签内切换，不新开标签）。</summary>
    public async Task SwitchToAsync(string path, bool forceReload)
    {
        if (this.isDisposed || string.IsNullOrEmpty(path))
        {
            return;
        }
        path = NormalizePath(path);
        if (!forceReload && string.Equals(path, this.currentPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        MediaKind kind = MediaFormatCatalog.Classify(path);
        if (kind == MediaKind.Unknown || !MediaFormatCatalog.IsPlayable(path))
        {
            this.ShowError("不支持的媒体格式：" + Path.GetFileName(path) + "，请用系统默认程序打开。");
            return;
        }
        bool kindChanged = kind != this.mediaType;
        this.ClearError();
        this.CurrentPath = path;
        this.MediaType = kind;
        this.Title = BuildTitle(path, kind);
        this.IsBusy = true;
        try
        {
            if (kind == MediaKind.Image)
            {
                this.StopEngine();
                await this.LoadImageAsync(path);
            }
            else
            {
                this.ReleaseImage();
                this.StartPlayback(path);
            }
            if (kindChanged || this.playlist.Items.Count == 0)
            {
                await this.ReloadPlaylistAsync();
            }
            else
            {
                this.playlist.SetCurrentByPath(path);
            }
        }
        catch (Exception error)
        {
            this.ShowError("打开失败：" + error.Message);
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    /// <summary>重新扫描播放列表（切换范围 / 排序或目录变化时调用）。</summary>
    public async Task ReloadPlaylistAsync()
    {
        string directory = Path.GetDirectoryName(this.currentPath) ?? string.Empty;
        MediaPlaylistScope scope = string.Equals(this.settings.MediaPlaylistScope, "AllMedia", StringComparison.OrdinalIgnoreCase)
            ? MediaPlaylistScope.AllMedia
            : MediaPlaylistScope.SameKind;
        MediaPlaylistSort sort = MediaPlaylistSort.Name;
        await this.playlist.LoadAsync(directory, this.mediaType, scope, sort, this.currentPath);
    }

    /// <summary>播放 / 暂停切换。</summary>
    public void TogglePlayPause()
    {
        if (!this.IsPlayableMedia)
        {
            return;
        }
        IMediaPlaybackEngine? playbackEngine = this.EnsureEngine();
        if (playbackEngine == null || !playbackEngine.IsAvailable)
        {
            this.ShowError("音视频引擎不可用，请用系统默认程序打开该文件。");
            return;
        }
        playbackEngine.TogglePlayPause();
    }

    /// <summary>跳转到指定比例位置（0-1）。</summary>
    public void SeekTo(double ratio)
    {
        if (this.engine == null || !this.IsPlayableMedia)
        {
            return;
        }
        double clamped = ratio < 0 ? 0 : (ratio > 1 ? 1 : ratio);
        this.Progress = clamped;
        this.engine.Seek(TimeSpan.FromTicks((long)(this.engine.Duration.Ticks * clamped)));
    }

    /// <summary>相对跳转（秒）。</summary>
    public void SeekRelative(double seconds)
    {
        if (this.engine == null)
        {
            return;
        }
        TimeSpan target = this.engine.Position + TimeSpan.FromSeconds(seconds);
        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }
        if (this.engine.Duration > TimeSpan.Zero && target > this.engine.Duration)
        {
            target = this.engine.Duration;
        }
        this.engine.Seek(target);
    }

    /// <summary>静音切换。</summary>
    public void ToggleMute()
    {
        this.IsMuted = !this.isMuted;
    }

    /// <summary>在常用倍速之间循环（0.5 / 1.0 / 1.25 / 1.5 / 2.0）。</summary>
    public void CycleRate()
    {
        float[] rates = new float[] { 0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f };
        int currentIndex = 2;
        for (int index = 0; index < rates.Length; index++)
        {
            if (Math.Abs(rates[index] - this.rate) < 0.001f)
            {
                currentIndex = index;
                break;
            }
        }
        this.Rate = rates[(currentIndex + 1) % rates.Length];
    }

    /// <summary>音量增减（百分点）。</summary>
    public void AdjustVolume(int delta)
    {
        this.Volume = this.volume + delta;
    }

    /// <summary>切换到播放列表的上一项 / 下一项。</summary>
    public async Task PlayNeighborAsync(int offset)
    {
        MediaPlaylistItemViewModel? neighbor = this.playlist.GetNeighbor(offset);
        if (neighbor == null)
        {
            return;
        }
        await this.SwitchToAsync(neighbor.FullPath, false);
    }

    /// <summary>按路径切换（供界面播放列表点击调用）。</summary>
    public async Task OpenItemAsync(string path)
    {
        await this.SwitchToAsync(path, false);
    }

    /// <summary>把播放列表显隐取反。</summary>
    public void TogglePlaylist()
    {
        this.PlaylistVisible = !this.playlistVisible;
        this.settings.MediaShowPlaylist = this.playlistVisible;
    }

    /// <summary>
    /// 挂载视频渲染面（由界面在面板挂载到视觉树时调用）。
    /// 音频与图片标签同样会调用，但只有视频会把句柄交给引擎。
    /// </summary>
    public void AttachSurface(IMediaSurface target)
    {
        this.surface = target;
        if (this.engine != null && this.mediaType == MediaKind.Video)
        {
            this.engine.AttachSurface(target);
        }
    }

    /// <summary>卸载视频渲染面（面板离开视觉树时调用）。</summary>
    public void DetachSurface()
    {
        this.surface = null;
        if (this.engine != null)
        {
            this.engine.DetachSurface();
        }
    }

    /// <summary>释放引擎与位图（关闭标签时调用）。</summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }
        this.isDisposed = true;
        this.playlist.ItemActivated -= this.OnPlaylistItemActivated;
        this.playlist.Clear();
        this.StopEngine();
        this.ReleaseImage();
    }

    /// <summary>请求关闭标签（供界面错误提示中的按钮调用）。</summary>
    public void RequestClose()
    {
        EventHandler? handler = this.CloseRequested;
        if (handler != null)
        {
            handler(this, EventArgs.Empty);
        }
    }

    /// <summary>播放列表点击：在当前标签内切换媒体。</summary>
    private async void OnPlaylistItemActivated(object? sender, MediaPlaylistItemViewModel item)
    {
        await this.SwitchToAsync(item.FullPath, false);
    }

    /// <summary>加载图片：超过解码上限时降采样，避免超大图撑爆内存。</summary>
    private async Task LoadImageAsync(string path)
    {
        int maxSize = this.settings.MediaImageMaxDecodeSize;
        Bitmap? bitmap = await Task.Run(delegate
        {
            return DecodeBitmap(path, maxSize);
        });
        if (bitmap == null)
        {
            this.ShowError("无法解码该图片：" + Path.GetFileName(path));
            return;
        }
        this.ReleaseImage();
        this.ImageSource = bitmap;
        this.ZoomPercent = 100;
        long sizeBytes = 0;
        try
        {
            FileInfo info = new FileInfo(path);
            if (info.Exists)
            {
                sizeBytes = info.Length;
            }
        }
        catch (Exception)
        {
            sizeBytes = 0;
        }
        this.ImageInfoText = bitmap.PixelSize.Width + " × " + bitmap.PixelSize.Height
            + " · " + MediaItem.FormatSize(sizeBytes);
        this.StatusText = string.Empty;
    }

    /// <summary>解码位图：宽度超过上限时按比例降采样（Skia 缩放解码，不占原图内存）。</summary>
    private static Bitmap? DecodeBitmap(string path, int maxSize)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            Bitmap probe = new Bitmap(stream);
            if (maxSize > 0 && probe.PixelSize.Width > maxSize)
            {
                probe.Dispose();
                stream.Position = 0;
                return Bitmap.DecodeToWidth(stream, maxSize, BitmapInterpolationMode.HighQuality);
            }
            return probe;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>启动音视频播放。</summary>
    private void StartPlayback(string path)
    {
        IMediaPlaybackEngine? playbackEngine = this.EnsureEngine();
        if (playbackEngine == null || !playbackEngine.IsAvailable)
        {
            this.ShowError("音视频引擎不可用，请用系统默认程序打开该文件。");
            return;
        }
        playbackEngine.Volume = this.volume;
        playbackEngine.Rate = this.rate;
        if (this.mediaType == MediaKind.Video && this.surface != null)
        {
            playbackEngine.AttachSurface(this.surface);
        }
        playbackEngine.Open(path);
        this.StatusText = string.Empty;
        UiLogger.Log("媒体", "开始播放：路径=" + path + " 引擎可用=" + playbackEngine.IsAvailable
            + " 时长=" + MediaTabViewModel.FormatTime(playbackEngine.Duration));
    }

    /// <summary>懒创建播放引擎并订阅事件（图片标签永不创建）。</summary>
    private IMediaPlaybackEngine? EnsureEngine()
    {
        if (this.engine != null)
        {
            return this.engine;
        }
        if (this.engineFactory == null)
        {
            return null;
        }
        this.engine = this.engineFactory();
        if (this.engine == null)
        {
            return null;
        }
        this.engine.StateChanged += this.OnEngineStateChanged;
        this.engine.PositionChanged += this.OnEnginePositionChanged;
        this.engine.DurationChanged += this.OnEngineDurationChanged;
        this.engine.Failed += this.OnEngineFailed;
        this.engine.EndReached += this.OnEngineEndReached;
        this.engine.Volume = this.volume;
        this.engine.Rate = this.rate;
        return this.engine;
    }

    /// <summary>停止并释放引擎（切换到图片时调用）。</summary>
    private void StopEngine()
    {
        if (this.engine == null)
        {
            return;
        }
        this.engine.StateChanged -= this.OnEngineStateChanged;
        this.engine.PositionChanged -= this.OnEnginePositionChanged;
        this.engine.DurationChanged -= this.OnEngineDurationChanged;
        this.engine.Failed -= this.OnEngineFailed;
        this.engine.EndReached -= this.OnEngineEndReached;
        this.engine.Dispose();
        this.engine = null;
        this.IsPlaying = false;
        this.PositionText = "00:00";
        this.DurationText = "00:00";
        this.Progress = 0;
    }

    /// <summary>释放当前位图。</summary>
    private void ReleaseImage()
    {
        if (this.imageSource != null)
        {
            this.imageSource.Dispose();
            this.ImageSource = null;
        }
        this.ImageInfoText = string.Empty;
    }

    /// <summary>引擎状态变化：回到 UI 线程刷新按钮字形。</summary>
    private void OnEngineStateChanged(object? sender, EventArgs e)
    {
        IMediaPlaybackEngine? playbackEngine = this.engine;
        if (playbackEngine == null)
        {
            return;
        }
        bool playing = playbackEngine.IsPlaying;
        Dispatcher.UIThread.Post(delegate
        {
            this.IsPlaying = playing;
        });
    }

    /// <summary>进度变化：刷新进度条与时间文本。</summary>
    private void OnEnginePositionChanged(object? sender, TimeSpan position)
    {
        IMediaPlaybackEngine? playbackEngine = this.engine;
        if (playbackEngine == null)
        {
            return;
        }
        TimeSpan duration = playbackEngine.Duration;
        string positionLabel = FormatTime(position);
        string durationLabel = FormatTime(duration);
        double ratio = duration > TimeSpan.Zero ? position.TotalMilliseconds / duration.TotalMilliseconds : 0;
        Dispatcher.UIThread.Post(delegate
        {
            this.PositionText = positionLabel;
            this.DurationText = durationLabel;
            this.Progress = ratio < 0 ? 0 : (ratio > 1 ? 1 : ratio);
        });
    }

    /// <summary>总时长就绪。</summary>
    private void OnEngineDurationChanged(object? sender, TimeSpan duration)
    {
        string durationLabel = FormatTime(duration);
        if (duration > TimeSpan.Zero && this.durationText == "00:00")
        {
            UiLogger.Log("媒体", "解码就绪：路径=" + this.currentPath + " 时长=" + durationLabel
                + " 音轨数=" + (this.engine != null ? this.engine.AudioTracks.Count : 0));
        }
        Dispatcher.UIThread.Post(delegate
        {
            this.DurationText = durationLabel;
        });
    }

    /// <summary>引擎报错。</summary>
    private void OnEngineFailed(object? sender, string message)
    {
        Dispatcher.UIThread.Post(delegate
        {
            this.ShowError(message);
        });
    }

    /// <summary>播放自然结束：按设置自动播放下一项。</summary>
    private void OnEngineEndReached(object? sender, EventArgs e)
    {
        if (!this.settings.MediaAutoPlayNext)
        {
            return;
        }
        Dispatcher.UIThread.Post(delegate
        {
            _ = this.PlayNeighborAsync(1);
        });
    }

    /// <summary>显示错误并清空播放状态。</summary>
    private void ShowError(string message)
    {
        UiLogger.Log("媒体", "播放失败：路径=" + this.currentPath + " 原因=" + message);
        this.ErrorText = message;
        this.HasError = true;
        this.IsPlaying = false;
        this.IsBusy = false;
    }

    /// <summary>清除错误提示。</summary>
    private void ClearError()
    {
        if (!this.hasError)
        {
            return;
        }
        this.HasError = false;
        this.ErrorText = string.Empty;
    }

    /// <summary>
    /// 规范化路径：正斜杠 / 相对路径统一为绝对路径的反斜杠形式，
    /// 否则播放列表扫描结果（系统返回反斜杠）与传入路径（可能带正斜杠）比较不相等，当前项无法高亮。
    /// </summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>生成标签标题："文件名 - 图片"。</summary>
    private static string BuildTitle(string path, MediaKind kind)
    {
        string name = string.IsNullOrEmpty(path) ? "媒体" : Path.GetFileName(path);
        return name + " - " + MediaFormatCatalog.DescribeKind(kind);
    }

    /// <summary>把时长格式化为 mm:ss 或 h:mm:ss。</summary>
    public static string FormatTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }
        if (value.TotalHours >= 1)
        {
            return ((int)value.TotalHours).ToString("0") + ":" + value.Minutes.ToString("00") + ":" + value.Seconds.ToString("00");
        }
        return value.Minutes.ToString("00") + ":" + value.Seconds.ToString("00");
    }

    /// <summary>触发属性变更通知。</summary>
    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChangedEventHandler? handler = this.PropertyChanged;
        if (handler != null)
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
