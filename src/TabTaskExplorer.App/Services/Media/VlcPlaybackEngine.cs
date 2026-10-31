using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using TabTaskExplorer.Kernel.Media;
using KernelMediaTrack = TabTaskExplorer.Kernel.Media.MediaTrack;
using VlcMedia = LibVLCSharp.Shared.Media;

namespace TabTaskExplorer.App.Services.Media;

/// <summary>
/// 基于 LibVLC 的播放引擎：音频与视频共用同一个 MediaPlayer，视频通过 <see cref="AttachSurface"/> 挂原生窗口。
/// 所有后端回调都在 libvlc 线程触发，这里统一收敛到 200ms 的 UI 定时器里再抛事件，
/// 订阅方因此可以安全地直接更新界面，不必各自做线程切换。
/// </summary>
public sealed class VlcPlaybackEngine : IMediaPlaybackEngine
{
    /// <summary>libvlc 初始化锁：全进程只初始化一次。</summary>
    private static readonly object InitLock = new object();

    /// <summary>共享的 LibVLC 实例（一个进程一个即可，MediaPlayer 才是每标签一个）。</summary>
    private static LibVLC? sharedLibVlc;

    /// <summary>是否已经尝试过初始化（失败后不再重试，避免反复弹错误）。</summary>
    private static bool initAttempted;

    /// <summary>初始化失败原因（供界面提示）。</summary>
    private static string initError = string.Empty;

    private readonly MediaPlayer? mediaPlayer;
    private readonly DispatcherTimer? timer;
    private VlcMedia? currentMedia;
    private IMediaSurface? surface;
    private long lastTime = -1;
    private long lastLength = -1;
    private bool lastPlaying;
    private bool pendingEndReached;
    private bool pendingError;
    private string pendingErrorMessage = string.Empty;
    private bool disposed;

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public event EventHandler<TimeSpan>? PositionChanged;

    /// <inheritdoc />
    public event EventHandler<TimeSpan>? DurationChanged;

    /// <inheritdoc />
    public event EventHandler<string>? Failed;

    /// <inheritdoc />
    public event EventHandler? EndReached;

    /// <summary>构造播放引擎：初始化 libvlc 并创建本标签专属的 MediaPlayer。</summary>
    public VlcPlaybackEngine()
    {
        EnsureLibVlcInitialized();
        if (sharedLibVlc == null)
        {
            return;
        }
        try
        {
            this.mediaPlayer = new MediaPlayer(sharedLibVlc);
            this.mediaPlayer.TimeChanged += this.OnVlcTimeChanged;
            this.mediaPlayer.LengthChanged += this.OnVlcLengthChanged;
            this.mediaPlayer.Playing += this.OnVlcPlaying;
            this.mediaPlayer.Paused += this.OnVlcPaused;
            this.mediaPlayer.Stopped += this.OnVlcStopped;
            this.mediaPlayer.EndReached += this.OnVlcEndReached;
            this.mediaPlayer.EncounteredError += this.OnVlcError;
            this.timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, this.OnTimerTick);
            this.timer.Start();
        }
        catch (Exception error)
        {
            initError = error.Message;
        }
    }

    /// <inheritdoc />
    public bool IsAvailable
    {
        get { return this.mediaPlayer != null; }
    }

    /// <summary>初始化失败原因（界面可展示，便于用户排查是否缺少原生库）。</summary>
    public static string InitializationError
    {
        get { return initError; }
    }

    /// <inheritdoc />
    public bool IsPlaying
    {
        get { return this.mediaPlayer != null && this.mediaPlayer.IsPlaying; }
    }

    /// <inheritdoc />
    public TimeSpan Position
    {
        get
        {
            if (this.mediaPlayer == null)
            {
                return TimeSpan.Zero;
            }
            long time = this.mediaPlayer.Time;
            return time <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(time);
        }
    }

    /// <inheritdoc />
    public TimeSpan Duration
    {
        get
        {
            if (this.mediaPlayer == null)
            {
                return TimeSpan.Zero;
            }
            long length = this.mediaPlayer.Length;
            return length <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(length);
        }
    }

    /// <inheritdoc />
    public int Volume
    {
        get { return this.mediaPlayer == null ? 0 : this.mediaPlayer.Volume; }
        set
        {
            if (this.mediaPlayer != null)
            {
                this.mediaPlayer.Volume = value < 0 ? 0 : (value > 100 ? 100 : value);
            }
        }
    }

    /// <inheritdoc />
    public bool IsMuted
    {
        get { return this.mediaPlayer != null && this.mediaPlayer.Mute; }
        set
        {
            if (this.mediaPlayer != null)
            {
                this.mediaPlayer.Mute = value;
            }
        }
    }

    /// <inheritdoc />
    public float Rate
    {
        get { return this.mediaPlayer == null ? 1.0f : this.mediaPlayer.Rate; }
        set
        {
            if (this.mediaPlayer != null && value > 0)
            {
                this.mediaPlayer.SetRate(value);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<KernelMediaTrack> AudioTracks
    {
        get { return this.ReadTracks(true); }
    }

    /// <inheritdoc />
    public IReadOnlyList<KernelMediaTrack> SubtitleTracks
    {
        get { return this.ReadTracks(false); }
    }

    /// <inheritdoc />
    public void Open(string path)
    {
        if (this.mediaPlayer == null || sharedLibVlc == null)
        {
            this.RaiseFailed("音视频解码库不可用（" + (initError.Length > 0 ? initError : "未找到 libvlc") + "），请改用系统默认程序打开。");
            return;
        }
        try
        {
            VlcMedia? previous = this.currentMedia;
            VlcMedia media = new VlcMedia(sharedLibVlc, path, FromType.FromPath);
            this.currentMedia = media;
            this.lastTime = -1;
            this.lastLength = -1;
            this.mediaPlayer.Play(media);
            if (previous != null)
            {
                previous.Dispose();
            }
            if (this.surface != null && this.surface.NativeHandle != IntPtr.Zero)
            {
                this.mediaPlayer.Hwnd = this.surface.NativeHandle;
            }
        }
        catch (Exception error)
        {
            this.RaiseFailed("无法打开该媒体：" + error.Message);
        }
    }

    /// <inheritdoc />
    public void Play()
    {
        if (this.mediaPlayer != null)
        {
            this.mediaPlayer.SetPause(false);
        }
    }

    /// <inheritdoc />
    public void Pause()
    {
        if (this.mediaPlayer != null)
        {
            this.mediaPlayer.SetPause(true);
        }
    }

    /// <inheritdoc />
    public void TogglePlayPause()
    {
        if (this.mediaPlayer == null)
        {
            return;
        }
        if (this.mediaPlayer.IsPlaying)
        {
            this.mediaPlayer.SetPause(true);
        }
        else
        {
            this.mediaPlayer.SetPause(false);
        }
    }

    /// <inheritdoc />
    public void Seek(TimeSpan position)
    {
        if (this.mediaPlayer == null)
        {
            return;
        }
        long milliseconds = (long)position.TotalMilliseconds;
        if (milliseconds < 0)
        {
            milliseconds = 0;
        }
        long length = this.mediaPlayer.Length;
        if (length > 0 && milliseconds > length)
        {
            milliseconds = length;
        }
        this.mediaPlayer.Time = milliseconds;
    }

    /// <inheritdoc />
    public void AttachSurface(IMediaSurface target)
    {
        if (this.surface != null)
        {
            this.surface.HandleChanged -= this.OnSurfaceHandleChanged;
        }
        this.surface = target;
        if (target != null)
        {
            target.HandleChanged += this.OnSurfaceHandleChanged;
            this.ApplySurfaceHandle();
        }
    }

    /// <inheritdoc />
    public void DetachSurface()
    {
        if (this.surface != null)
        {
            this.surface.HandleChanged -= this.OnSurfaceHandleChanged;
            this.surface = null;
        }
    }

    /// <inheritdoc />
    public void SelectSubtitle(int trackId)
    {
        if (this.mediaPlayer != null)
        {
            this.mediaPlayer.SetSpu(trackId);
        }
    }

    /// <inheritdoc />
    public void SelectAudioTrack(int trackId)
    {
        if (this.mediaPlayer != null)
        {
            this.mediaPlayer.SetAudioTrack(trackId);
        }
    }

    /// <inheritdoc />
    public void AddSubtitleFile(string path)
    {
        if (this.mediaPlayer == null || string.IsNullOrEmpty(path))
        {
            return;
        }
        try
        {
            this.mediaPlayer.AddSlave(MediaSlaveType.Subtitle, new Uri(path).AbsoluteUri, true);
        }
        catch (Exception error)
        {
            this.RaiseFailed("字幕加载失败：" + error.Message);
        }
    }

    /// <inheritdoc />
    public bool TakeSnapshot(string targetPath)
    {
        if (this.mediaPlayer == null || string.IsNullOrEmpty(targetPath))
        {
            return false;
        }
        try
        {
            return this.mediaPlayer.TakeSnapshot(0, targetPath, 0, 0);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }
        this.disposed = true;
        this.DetachSurface();
        if (this.timer != null)
        {
            this.timer.Stop();
        }
        if (this.mediaPlayer != null)
        {
            this.mediaPlayer.TimeChanged -= this.OnVlcTimeChanged;
            this.mediaPlayer.LengthChanged -= this.OnVlcLengthChanged;
            this.mediaPlayer.Playing -= this.OnVlcPlaying;
            this.mediaPlayer.Paused -= this.OnVlcPaused;
            this.mediaPlayer.Stopped -= this.OnVlcStopped;
            this.mediaPlayer.EndReached -= this.OnVlcEndReached;
            this.mediaPlayer.EncounteredError -= this.OnVlcError;
            try
            {
                this.mediaPlayer.Stop();
            }
            catch (Exception)
            {
                // 停止失败不影响释放流程
            }
            this.mediaPlayer.Dispose();
        }
        if (this.currentMedia != null)
        {
            this.currentMedia.Dispose();
            this.currentMedia = null;
        }
    }

    /// <summary>初始化 libvlc：Windows 优先用随包分发的 libvlc 目录，其他平台用系统安装的库。</summary>
    private static void EnsureLibVlcInitialized()
    {
        lock (InitLock)
        {
            if (initAttempted)
            {
                return;
            }
            initAttempted = true;
            try
            {
                string? directory = ResolveLibVlcDirectory();
                if (directory != null)
                {
                    Core.Initialize(directory);
                }
                else
                {
                    Core.Initialize();
                }
                sharedLibVlc = new LibVLC("--no-video-title-show", "--quiet", "--avcodec-hw=any");
            }
            catch (Exception error)
            {
                initError = error.Message;
                sharedLibVlc = null;
            }
        }
    }

    /// <summary>定位随包分发的 libvlc 目录（找不到返回 null，交给 Core 自行搜索系统库）。</summary>
    private static string? ResolveLibVlcDirectory()
    {
        string baseDirectory = AppContext.BaseDirectory;
        if (OperatingSystem.IsWindows())
        {
            string[] candidates = new string[]
            {
                Path.Combine(baseDirectory, "libvlc", "win-x64"),
                Path.Combine(baseDirectory, "libvlc", "win-x86"),
                Path.Combine(baseDirectory, "libvlc"),
                baseDirectory,
            };
            for (int index = 0; index < candidates.Length; index++)
            {
                if (File.Exists(Path.Combine(candidates[index], "libvlc.dll")))
                {
                    return candidates[index];
                }
            }
            return null;
        }
        return null;
    }

    /// <summary>读取音轨或字幕轨清单（后端未就绪时返回空列表）。</summary>
    private IReadOnlyList<KernelMediaTrack> ReadTracks(bool audio)
    {
        List<KernelMediaTrack> tracks = new List<KernelMediaTrack>();
        if (this.mediaPlayer == null)
        {
            return tracks;
        }
        try
        {
            if (audio)
            {
                for (int index = 0; index < this.mediaPlayer.AudioTrackCount; index++)
                {
                    KernelMediaTrack track = new KernelMediaTrack();
                    track.Id = this.mediaPlayer.AudioTrackDescription[index].Id;
                    track.Name = this.mediaPlayer.AudioTrackDescription[index].Name;
                    tracks.Add(track);
                }
            }
            else
            {
                for (int index = 0; index < this.mediaPlayer.SpuCount; index++)
                {
                    KernelMediaTrack track = new KernelMediaTrack();
                    track.Id = this.mediaPlayer.SpuDescription[index].Id;
                    track.Name = this.mediaPlayer.SpuDescription[index].Name;
                    tracks.Add(track);
                }
            }
        }
        catch (Exception)
        {
            // 轨道信息在媒体就绪前不可读，返回已读到的部分
        }
        return tracks;
    }

    /// <summary>渲染面句柄就绪 / 变化：重新挂载到 MediaPlayer。</summary>
    private void OnSurfaceHandleChanged(object? sender, EventArgs e)
    {
        this.ApplySurfaceHandle();
    }

    /// <summary>把渲染面句柄写入 MediaPlayer（视频才会用到）。</summary>
    private void ApplySurfaceHandle()
    {
        if (this.mediaPlayer == null || this.surface == null)
        {
            return;
        }
        IntPtr handle = this.surface.NativeHandle;
        if (handle == IntPtr.Zero)
        {
            return;
        }
        this.mediaPlayer.Hwnd = handle;
    }

    /// <summary>libvlc 时间回调：只记录，由定时器统一抛给界面。</summary>
    private void OnVlcTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        this.lastTime = e.Time;
    }

    /// <summary>libvlc 时长回调：只记录。</summary>
    private void OnVlcLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        this.lastLength = e.Length;
    }

    /// <summary>开始播放回调。</summary>
    private void OnVlcPlaying(object? sender, EventArgs e)
    {
        this.lastPlaying = true;
    }

    /// <summary>暂停回调。</summary>
    private void OnVlcPaused(object? sender, EventArgs e)
    {
        this.lastPlaying = false;
    }

    /// <summary>停止回调。</summary>
    private void OnVlcStopped(object? sender, EventArgs e)
    {
        this.lastPlaying = false;
    }

    /// <summary>播放结束回调：置标志，由定时器在 UI 线程抛出（回调线程内不能直接操作播放器）。</summary>
    private void OnVlcEndReached(object? sender, EventArgs e)
    {
        this.pendingEndReached = true;
    }

    /// <summary>解码错误回调。</summary>
    private void OnVlcError(object? sender, EventArgs e)
    {
        this.pendingError = true;
        this.pendingErrorMessage = "解码失败：文件可能已损坏，或当前后端不支持该编码格式。";
    }

    /// <summary>UI 定时器：把后端状态变化统一抛给订阅方（天然节流到 200ms）。</summary>
    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (this.disposed || this.mediaPlayer == null)
        {
            return;
        }
        if (this.pendingError)
        {
            this.pendingError = false;
            this.RaiseFailed(this.pendingErrorMessage);
        }
        if (this.pendingEndReached)
        {
            this.pendingEndReached = false;
            this.lastPlaying = false;
            EventHandler? endHandler = this.EndReached;
            if (endHandler != null)
            {
                endHandler(this, EventArgs.Empty);
            }
            return;
        }
        bool playing = this.mediaPlayer.IsPlaying;
        if (playing != this.lastPlaying)
        {
            this.lastPlaying = playing;
            EventHandler? stateHandler = this.StateChanged;
            if (stateHandler != null)
            {
                stateHandler(this, EventArgs.Empty);
            }
        }
        long time = this.lastTime;
        if (time >= 0)
        {
            EventHandler<TimeSpan>? positionHandler = this.PositionChanged;
            if (positionHandler != null)
            {
                positionHandler(this, TimeSpan.FromMilliseconds(time));
            }
        }
        long length = this.lastLength;
        if (length > 0)
        {
            EventHandler<TimeSpan>? durationHandler = this.DurationChanged;
            if (durationHandler != null)
            {
                durationHandler(this, TimeSpan.FromMilliseconds(length));
            }
        }
    }

    /// <summary>抛出失败事件（已在 UI 线程时直接抛）。</summary>
    private void RaiseFailed(string message)
    {
        EventHandler<string>? handler = this.Failed;
        if (handler != null)
        {
            handler(this, message);
        }
    }
}
