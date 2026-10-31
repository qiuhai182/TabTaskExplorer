using System;
using System.Collections.Generic;
using TabTaskExplorer.Kernel.Media;

namespace TabTaskExplorer.App.Services.Media;

/// <summary>
/// 空播放引擎：原生解码库缺失或构建时未包含音视频后端时使用。
/// 所有操作静默失败并把 <see cref="IsAvailable"/> 置为 false，界面据此提示用户改用系统默认程序。
/// </summary>
public sealed class NullMediaPlaybackEngine : IMediaPlaybackEngine
{
    private static readonly IReadOnlyList<MediaTrack> EmptyTracks = new List<MediaTrack>();

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

    /// <inheritdoc />
    public bool IsAvailable
    {
        get { return false; }
    }

    /// <inheritdoc />
    public bool IsPlaying
    {
        get { return false; }
    }

    /// <inheritdoc />
    public TimeSpan Position
    {
        get { return TimeSpan.Zero; }
    }

    /// <inheritdoc />
    public TimeSpan Duration
    {
        get { return TimeSpan.Zero; }
    }

    /// <inheritdoc />
    public int Volume { get; set; }

    /// <inheritdoc />
    public bool IsMuted { get; set; }

    /// <inheritdoc />
    public float Rate { get; set; } = 1.0f;

    /// <inheritdoc />
    public IReadOnlyList<MediaTrack> AudioTracks
    {
        get { return EmptyTracks; }
    }

    /// <inheritdoc />
    public IReadOnlyList<MediaTrack> SubtitleTracks
    {
        get { return EmptyTracks; }
    }

    /// <inheritdoc />
    public void Open(string path)
    {
        EventHandler<string>? handler = this.Failed;
        if (handler != null)
        {
            handler(this, "当前构建未包含音视频解码库，无法播放该文件，请改用系统默认程序打开。");
        }
    }

    /// <inheritdoc />
    public void Play()
    {
    }

    /// <inheritdoc />
    public void Pause()
    {
    }

    /// <inheritdoc />
    public void TogglePlayPause()
    {
    }

    /// <inheritdoc />
    public void Seek(TimeSpan position)
    {
    }

    /// <inheritdoc />
    public void AttachSurface(IMediaSurface surface)
    {
    }

    /// <inheritdoc />
    public void DetachSurface()
    {
    }

    /// <inheritdoc />
    public void SelectSubtitle(int trackId)
    {
    }

    /// <inheritdoc />
    public void SelectAudioTrack(int trackId)
    {
    }

    /// <inheritdoc />
    public void AddSubtitleFile(string path)
    {
    }

    /// <inheritdoc />
    public bool TakeSnapshot(string targetPath)
    {
        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // 无资源可释放
    }
}
